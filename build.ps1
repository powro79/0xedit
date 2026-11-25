param([switch]$Test)
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath $framework)) {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Install .NET Framework 4.8 and run this script on Windows.' }

Push-Location $PSScriptRoot
try {
    $output = Join-Path $PSScriptRoot 'bin\Release'
    [void][IO.Directory]::CreateDirectory($output)
    $sources = @(Get-ChildItem -LiteralPath 'src' -Filter '*.cs' | Sort-Object Name | ForEach-Object FullName)
    $options = @('/nologo', '/optimize+', '/warn:4', '/warnaserror+', '/codepage:65001', '/platform:anycpu',
        '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll',
        '/win32manifest:app.manifest', '/win32icon:assets\0xEdit.ico', '/resource:assets\aurora.png,ZeroXEdit.aurora.png',
        '/resource:LICENSE,ZeroXEdit.LICENSE')
    $app = Join-Path $output '0xEdit.exe'
    & $compiler @options '/target:winexe' "/out:$app" @sources
    if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
    Write-Host "Built $app"
    if ($Test) {
        $runner = Join-Path $output '0xEdit.Tests.exe'
        & $compiler @options '/target:exe' '/main:ZeroXEdit.Tests' "/out:$runner" @sources 'tests\Tests.cs'
        if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
        & $runner
        if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    }
} finally { Pop-Location }
