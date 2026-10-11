<#
.SYNOPSIS
    Removes the monitoring agent service from this computer.

.DESCRIPTION
    Run it as administrator. It stops and deletes the service and removes the installed files.

    The identity the server issued is kept by default, so installing the agent again continues as the same
    device. With -RemoveIdentity it is deleted as well; the computer then has to be registered again, and
    the device should be removed on the server.

.PARAMETER InstallDirectory
    Where the agent is installed.

.PARAMETER RemoveIdentity
    Also delete the identity the server issued to this computer.

.EXAMPLE
    .\Uninstall-Agent.ps1 -RemoveIdentity
#>
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [string]$InstallDirectory = (Join-Path $env:ProgramFiles 'NetworkMonitoringAgent'),

    [switch]$RemoveIdentity
)

$ErrorActionPreference = 'Stop'

$serviceName = 'NetworkMonitoringAgent'
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue

if ($service) {
    if ($service.Status -ne 'Stopped') {
        Write-Host 'Stopping the agent...'
        Stop-Service -Name $serviceName -Force
        $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
    }

    & sc.exe delete $serviceName | Out-Null
    Write-Host 'The service was removed.'
}
else {
    Write-Host 'The service is not installed.'
}

# Only a folder that really holds the agent is deleted, whatever path was given.
if (Test-Path (Join-Path $InstallDirectory 'NetworkMonitoringSystem.Agent.exe')) {
    # The script itself may be running from that folder.
    Set-Location $env:TEMP
    Remove-Item $InstallDirectory -Recurse -Force
    Write-Host "The files in $InstallDirectory were removed."
}

if ($RemoveIdentity) {
    $identityFolder = Join-Path $env:ProgramData 'NetworkMonitoringSystem\Agent'

    if (Test-Path $identityFolder) {
        Remove-Item $identityFolder -Recurse -Force
        Write-Host 'The identity of this computer was removed. Remove the device on the server as well.'
    }
}
