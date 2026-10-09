[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$core = Join-Path $root 'core'
$forbidden = Get-ChildItem -LiteralPath $core -Recurse -File | Where-Object {
    $_.Extension -in @('.sys', '.dll', '.key', '.pfx', '.p12') -or $_.Name -match '^wx_channel.*\.exe$'
}
if ($forbidden) { throw "Unapproved upstream binaries remain: $($forbidden.FullName -join ', ')" }
$sourceFiles = Get-ChildItem -LiteralPath $core, (Join-Path $root 'app') -Recurse -File | Where-Object { $_.Extension -in @('.go', '.cs', '.js', '.mjs', '.json', '.yaml', '.yml', '.ps1', '.xaml') -and $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
$embeddedKeys = $sourceFiles | Select-String -Pattern '-----BEGIN (?:[A-Z]+ )?PRIVATE KEY-----' -List
if ($embeddedKeys) { throw "Embedded private key in source: $($embeddedKeys.Path -join ', ')" }
if (Test-Path -LiteralPath (Join-Path $core 'pkg/sunnynet/Resource/nfapi')) { throw 'Commercial NetFilter SDK resources must be removed before distribution.' }
$driverSource = Join-Path $core 'pkg/sunnynet/src/nfapi'
if (Test-Path -LiteralPath $driverSource) {
    $found = Get-ChildItem -LiteralPath $driverSource -Recurse -File | Select-String -Pattern 'typedef\s+.*NF_|NF_EventHandler|nf_registerDriver|nfapi\.dll|Vitaly Sidorov'
    if ($found) { throw 'NetFilter SDK-derived declarations or driver-loading code remain.' }
}
foreach ($file in @('LICENSE', 'NOTICE', 'THIRD-PARTY-NOTICES.md', 'core/LICENSE', 'docs/licenses/SunnyNet-MIT.txt', 'docs/licenses/Go-BSD.txt', 'docs/licenses/Gorilla-BSD.txt', 'docs/licenses/PKCS12-BSD.txt', 'docs/licenses/RC2-MIT.txt', 'docs/licenses/Mitt-MIT.txt', 'docs/licenses/Gopeed-GPL-3.0.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $file))) { throw "Required license missing: $file" }
}
Write-Host 'License source gate passed: commercial NetFilter artifacts excluded; required notices present.'
