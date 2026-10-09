[CmdletBinding()]
param([string]$Version = '1.0.0', [switch]$Installer, [switch]$SourceArchive, [switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'Verify-Licenses.ps1')
$go = Join-Path $root '.tools/go/bin/go.exe'
$dotnet = Join-Path $root '.tools/dotnet/dotnet.exe'
$node = Join-Path $root '.tools/node-v24.19.0-win-x64/node.exe'
if (-not (Test-Path $go) -or -not (Test-Path $dotnet) -or -not (Test-Path $node)) { throw 'Run scripts/Prepare-Tools.ps1 first.' }
$publish = Join-Path $root 'artifacts/publish'
$release = Join-Path $root 'artifacts/release'
New-Item -ItemType Directory -Path $publish, $release -Force | Out-Null
$env:GOOS = 'windows'
$env:GOARCH = 'amd64'
$env:CGO_ENABLED = '1'
$zig = Join-Path $root '.tools/zig-windows-x86_64-0.13.0/zig.exe'
if (-not $env:CC) { $env:CC = "`"$zig`" cc -target x86_64-windows-gnu" }
if (-not $env:CXX) { $env:CXX = "`"$zig`" c++ -target x86_64-windows-gnu" }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
if (-not $SkipTests) {
    & $dotnet run --project (Join-Path $root 'app/Lumiven.SmokeTests/Lumiven.SmokeTests.csproj') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Actual shell service and media library smoke tests failed.' }
    $injectTests = @(Get-ChildItem -LiteralPath (Join-Path $root 'core/internal/assets/inject') -Filter '*.test.js' -File | ForEach-Object FullName)
    & $node --test @injectTests
    if ($LASTEXITCODE -ne 0) { throw 'WeChat injection tests failed.' }
}
Push-Location (Join-Path $root 'core')
try {
    if (-not $SkipTests) { & $go test -tags nosqlite ./internal/app/... ./internal/desktop/... ./internal/storage/... ./internal/handlers/...; if ($LASTEXITCODE -ne 0) { throw 'Go app, desktop, storage or handler tests failed.' } }
    & $go build -tags nosqlite -trimpath -ldflags '-s -w -H windowsgui' -o (Join-Path $publish 'lumiven-core.exe') .
    if ($LASTEXITCODE -ne 0) { throw 'Go core build failed. Check CGO C compiler availability (CC).' }
} finally { Pop-Location }
if (-not $SkipTests) {
    foreach ($concurrency in @(5, 2)) {
        & $node (Join-Path $root 'core/internal/desktop/blackbox.test.mjs') (Join-Path $publish 'lumiven-core.exe') $concurrency
        if ($LASTEXITCODE -ne 0) { throw "Real core handler/Gopeed/SQLite fixture test failed at concurrency $concurrency." }
    }
}
$appProject = Join-Path $root 'app/Lumiven/Lumiven.csproj'
& $dotnet clean $appProject -c Release -r win-x64 -p:Platform=x64
if ($LASTEXITCODE -ne 0) { throw 'WinUI clean failed.' }
& $dotnet publish $appProject -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:WindowsAppSDKSelfContained=true -p:Version=$Version -p:DebugType=none -p:DebugSymbols=false -o $publish
if ($LASTEXITCODE -ne 0) { throw 'WinUI publish failed.' }
if (-not (Test-Path -LiteralPath (Join-Path $publish 'EVEING.Lumiven.exe'))) { throw 'Expected WinUI executable EVEING.Lumiven.exe missing.' }
$pdb = Join-Path $publish 'EVEING.Lumiven.pdb'
if (Test-Path -LiteralPath $pdb) { Remove-Item -LiteralPath $pdb -Force }
$assembly = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes((Join-Path $publish 'EVEING.Lumiven.dll')))
if ($assembly.Contains([Environment]::GetFolderPath('UserProfile'))) { throw 'Published WinUI DLL contains the local user path.' }
$appOutput = Join-Path $root 'app/Lumiven/bin/x64/Release/net10.0-windows10.0.26100.0/win-x64'
foreach ($relative in @('App.xbf', 'MainWindow.xbf', 'Themes/Glass.xbf', 'EVEING.Lumiven.pri')) {
    $source = Join-Path $appOutput $relative
    if (-not (Test-Path -LiteralPath $source)) { throw "Compiled WinUI resource missing: $relative" }
    $destination = Join-Path $publish $relative
    New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination -Force
}
foreach ($name in @('LICENSE', 'NOTICE', 'THIRD-PARTY-NOTICES.md')) { Copy-Item -LiteralPath (Join-Path $root $name) -Destination $publish -Force }
& (Join-Path $PSScriptRoot 'Collect-Licenses.ps1') -PublishDir $publish
& $node (Join-Path $PSScriptRoot 'Audit-Runtime.mjs') $publish (Join-Path $root 'artifacts/qa/runtime-imports.json')
if ($LASTEXITCODE -ne 0) { throw 'A published PE has an unbundled CRT or unresolved native dependency.' }
if ($Installer) {
    $iscc = Join-Path $root '.tools/inno/ISCC.exe'
    if (-not (Test-Path -LiteralPath $iscc)) { throw 'Run scripts/Prepare-Tools.ps1 -IncludeInstaller first.' }
    & $iscc /Qp "/DAppVersion=$Version" "/DPublishDir=$publish" "/DReleaseDir=$release" (Join-Path $root 'installer/Lumiven.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compile failed.' }
    & $node (Join-Path $PSScriptRoot 'Audit-Runtime.mjs') $release (Join-Path $root 'artifacts/qa/installer-imports.json')
    if ($LASTEXITCODE -ne 0) { throw 'The installer has an unresolved native dependency.' }
}
if ($SourceArchive) { & (Join-Path $PSScriptRoot 'Source-Archive.ps1') -Version $Version }
$sums = Get-ChildItem -LiteralPath $release -File | Where-Object { $_.Name -ne 'SHA256SUMS' } | Sort-Object Name | ForEach-Object { '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name }
[IO.File]::WriteAllLines((Join-Path $release 'SHA256SUMS'), [string[]]$sums, [Text.UTF8Encoding]::new($false))
Write-Host "Build outputs: $release"
