<#
.SYNOPSIS
    Creates a self-signed certificate for the server and prints the pin that agents and the desktop application use to trust it.

.DESCRIPTION
    The certificate is stored in the personal certificate store of the current user, which is where the server
    looks for it when it runs under this account. Its private key cannot be exported.

    Clients do not need the certificate installed: they are given its SHA-256 hash (the pin) and talk only
    to a server that presents exactly this certificate.

.PARAMETER DnsName
    Names under which other computers reach the server, for example the computer name.

.PARAMETER IpAddress
    IP addresses under which other computers reach the server, when they use an address instead of a name.

.PARAMETER ValidYears
    How long the certificate is valid. After that a new one has to be created and its pin given to the clients.

.EXAMPLE
    .\New-ServerCertificate.ps1 -DnsName SERVER01 -IpAddress 192.168.50.10
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string[]]$DnsName,

    [string[]]$IpAddress = @(),

    [ValidateRange(1, 10)]
    [int]$ValidYears = 2
)

$ErrorActionPreference = 'Stop'

foreach ($address in $IpAddress) {
    $parsed = $null

    if (-not [System.Net.IPAddress]::TryParse($address, [ref]$parsed)) {
        throw "'$address' is not an IP address."
    }
}

# The names and addresses go into the subject alternative name, which is what clients compare the address with.
$alternativeNames = @($DnsName | ForEach-Object { "DNS=$_" }) + @($IpAddress | ForEach-Object { "IPAddress=$_" })

$certificate = New-SelfSignedCertificate `
    -Subject "CN=$($DnsName[0])" `
    -FriendlyName 'Network Monitoring System server' `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -KeyAlgorithm RSA `
    -KeyLength 3072 `
    -HashAlgorithm SHA256 `
    -KeyExportPolicy NonExportable `
    -KeyUsage DigitalSignature, KeyEncipherment `
    -NotAfter (Get-Date).AddYears($ValidYears) `
    -TextExtension @(
        '2.5.29.37={text}1.3.6.1.5.5.7.3.1',
        "2.5.29.17={text}$($alternativeNames -join '&')"
    )

$sha256 = [System.Security.Cryptography.SHA256]::Create()

try {
    $pin = ([System.BitConverter]::ToString($sha256.ComputeHash($certificate.RawData))).Replace('-', '')
}
finally {
    $sha256.Dispose()
}

Write-Host ''
Write-Host 'Certificate created.'
Write-Host "  Subject:     $($certificate.Subject)"
Write-Host "  Valid until: $($certificate.NotAfter.ToString('yyyy-MM-dd'))"
Write-Host "  Thumbprint:  $($certificate.Thumbprint)   (identifies it in the certificate store)"
Write-Host ''
Write-Host 'Pin for agents and the desktop application (SHA-256 of the certificate):'
Write-Host "  $pin"
Write-Host ''
Write-Host 'Tell the server which certificate is its own and start it on the network:'
Write-Host "  dotnet user-secrets set `"Kestrel:Endpoints:Https:Certificate:Subject`" `"$($DnsName[0])`" --project src/NetworkMonitoringSystem.Server"
Write-Host '  dotnet run --project src/NetworkMonitoringSystem.Server --launch-profile network'

# The pin is also returned, so the script can be used from another script.
$pin
