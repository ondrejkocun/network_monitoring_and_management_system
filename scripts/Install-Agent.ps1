<#
.SYNOPSIS
    Installs the monitoring agent on this computer as a Windows service and registers it with the server.

.DESCRIPTION
    Run it as administrator from the folder created by Publish-Agent.ps1. It

      1. copies the agent to the installation directory,
      2. writes the address of the server and the pin of its certificate to the agent's settings,
      3. registers this computer with the server, using the enrollment code (or the shared token) once,
      4. creates the service, sets it to start with Windows and to restart after a failure, and starts it.

    The enrollment code is not stored anywhere: it is used for the registration and forgotten. What the agent
    keeps is the identity the server issued, encrypted and readable only by the system and administrators.

    Running the script again updates an installed agent and keeps its identity.

.PARAMETER ServerUrl
    Address of the server, for example https://SERVER01:7006. It must be an https address.

.PARAMETER ServerCertificateSha256
    Pin of the server's certificate as printed by New-ServerCertificate.ps1. Leave it out only when the server
    has a certificate this computer already trusts.

.PARAMETER EnrollmentToken
    A one-time enrollment code created in the desktop application (Registračné kódy), for example K7QM-4XTD-9HPA,
    or the shared token when the server has one. Also accepted as -EnrollmentCode. When left out, the script asks for
    it without showing it. A computer the server already knows, for example after a reinstall, needs a code made for
    that device.

.PARAMETER InstallDirectory
    Where the agent is installed.

.PARAMETER WithTrafficCapture
    Also prepares this computer for capturing its network traffic, which is switched on per device in the desktop
    application. When missing, Wireshark (with TShark) is installed silently with winget, and the installer of the
    Npcap driver is downloaded from npcap.com, its signature checked and started. Npcap cannot be installed
    silently: its free edition allows that only in the paid OEM edition, and it may not be shipped with the agent
    either, so its installer asks for a few clicks. A failure here does not stop the installation of the agent;
    the agent then reports why it cannot capture.

.EXAMPLE
    .\Install-Agent.ps1 -ServerUrl https://SERVER01:7006 -ServerCertificateSha256 3F2A...9C

.EXAMPLE
    .\Install-Agent.ps1 -ServerUrl https://SERVER01:7006 -ServerCertificateSha256 3F2A...9C -WithTrafficCapture
#>
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ServerUrl,

    [string]$ServerCertificateSha256 = '',

    [Alias('EnrollmentCode')]
    [string]$EnrollmentToken = '',

    [string]$InstallDirectory = (Join-Path $env:ProgramFiles 'NetworkMonitoringAgent'),

    [switch]$WithTrafficCapture
)

$ErrorActionPreference = 'Stop'

# Prepares TShark and Npcap for traffic capture. Returns a message for the summary; never stops the installation.
function Install-TrafficCapture {
    $tshark = Join-Path $env:ProgramFiles 'Wireshark\tshark.exe'

    if (-not (Test-Path $tshark)) {
        if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
            return 'Wireshark is not installed and winget is not available. Install it from https://www.wireshark.org/download.html.'
        }

        Write-Host 'Installing Wireshark with winget...'
        & winget install --id WiresharkFoundation.Wireshark --exact --silent --scope machine `
            --accept-package-agreements --accept-source-agreements | Out-Host

        if (-not (Test-Path $tshark)) {
            return 'Wireshark could not be installed with winget; the messages above say why.'
        }
    }
    else {
        Write-Host 'Wireshark is already installed.'
    }

    if (-not (Get-Service -Name npcap -ErrorAction SilentlyContinue)) {
        Write-Host 'Downloading the Npcap installer from npcap.com...'

        # The page links the current version as dist/npcap-<version>.exe.
        $page = Invoke-WebRequest -Uri 'https://npcap.com/' -UseBasicParsing
        $match = [regex]::Match($page.Content, 'dist/npcap-[0-9.]+\.exe')

        if (-not $match.Success) {
            return 'The Npcap installer was not found on https://npcap.com/. Download and install it from there.'
        }

        $installer = Join-Path $env:TEMP ([System.IO.Path]::GetFileName($match.Value))
        Invoke-WebRequest -Uri "https://npcap.com/$($match.Value)" -OutFile $installer -UseBasicParsing

        # Only a file signed by the authors of Npcap is run.
        $signature = Get-AuthenticodeSignature -FilePath $installer

        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '(^|, )O=Nmap Software LLC(,|$)') {
            Remove-Item $installer -Force
            return "The downloaded Npcap installer is not signed by its authors ($($signature.Status)); it was not run."
        }

        Write-Host 'Starting the Npcap installer; confirm its licence and click Install. The default options are enough.'
        Start-Process -FilePath $installer -Wait
        Remove-Item $installer -Force -ErrorAction SilentlyContinue

        if (-not (Get-Service -Name npcap -ErrorAction SilentlyContinue)) {
            return 'Npcap was not installed. Run the installer from https://npcap.com/ again.'
        }
    }
    else {
        Write-Host 'Npcap is already installed.'
    }

    # TShark lists the interfaces it can capture on; Npcap adapters appear as \Device\NPF_{...}.
    $interfaces = & $tshark -D 2>$null

    if (-not ($interfaces -match '\\Device\\NPF_')) {
        return 'TShark and Npcap are installed, but TShark sees no network adapter. Restart the computer and check it with: tshark -D'
    }

    return 'Traffic capture is ready; switch it on for this device in the desktop application.'
}

$serviceName = 'NetworkMonitoringAgent'
$programName = 'NetworkMonitoringSystem.Agent.exe'
$sourceProgram = Join-Path $PSScriptRoot $programName

if (-not (Test-Path $sourceProgram)) {
    throw "$programName was not found next to this script. Run the script from the folder created by Publish-Agent.ps1."
}

$serverUri = $null

if (-not [System.Uri]::TryCreate($ServerUrl, [System.UriKind]::Absolute, [ref]$serverUri) -or $serverUri.Scheme -ne 'https') {
    throw "'$ServerUrl' is not an https address."
}

$pin = ($ServerCertificateSha256 -replace '[\s:\-]', '').ToUpperInvariant()

if ($pin -ne '' -and $pin -notmatch '^[0-9A-F]{64}$') {
    throw 'The certificate pin must be the SHA-256 hash of the certificate: 64 hexadecimal digits.'
}

# An installed agent is stopped while its files are replaced.
$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue

if ($existing -and $existing.Status -ne 'Stopped') {
    Write-Host 'Stopping the installed agent...'
    Stop-Service -Name $serviceName -Force
    $existing.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
}

Write-Host "Copying the agent to $InstallDirectory..."
New-Item -ItemType Directory -Path $InstallDirectory -Force | Out-Null
Copy-Item -Path (Join-Path $PSScriptRoot '*') -Destination $InstallDirectory -Recurse -Force

# Settings of this installation; they hold nothing secret.
$settings = @{ Agent = @{ ServerUrl = $ServerUrl; ServerCertificateSha256 = $pin } } | ConvertTo-Json
[System.IO.File]::WriteAllText(
    (Join-Path $InstallDirectory 'appsettings.Production.json'),
    $settings,
    (New-Object System.Text.UTF8Encoding $false))

$program = Join-Path $InstallDirectory $programName

Write-Host 'Registering this computer with the server...'

if ($EnrollmentToken -eq '') {
    $secure = Read-Host -Prompt 'Enrollment code' -AsSecureString
    $pointer = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)

    try {
        $EnrollmentToken = [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    }
    finally {
        [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
}

# The code is handed over in the environment of this one process, not on its command line and not in a file.
$env:Agent__EnrollmentToken = $EnrollmentToken

try {
    & $program register
    $registered = $LASTEXITCODE -eq 0
}
finally {
    Remove-Item Env:\Agent__EnrollmentToken -ErrorAction SilentlyContinue
    $EnrollmentToken = $null
}

if (-not $registered) {
    throw 'The computer could not be registered. Check the address of the server, the certificate pin and the enrollment code (it may have been used, expired or revoked); the messages above say what failed. The service was not created.'
}

if (-not $existing) {
    Write-Host 'Creating the service...'
    New-Service -Name $serviceName `
        -BinaryPathName "`"$program`"" `
        -DisplayName 'Network Monitoring Agent' `
        -Description 'Reports the state of this computer to the Network Monitoring System server and carries out its commands.' `
        -StartupType Automatic | Out-Null
}
else {
    # A service disabled since the last installation would refuse to start.
    Set-Service -Name $serviceName -StartupType Automatic
}

# After a failure the service is started again: twice after a minute, then every five minutes.
& sc.exe failure $serviceName reset= 86400 actions= restart/60000/restart/60000/restart/300000 | Out-Null

Start-Service -Name $serviceName

# The agent works without traffic capture, so this comes last and cannot undo the installation.
$captureMessage = $null

if ($WithTrafficCapture) {
    try {
        $captureMessage = Install-TrafficCapture
    }
    catch {
        $captureMessage = "Traffic capture could not be prepared: $($_.Exception.Message)"
    }
}

Write-Host ''
Write-Host "The agent is installed and running as the service '$serviceName'."
Write-Host 'Its messages are in the Windows event log (Application, source NetworkMonitoringAgent).'

if ($captureMessage) {
    Write-Host $captureMessage
}
