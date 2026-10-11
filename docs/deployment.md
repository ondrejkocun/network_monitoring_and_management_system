# Running the system on more than one computer

The steps in the README run everything on one computer. This guide puts the server on one computer and agents on others, as in the model network: the server gets its own certificate, and every agent is installed as a Windows service that starts with Windows.

## How agents trust the server

The server uses a self-signed certificate, and each client is given its **pin**: the SHA-256 hash of that certificate. A client with a pin talks only to a server that presents exactly this certificate.

This was chosen over a private certificate authority for three reasons:

- Nothing has to be installed into the trusted roots of every monitored computer; the pin is one setting of the agent.
- A pinned client refuses any other certificate, even one Windows would trust, so a certificate issued by someone else cannot be used to impersonate the server.
- There is no authority whose key would have to be guarded.

The price is that a new server certificate means giving every agent the new pin. For a network with many computers and a certificate authority already in place, leave the pin empty: the agent then accepts a certificate Windows trusts for the server's name.

## 1. Server

Do this on the computer that runs the server and the database.

### Create the certificate

In PowerShell, under the account that will run the server:

```powershell
.\scripts\New-ServerCertificate.ps1 -DnsName SERVER01 -IpAddress 192.168.50.10
```

Use the name and the address other computers will reach the server by. The script stores the certificate in your personal certificate store, with a private key that cannot be exported, and prints the pin. Keep the pin; the agents and the desktop application need it.

### Open the port

In PowerShell as administrator:

```powershell
New-NetFirewallRule -DisplayName "Network Monitoring System server" -Direction Inbound -Protocol TCP -LocalPort 7006 -Action Allow -Profile Private,Domain
```

### Start the server

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:Kestrel__Endpoints__Https__Url = "https://0.0.0.0:7006"
$env:Kestrel__Endpoints__Https__Certificate__Subject = "SERVER01"
$env:Kestrel__Endpoints__Https__Certificate__Store = "My"
$env:Kestrel__Endpoints__Https__Certificate__Location = "CurrentUser"
$env:Kestrel__Endpoints__Https__Certificate__AllowInvalid = "true"

docker compose up -d
dotnet run --project src/NetworkMonitoringSystem.Server --no-launch-profile
```

`AllowInvalid` only tells the server that its own certificate may be self-signed. `--no-launch-profile` matters: the launch profile would make the server listen on `localhost` only.

The `Development` environment keeps using your user secrets (database password, enrollment token, first administrator's password) and applies database migrations on start. Outside it, set the same values as environment variables (`Database__Password`, `Agents__EnrollmentToken`, `Access__InitialAdminPassword`) and set `Database__MigrateOnStartup` to `true` when the database should be created or updated on start.

## 2. Agent on another computer

### Build the package

On the development computer:

```powershell
.\scripts\Publish-Agent.ps1
```

This creates `artifacts\agent`: the agent with everything it needs, so .NET does not have to be installed on the target computer, and the scripts that install and remove it.

### Install

Copy the folder to the target computer and run, in PowerShell as administrator:

```powershell
.\Install-Agent.ps1 -ServerUrl https://SERVER01:7006 -ServerCertificateSha256 <pin>
```

The script asks for the enrollment token without showing it. It then

1. copies the agent to `C:\Program Files\NetworkMonitoringAgent`,
2. writes the server address and the pin to the agent's settings,
3. registers the computer with the server, using the token once,
4. creates the service `NetworkMonitoringAgent`, sets it to start with Windows and to restart after a failure, and starts it.

The computer appears in the desktop application as a new device within a few seconds.

If PowerShell refuses to run the script, allow scripts for this one window first: `Set-ExecutionPolicy -Scope Process RemoteSigned`.

### What is stored where

| What | Where | Who can read it |
|---|---|---|
| Program and settings (server address, pin) | `C:\Program Files\NetworkMonitoringAgent` | Everyone; nothing in it is secret |
| Identity issued by the server | `C:\ProgramData\NetworkMonitoringSystem\Agent\identity.bin`, encrypted with Windows DPAPI | The system and administrators only |
| Enrollment token | Nowhere; it is used for the registration and forgotten | – |
| Messages of the agent | Windows event log, Application, source `NetworkMonitoringAgent` | – |

The service runs under the local system account. That lets it see all processes and control services, which the remote commands need.

### Update and removal

Running `Install-Agent.ps1` again from a newer package replaces the program and keeps the identity, so the computer continues as the same device.

```powershell
.\Uninstall-Agent.ps1                  # removes the service and the program, keeps the identity
.\Uninstall-Agent.ps1 -RemoveIdentity  # also forgets the identity; remove the device on the server too
```

## 3. Desktop application on another computer

Set the address and the pin in `appsettings.json` next to the application:

```json
{
  "Server": {
    "Url": "https://SERVER01:7006",
    "CertificateSha256": "<pin>"
  }
}
```

## Troubleshooting

| Message of the agent | Meaning |
|---|---|
| `The remote certificate was rejected by the provided RemoteCertificateValidationCallback` | The pin does not belong to the certificate the server presents. Check that the pin was copied whole and that the server was started with the new certificate. |
| `The remote certificate is invalid because of errors in the certificate chain: UntrustedRoot` | No pin is set and Windows does not trust the server's certificate. Set the pin. |
| `Server is not reachable (Unavailable)` | The address or port is wrong, the server is not running, or the firewall blocks the port. |
| `Server refused the registration` | The enrollment token is wrong, the device is disabled, or the server still sees a device of this name as online. In the last case wait until it is marked offline (three synchronization intervals) and install again. |
