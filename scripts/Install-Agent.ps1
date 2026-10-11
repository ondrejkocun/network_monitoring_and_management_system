<#
.SYNOPSIS
    Installs the monitoring agent on this computer as a Windows service and registers it with the server.

.DESCRIPTION
    Run it as administrator from the folder created by Publish-Agent.ps1. It

      1. copies the agent to the installation directory,
      2. writes the address of the server and the pin of its certificate to the agent's settings,
      3. registers this computer with the server, using the enrollment token once,
      4. creates the service, sets it to start with Windows and to restart after a failure, and starts it.

    The enrollment token is not stored anywhere: it is used for the registration and forgotten. What the agent
    keeps is the identity the server issued, encrypted and readable only by the system and administrators.

    Running the script again updates an installed agent and keeps its identity.

.PARAMETER ServerUrl
    Address of the server, for example https://SERVER01:7006. It must be an https address.

.PARAMETER ServerCertificateSha256
    Pin of the server's certificate as printed by New-ServerCertificate.ps1. Leave it out only when the server
    has a certificate this computer already trusts.

.PARAMETER EnrollmentToken
    The enrollment token configured on the server. When left out, the script asks for it without showing it.

.PARAMETER InstallDirectory
    Where the agent is installed.

.EXAMPLE
    .\Install-Agent.ps1 -ServerUrl https://SERVER01:7006 -ServerCertificateSha256 3F2A...9C
#>
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ServerUrl,

    [string]$ServerCertificateSha256 = '',

    [string]$EnrollmentToken = '',

    [string]$InstallDirectory = (Join-Path $env:ProgramFiles 'NetworkMonitoringAgent')
)

$ErrorActionPreference = 'Stop'

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
    $secure = Read-Host -Prompt 'Enrollment token' -AsSecureString
    $pointer = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)

    try {
        $EnrollmentToken = [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    }
    finally {
        [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
}

# The token is handed over in the environment of this one process, not on its command line and not in a file.
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
    throw 'The computer could not be registered. Check the address of the server, the certificate pin and the enrollment token; the messages above say what failed. The service was not created.'
}

if (-not $existing) {
    Write-Host 'Creating the service...'
    New-Service -Name $serviceName `
        -BinaryPathName "`"$program`"" `
        -DisplayName 'Network Monitoring Agent' `
        -Description 'Reports the state of this computer to the Network Monitoring System server and carries out its commands.' `
        -StartupType Automatic | Out-Null
}

# After a failure the service is started again: twice after a minute, then every five minutes.
& sc.exe failure $serviceName reset= 86400 actions= restart/60000/restart/60000/restart/300000 | Out-Null

Start-Service -Name $serviceName

Write-Host ''
Write-Host "The agent is installed and running as the service '$serviceName'."
Write-Host 'Its messages are in the Windows event log (Application, source NetworkMonitoringAgent).'
