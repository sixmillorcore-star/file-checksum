[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryDirectory = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath -PathType Leaf)) {
    throw 'The .NET Framework 4.x C# compiler was not found. Run these tests on Windows with .NET Framework 4.8 installed.'
}

$artifactDirectory = Join-Path $PSScriptRoot '.artifacts'
[System.IO.Directory]::CreateDirectory($artifactDirectory) | Out-Null
$executablePath = Join-Path $artifactDirectory 'UiSmokeTests.exe'
$compilerArguments = @(
    '/nologo'
    '/target:exe'
    '/langversion:5'
    '/codepage:65001'
    '/warn:4'
    '/warnaserror+'
    '/optimize+'
    '/reference:System.Core.dll'
    '/reference:System.Drawing.dll'
    '/reference:System.Windows.Forms.dll'
    ('/out:' + $executablePath)
    (Join-Path $repositoryDirectory 'src\HashService.cs')
    (Join-Path $repositoryDirectory 'src\ChecksumWriter.cs')
    (Join-Path $repositoryDirectory 'src\MainForm.cs')
    (Join-Path $PSScriptRoot 'UiSmokeTests.cs')
)
& $compilerPath @compilerArguments
if ($LASTEXITCODE -ne 0) {
    throw ('UI smoke test compilation failed with exit code ' + $LASTEXITCODE + '.')
}

& $executablePath
if ($LASTEXITCODE -ne 0) {
    throw ('UI smoke tests failed with exit code ' + $LASTEXITCODE + '.')
}
