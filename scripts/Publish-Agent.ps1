<#
.SYNOPSIS
    Builds the agent into one folder that can be copied to another computer and installed there.

.DESCRIPTION
    The folder contains the agent with everything it needs (.NET does not have to be installed on the target
    computer) and the scripts that install and remove it as a Windows service.

.PARAMETER OutputDirectory
    Where to put the folder. It is emptied first.

.EXAMPLE
    .\Publish-Agent.ps1
#>
[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\agent')
)

$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot '..\src\NetworkMonitoringSystem.Agent\NetworkMonitoringSystem.Agent.csproj'
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)

if (Test-Path $OutputDirectory) {
    Remove-Item $OutputDirectory -Recurse -Force
}

dotnet publish $project --configuration Release --runtime win-x64 --self-contained true --output $OutputDirectory

if ($LASTEXITCODE -ne 0) {
    throw 'Publishing the agent failed.'
}

# Settings for the developer's own computer do not belong on another one.
Remove-Item (Join-Path $OutputDirectory 'appsettings.Development.json') -ErrorAction SilentlyContinue

Copy-Item (Join-Path $PSScriptRoot 'Install-Agent.ps1'), (Join-Path $PSScriptRoot 'Uninstall-Agent.ps1') -Destination $OutputDirectory

Write-Host ''
Write-Host "The agent is ready in $OutputDirectory"
Write-Host 'Copy the folder to the target computer and run Install-Agent.ps1 there as administrator.'
