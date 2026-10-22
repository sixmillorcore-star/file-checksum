param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'Build.ps1') }
$releaseFolder = Join-Path $PSScriptRoot 'release'
New-Item -ItemType Directory -Path $releaseFolder -Force | Out-Null
$exe = Join-Path $PSScriptRoot 'build/FileChecksum.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Build File Checksum first.' }
if ([System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion -ne '1.0.0.0') { throw 'The executable version does not match this package.' }
$stage = Join-Path $releaseFolder ('.stage-' + [Guid]::NewGuid().ToString('N'))
$appFolder = Join-Path $stage 'FileChecksum'
New-Item -ItemType Directory -Path $appFolder -Force | Out-Null
try {
    Copy-Item -LiteralPath $exe -Destination $appFolder
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'build/FileChecksum.exe.config') -Destination $appFolder
    foreach ($name in 'README.md','LICENSE','CHANGELOG.md','VALIDATION.md') {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $appFolder
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs') -Destination (Join-Path $appFolder 'docs') -Recurse
    $packageHashes = Get-ChildItem -LiteralPath $appFolder -File | Where-Object { $_.Extension -in '.exe','.config' } | Sort-Object Name | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.Name }
    [System.IO.File]::WriteAllText((Join-Path $appFolder 'SHA256SUMS.txt'), (($packageHashes -join "`n") + "`n"), (New-Object System.Text.UTF8Encoding($false)))
    $zip = Join-Path $releaseFolder 'FileChecksum-v1.0.0-windows.zip'
    Compress-Archive -LiteralPath $appFolder -DestinationPath $zip -Force
    $archiveHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    [System.IO.File]::WriteAllText((Join-Path $releaseFolder 'SHA256SUMS.txt'), ($archiveHash + '  FileChecksum-v1.0.0-windows.zip' + "`n"), (New-Object System.Text.UTF8Encoding($false)))
    Write-Output $zip
    Write-Output ('SHA-256: ' + $archiveHash)
} finally {
    $resolvedRelease = [System.IO.Path]::GetFullPath($releaseFolder).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $resolvedStage = [System.IO.Path]::GetFullPath($stage)
    if (-not $resolvedStage.StartsWith($resolvedRelease,[System.StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing to remove a staging folder outside release.' }
    Remove-Item -LiteralPath $resolvedStage -Recurse -Force
}
