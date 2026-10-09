[CmdletBinding()]
param([Parameter(Mandatory)][string]$PublishDir)
$ErrorActionPreference = 'Stop'
$PublishDir = [IO.Path]::GetFullPath($PublishDir)
$root = Split-Path $PSScriptRoot -Parent
$go = Join-Path $root '.tools/go/bin/go.exe'
$licenseDir = Join-Path $PublishDir 'licenses'
New-Item -ItemType Directory -Path $licenseDir -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'docs/licenses') -Destination $PublishDir -Recurse -Force
Copy-Item -LiteralPath (Join-Path $root 'core/LICENSE') -Destination (Join-Path $licenseDir 'wx_channel-MIT.txt') -Force
$moduleDir = Join-Path $licenseDir 'go-modules'
New-Item -ItemType Directory -Path $moduleDir -Force | Out-Null
Push-Location (Join-Path $root 'core')
try {
    & $go mod download
    if ($LASTEXITCODE -ne 0) { throw 'Cannot collect exact Go dependency licenses.' }
    $modules = & $go list -deps -tags nosqlite -f '{{if .Module}}{{.Module.Path}}|{{.Module.Version}}|{{.Module.Dir}}{{end}}' . | Where-Object { $_ } | Sort-Object -Unique
    if ($LASTEXITCODE -ne 0) { throw 'Go module inventory failed.' }
    $modules | ForEach-Object { $entry = $_ -split '\|', 3; "$($entry[0])@$($entry[1])" } | Set-Content -LiteralPath (Join-Path $moduleDir 'MODULES.txt') -Encoding utf8
    foreach ($line in $modules) {
        $parts = $line -split '\|', 3
        if ($parts.Length -ne 3 -or -not $parts[1] -or -not $parts[2]) { continue }
        if ($parts[0] -eq 'github.com/qtgolang/SunnyNet') { continue } # Local fork notices are copied above.
        $files = Get-ChildItem -LiteralPath $parts[2] -File | Where-Object { $_.Name -match '^(LICENSE|LICENCE|COPYING|NOTICE|COPYRIGHT)(\.|$)' }
        if (-not $files) { throw "No license file found for Go module $($parts[0])@$($parts[1]); review it before releasing." }
        $destination = Join-Path $moduleDir (($parts[0] + '@' + $parts[1]) -replace '[/:\\]', '_')
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        $files | Copy-Item -Destination $destination -Force
    }
} finally { Pop-Location }
$assetsPath = Join-Path $root 'app/Lumiven/obj/project.assets.json'
if (-not (Test-Path -LiteralPath $assetsPath)) { throw 'NuGet restore inventory missing.' }
$assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
$nugetRoots = @($assets.packageFolders.PSObject.Properties.Name)
$nugetDir = Join-Path $licenseDir 'dotnet-packages'
New-Item -ItemType Directory -Path $nugetDir -Force | Out-Null
$inventory = [Collections.Generic.List[string]]::new()
foreach ($property in $assets.libraries.PSObject.Properties) {
    if ($property.Value.type -ne 'package') { continue }
    $relative = $property.Name.ToLowerInvariant()
    $package = $nugetRoots | ForEach-Object { Join-Path $_ $relative } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $package) { throw "NuGet package unavailable: $($property.Name)" }
    $destination = Join-Path $nugetDir ($property.Name -replace '[/\\:]', '_')
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    $files = Get-ChildItem -LiteralPath $package -Recurse -File | Where-Object { $_.Name -match '(license|licence|copyright|third.part.*notice|^notice)' -and $_.Extension -in @('.txt','.md','.rtf','.html','') }
    foreach ($file in $files) {
        $target = Join-Path $destination ([IO.Path]::GetRelativePath($package, $file.FullName))
        New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target -Force
    }
    $nuspec = Get-ChildItem -LiteralPath $package -Filter '*.nuspec' -File | Select-Object -First 1
    if (-not $nuspec) { throw "NuGet license metadata missing: $($property.Name)" }
    Copy-Item -LiteralPath $nuspec.FullName -Destination $destination -Force
    $inventory.Add($property.Name)
}
$inventory | Sort-Object | Set-Content -LiteralPath (Join-Path $nugetDir 'PACKAGES.txt') -Encoding utf8
