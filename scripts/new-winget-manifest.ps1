param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory)][string]$InstallerPath,
    [string]$OutputDirectory = 'artifacts/winget/manifests'
)
$ErrorActionPreference = 'Stop'
$packageId = 'behnamapps.RtlTerminal'
$destination = Join-Path $OutputDirectory "b/behnamapps/RtlTerminal/$Version"
$null = New-Item -ItemType Directory -Force -Path $destination
$sha256 = (Get-FileHash -LiteralPath $InstallerPath -Algorithm SHA256).Hash
$baseUrl = 'https://github.com/mirbehnam/RtlTerminal'
$schemaVersion = '1.6.0'
$utf8 = New-Object System.Text.UTF8Encoding($false)
function Write-Manifest([string]$Suffix, [string]$Type, [string]$Body) {
    $name = if ($Suffix) { "$packageId.$Suffix.yaml" } else { "$packageId.yaml" }
    $header = "# yaml-language-server: `$schema=https://aka.ms/winget-manifest.$Type.$schemaVersion.schema.json`n"
    [IO.File]::WriteAllText((Join-Path $destination $name), $header + $Body.Trim() + "`n", $utf8)
}
Write-Manifest '' 'version' @"
PackageIdentifier: $packageId
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: $schemaVersion
"@
Write-Manifest 'installer' 'installer' @"
PackageIdentifier: $packageId
PackageVersion: $Version
MinimumOSVersion: 10.0.19041.0
InstallerType: inno
Scope: machine
UpgradeBehavior: install
ProductCode: '{A5BC0F01-F4CD-49C4-B85F-8B88ACDC4416}_is1'
AppsAndFeaturesEntries:
- DisplayName: Rtl Terminal
  Publisher: behnamapps
  DisplayVersion: $Version
  ProductCode: '{A5BC0F01-F4CD-49C4-B85F-8B88ACDC4416}_is1'
Installers:
- Architecture: x64
  InstallerUrl: $baseUrl/releases/download/v$Version/RtlTerminal-Setup-$Version-x64.exe
  InstallerSha256: $sha256
ManifestType: installer
ManifestVersion: $schemaVersion
"@
Write-Manifest 'locale.en-US' 'defaultLocale' @"
PackageIdentifier: $packageId
PackageVersion: $Version
PackageLocale: en-US
Publisher: behnamapps
PublisherUrl: https://github.com/mirbehnam
PublisherSupportUrl: $baseUrl/issues
PackageName: Rtl Terminal
PackageUrl: $baseUrl
License: GPL-3.0-only
LicenseUrl: $baseUrl/blob/v$Version/LICENSE
ShortDescription: A Windows terminal with Persian and right-to-left text support.
Description: RtlTerminal is a Windows terminal with Smart RTL rendering, PowerShell, Command Prompt and WSL profiles, split panes, terminal macros, themes and English, Persian and Arabic interface languages.
Moniker: rtlterminal
Tags:
- terminal
- console
- persian
- rtl
- powershell
- wsl
ReleaseNotesUrl: $baseUrl/releases/tag/v$Version
ManifestType: defaultLocale
ManifestVersion: $schemaVersion
"@
Write-Output $destination
