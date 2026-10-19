[CmdletBinding()]
param([string]$Version = '1.1.0')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$qa = Join-Path $root 'artifacts/install-qa'
$target = Join-Path $root 'artifacts/qa-install'
if (-not $target.StartsWith((Join-Path $root 'artifacts') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'QA install must remain inside artifacts.' }
if (Get-Process -Name EVEING.Lumiven -ErrorAction SilentlyContinue) { throw 'Close the review app before checking that installation preserves its live AppData.' }
New-Item -ItemType Directory -Path $qa -Force | Out-Null
$installer = Join-Path $root "artifacts/release/EVEING-Lumiven-Setup-$Version-x64.exe"
if (-not (Test-Path -LiteralPath $installer)) { throw 'Build the real installer first.' }
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{52E4F908-97BD-4676-A857-5B296DB76DB0}_is1'
if (Test-Path $uninstallKey) {
    $existing = (Get-ItemProperty $uninstallKey).InstallLocation
    if ([IO.Path]::GetFullPath($existing).TrimEnd('\') -ne $target.TrimEnd('\')) { throw 'A real installation exists; QA will not modify or uninstall it.' }
}
function State-Fingerprint {
    $proxy = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings'
    $proxyState = [ordered]@{}
    foreach ($name in @('ProxyEnable','ProxyServer','ProxyOverride','AutoConfigURL')) {
        $key = Get-Item $proxy
        $proxyState[$name] = @{ exists = $key.GetValueNames() -contains $name; value = $key.GetValue($name); kind = if ($key.GetValueNames() -contains $name) { $key.GetValueKind($name).ToString() } else { '' } }
    }
    $appData = Join-Path $env:LOCALAPPDATA 'EVEING/Lumiven'
    $directories = [Collections.Generic.List[string]]::new()
    $directories.Add($appData)
    $settings = Join-Path $appData 'settings.json'
    if (Test-Path -LiteralPath $settings) {
        $configuration = Get-Content -LiteralPath $settings -Raw | ConvertFrom-Json
        if ($configuration.downloadDirectory) { $directories.Add($configuration.downloadDirectory) }
        foreach ($directory in $configuration.importedDirectories) { $directories.Add($directory) }
    }
    $preserved = foreach ($directory in $directories | Sort-Object -Unique) {
        if (Test-Path -LiteralPath $directory) {
            foreach ($file in Get-ChildItem -LiteralPath $directory -Recurse -File | Sort-Object FullName) {
                "$($file.FullName)|$($file.Length)|$((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash)"
            }
        }
    }
    [ordered]@{
        proxy = $proxyState
        currentUserRoots = @(Get-ChildItem Cert:/CurrentUser/Root | ForEach-Object Thumbprint | Sort-Object)
        localMachineRoots = @(Get-ChildItem Cert:/LocalMachine/Root | ForEach-Object Thumbprint | Sort-Object)
        preservedFiles = @($preserved)
    } | ConvertTo-Json -Depth 8 -Compress
}
function Assert-State([string]$expected, [string]$phase) {
    if ((State-Fingerprint) -cne $expected) { throw "$phase changed proxy, root certificates, real AppData or media content." }
}
function Run-Setup([string]$path, [string]$log) {
    $process = Start-Process -FilePath $path -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS', '/TASKS=', "/DIR=`"$target`"", "/LOG=`"$log`"") -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "Installer exited with $($process.ExitCode)" }
}
$before = State-Fingerprint
Run-Setup $installer (Join-Path $qa 'install.log')
foreach ($name in @('EVEING.Lumiven.exe','lumiven-core.exe','App.xbf','MainWindow.xbf','Themes/Glass.xbf','EVEING.Lumiven.pri','LICENSE','THIRD-PARTY-NOTICES.md','unins000.exe')) {
    if (-not (Test-Path -LiteralPath (Join-Path $target $name))) { throw "Installed file missing: $name" }
}
$expectedCoreHash = (Get-FileHash -LiteralPath (Join-Path $root 'artifacts/publish/lumiven-core.exe') -Algorithm SHA256).Hash
$installedCoreHash = (Get-FileHash -LiteralPath (Join-Path $target 'lumiven-core.exe') -Algorithm SHA256).Hash
if ($installedCoreHash -ne $expectedCoreHash) { throw 'Installed core does not match the verified publish payload.' }
Assert-State $before 'Install'
$installedHash = (Get-FileHash -LiteralPath (Join-Path $target 'EVEING.Lumiven.exe') -Algorithm SHA256).Hash
$sentinel = Join-Path $qa 'preserved-media.txt'
[IO.File]::WriteAllText($sentinel, 'Installer QA media outside the application directory.')
$sentinelHash = (Get-FileHash -LiteralPath $sentinel -Algorithm SHA256).Hash
$baseVersion = [version]$Version
$upgradeVersion = '{0}.{1}.{2}' -f $baseVersion.Major, $baseVersion.Minor, ($baseVersion.Build + 1)
& (Join-Path $root '.tools/inno/ISCC.exe') /Qp "/DAppVersion=$upgradeVersion" "/DPublishDir=$(Join-Path $root 'artifacts/publish')" "/DReleaseDir=$qa" (Join-Path $root 'installer/Lumiven.iss')
if ($LASTEXITCODE -ne 0) { throw 'QA upgrade installer compile failed.' }
Run-Setup (Join-Path $qa "EVEING-Lumiven-Setup-$upgradeVersion-x64.exe") (Join-Path $qa 'update.log')
if ((Get-ItemProperty $uninstallKey).DisplayVersion -ne $upgradeVersion) { throw 'Upgrade failed to replace the installed application version.' }
if ((Get-FileHash -LiteralPath (Join-Path $target 'EVEING.Lumiven.exe') -Algorithm SHA256).Hash -ne $installedHash) { throw 'QA upgrade corrupted the payload.' }
if ((Get-FileHash -LiteralPath (Join-Path $target 'lumiven-core.exe') -Algorithm SHA256).Hash -ne $installedCoreHash) { throw 'QA upgrade changed the verified core payload.' }
Assert-State $before 'Version upgrade'
$uninstaller = Join-Path $target 'unins000.exe'
$process = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART', "/LOG=`"$(Join-Path $qa 'uninstall.log')`"") -WindowStyle Hidden -PassThru -Wait
if ($process.ExitCode -ne 0) { throw "Uninstall exited with $($process.ExitCode)" }
if (Test-Path -LiteralPath (Join-Path $target 'EVEING.Lumiven.exe')) { throw 'Uninstall left the installed application executable.' }
Assert-State $before 'Uninstall'
if ((Get-FileHash -LiteralPath $sentinel -Algorithm SHA256).Hash -ne $sentinelHash) { throw 'Uninstall changed preserved QA media.' }
if (Test-Path $uninstallKey) { throw 'Uninstall left its application registry entry.' }
$result = [ordered]@{ date = (Get-Date).ToString('o'); version = $Version; installed = $true; versionUpgrade = $true; uninstalled = $true; coreSha256 = $installedCoreHash; proxyUnchanged = $true; rootCertificatesUnchanged = $true; actualAppDataAndMediaUnchanged = $true; appAutoLaunched = $false; target = $target }
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $qa 'result.json') -Encoding utf8
$result | ConvertTo-Json -Compress | Write-Output
