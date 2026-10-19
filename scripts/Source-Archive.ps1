[CmdletBinding()]
param([string]$Version = '1.1.0')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'Verify-Licenses.ps1')
Push-Location $root
try {
    $dirty = & git status --porcelain
    if ($LASTEXITCODE -ne 0 -or $dirty) { throw 'Commit the exact verified release source before creating its source archive.' }
    $release = Join-Path $root 'artifacts/release'
    New-Item -ItemType Directory -Path $release -Force | Out-Null
    $destination = Join-Path $release "EVEING-Lumiven-Source-$Version.zip"
    $stage = Join-Path $root ('artifacts/source-stage-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    $snapshot = Join-Path $stage 'snapshot.zip'
    & git archive --format=zip "--prefix=EVEING-Lumiven-$Version/" "--output=$snapshot" HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Source archive failed.' }
    Expand-Archive -LiteralPath $snapshot -DestinationPath $stage
    $source = Join-Path $stage "EVEING-Lumiven-$Version"
    & git rev-parse HEAD | Set-Content -LiteralPath (Join-Path $source 'RELEASE-COMMIT.txt') -Encoding utf8
    $go = Join-Path $root '.tools/go/bin/go.exe'
    Push-Location (Join-Path $source 'core')
    try {
        & $go mod vendor
        if ($LASTEXITCODE -ne 0) { throw 'Corresponding Go dependency source vendoring failed.' }
    } finally { Pop-Location }
    Compress-Archive -LiteralPath $source -DestinationPath $destination -Force
    Write-Host "Corresponding source: $destination"
} finally { Pop-Location }
