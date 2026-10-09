[CmdletBinding()]
param([switch]$IncludeInstaller)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$toolDir = Join-Path $root '.tools'
New-Item -ItemType Directory -Path $toolDir -Force | Out-Null
$manifest = Get-Content (Join-Path $PSScriptRoot 'toolchain.json') -Raw | ConvertFrom-Json
$spec = $manifest.node
$node = Join-Path $toolDir "node-v$($spec.version)-win-x64/node.exe"
if (-not (Test-Path -LiteralPath $node)) {
    $archive = Join-Path $toolDir "node-$($spec.version).zip"
    if (-not (Test-Path -LiteralPath $archive)) { Invoke-WebRequest -Uri $spec.url -OutFile $archive }
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $spec.sha256) { throw 'Node checksum mismatch' }
    Expand-Archive -LiteralPath $archive -DestinationPath $toolDir -Force
}
foreach ($name in @('dotnet', 'go')) {
    $spec = $manifest.$name
    $destination = Join-Path $toolDir $name
    $executable = if ($name -eq 'go') { Join-Path $destination 'bin/go.exe' } else { Join-Path $destination 'dotnet.exe' }
    if (Test-Path -LiteralPath $executable) { continue }
    $archive = Join-Path $toolDir "$name-$($spec.version).zip"
    if (-not (Test-Path -LiteralPath $archive)) { Invoke-WebRequest -Uri $spec.url -OutFile $archive }
    $algorithm = if ($name -eq 'dotnet') { 'SHA512' } else { 'SHA256' }
    $expected = if ($name -eq 'dotnet') { $spec.sha512 } else { $spec.sha256 }
    if ((Get-FileHash -LiteralPath $archive -Algorithm $algorithm).Hash -ne $expected) { throw "$name archive checksum mismatch" }
    if ($name -eq 'go') { Expand-Archive -LiteralPath $archive -DestinationPath $toolDir -Force }
    else { Expand-Archive -LiteralPath $archive -DestinationPath $destination -Force }
}
if ($IncludeInstaller -and -not (Test-Path -LiteralPath (Join-Path $toolDir 'inno/ISCC.exe'))) {
    $spec = $manifest.inno
    $installer = Join-Path $toolDir "innosetup-$($spec.version).exe"
    if (-not (Test-Path -LiteralPath $installer)) { Invoke-WebRequest -Uri $spec.url -OutFile $installer }
    if ((Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash -ne $spec.sha256) { throw 'Inno Setup checksum mismatch' }
    $destination = Join-Path $toolDir 'inno'
    $process = Start-Process -FilePath $installer -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER', "/DIR=`"$destination`"") -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "Inno Setup installation failed: $($process.ExitCode)" }
}
$spec = $manifest.zig
$zig = Join-Path $toolDir "zig-windows-x86_64-$($spec.version)/zig.exe"
if (-not (Test-Path -LiteralPath $zig)) {
    $archive = Join-Path $toolDir "zig-$($spec.version).zip"
    if (-not (Test-Path -LiteralPath $archive)) { Invoke-WebRequest -Uri $spec.url -OutFile $archive }
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $spec.sha256) { throw 'Zig checksum mismatch' }
    Expand-Archive -LiteralPath $archive -DestinationPath $toolDir -Force
}
