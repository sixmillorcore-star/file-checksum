param()
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe' }
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The .NET Framework C# compiler was not found.' }
$buildFolder = Join-Path $PSScriptRoot 'build'
New-Item -ItemType Directory -Path $buildFolder -Force | Out-Null
$icon = Join-Path $PSScriptRoot 'assets/app.ico'
if (-not (Test-Path -LiteralPath $icon)) { & (Join-Path $PSScriptRoot 'Generate-Icon.ps1') }
$sources = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object FullName
$compileArguments = @('/nologo','/target:winexe','/platform:anycpu','/optimize+','/warn:4','/warnaserror+','/langversion:5','/codepage:65001','/utf8output',('/out:' + (Join-Path $buildFolder 'FileChecksum.exe')),('/win32icon:' + $icon),('/win32manifest:' + (Join-Path $PSScriptRoot 'src/app.manifest')),'/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll') + $sources
& $compiler @compileArguments
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE" }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src/app.config') -Destination (Join-Path $buildFolder 'FileChecksum.exe.config') -Force
Write-Output 'Built build/FileChecksum.exe'
