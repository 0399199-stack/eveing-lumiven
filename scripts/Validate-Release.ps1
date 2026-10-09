[CmdletBinding()]
param([string]$Version = '1.0.0')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'Verify-Licenses.ps1')
$validation = Get-Content (Join-Path $root 'docs/VALIDATION.md') -Raw
if ($validation -match '待验证|状态：尚未完成|TODO|PENDING' -or $validation -notmatch '状态：已完成真实微信端到端验收') { throw 'Public release requires a completed, factual validation record, including real WeChat download and system-state recovery.' }
$release = Join-Path $root 'artifacts/release'
$payload = @("EVEING-Lumiven-Setup-$Version-x64.exe", "EVEING-Lumiven-Source-$Version.zip")
$required = @($payload) + @('SHA256SUMS')
$entries = @(Get-ChildItem -LiteralPath $release -Force)
if ($entries.Count -ne $required.Count -or @($entries | Where-Object { $_ -isnot [IO.FileInfo] -or $required -cnotcontains $_.Name }).Count -ne 0) {
    throw 'Release directory must contain exactly the installer, corresponding source ZIP and SHA256SUMS.'
}
$lines = @(Get-Content -LiteralPath (Join-Path $release 'SHA256SUMS'))
if ($lines.Count -ne $payload.Count) { throw 'Checksum manifest must list both release payloads exactly once.' }
$seen = @{}
foreach ($line in $lines) {
    if ($line -cnotmatch '^([0-9a-f]{64})  ([^/\\]+)$') { throw 'Invalid checksum manifest.' }
    $expected = $Matches[1]
    $name = $Matches[2]
    if ($payload -cnotcontains $name -or $seen.ContainsKey($name)) { throw "Unexpected or repeated checksum entry: $name" }
    $file = Join-Path $release $name
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -cne $expected) { throw "Release checksum mismatch: $file" }
    $seen[$name] = $true
}
Write-Host 'Release validation record, licenses, artifact names and checksums verified.'
