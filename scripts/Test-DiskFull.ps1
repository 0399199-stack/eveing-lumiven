[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this isolated virtual-disk test from an elevated PowerShell.' }
$qa = Join-Path $repo ('artifacts/disk-full-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $qa | Out-Null
$image = Join-Path $qa 'test.vhdx'
$mount = Join-Path $qa 'mount'
$attached = $false
try {
    New-Item -ItemType Directory -Path $mount | Out-Null
    $create = Join-Path $qa 'create-vhd.txt'
    [IO.File]::WriteAllLines($create, @(('create vdisk file="' + $image + '" maximum=128 type=expandable'), 'exit'), [Text.Encoding]::ASCII)
    & "$env:SystemRoot/System32/diskpart.exe" /s $create
    if (!(Test-Path -LiteralPath $image)) { throw 'VHD creation failed.' }
    Mount-DiskImage -ImagePath $image -NoDriveLetter | Out-Null
    $attached = $true
    $disk = Get-DiskImage -ImagePath $image | Get-Disk
    if (@($disk).Count -ne 1 -or $disk.IsBoot -or $disk.IsSystem -or $disk.Size -ne 128MB -or $disk.PartitionStyle -ne 'RAW') { throw 'Unexpected disk identity; refusing initialization.' }
    Initialize-Disk -Number $disk.Number -PartitionStyle MBR -PassThru | Out-Null
    $partition = New-Partition -DiskNumber $disk.Number -UseMaximumSize
    $partition | Format-Volume -FileSystem NTFS -NewFileSystemLabel 'LumivenQATest' -Confirm:$false | Out-Null
    Add-PartitionAccessPath -DiskNumber $disk.Number -PartitionNumber $partition.PartitionNumber -AccessPath ($mount + '\')
    $verified = Get-DiskImage -ImagePath $image | Get-Disk
    if ($verified.Number -ne $disk.Number -or $verified.IsBoot -or $verified.IsSystem) { throw 'Disk identity changed.' }
    $result = & (Join-Path $repo '.tools/node-v24.19.0-win-x64/node.exe') (Join-Path $PSScriptRoot 'tests/disk-full.mjs') (Join-Path $repo 'artifacts/publish/lumiven-core.exe') $mount 2>&1
    $result | Out-String | Set-Content -LiteralPath (Join-Path $qa 'result.log')
    if ($LASTEXITCODE -ne 0) { throw "Disk-full test failed. See $qa/result.log" }
    Write-Host "Real disk-full test passed. Evidence: $qa/result.log"
} finally {
    if ($attached) { Dismount-DiskImage -ImagePath $image -ErrorAction Stop | Out-Null }
    # This exact image was created in our new, unique test directory; no physical disk is selected.
    if ((Test-Path -LiteralPath $image) -and !(Get-DiskImage -ImagePath $image).Attached) { Remove-Item -LiteralPath $image }
}
