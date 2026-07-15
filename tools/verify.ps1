[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $repoRoot

function Invoke-Checked([string]$FilePath, [string[]]$Arguments) {
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath failed with exit code $LASTEXITCODE."
    }
}

Write-Host '[1/7] cargo fmt'
Invoke-Checked 'cargo' @('fmt', '--all', '--check')

Write-Host '[2/7] cargo clippy'
Invoke-Checked 'cargo' @('clippy', '--workspace', '--all-targets', '--target', 'x86_64-pc-windows-msvc', '--', '-D', 'warnings')

Write-Host '[3/7] cargo test'
Invoke-Checked 'cargo' @('test', '--workspace', '--target', 'x86_64-pc-windows-msvc')

Write-Host '[4/7] build.ps1 Debug'
& (Join-Path $PSScriptRoot 'build.ps1') -Configuration Debug
if ($LASTEXITCODE -ne 0) { throw "build.ps1 failed with exit code $LASTEXITCODE." }

Write-Host '[5/7] native Interop smoke test'
$smokeDll = Join-Path $repoRoot 'tests\OpenExplorer.Interop.SmokeTests\bin\Debug\net10.0\OpenExplorer.Interop.SmokeTests.dll'
if (-not (Test-Path -LiteralPath $smokeDll)) { throw "Smoke test output was not found: $smokeDll" }
$smokeOutput = & dotnet $smokeDll
if ($LASTEXITCODE -ne 0) { throw "Interop smoke test failed with exit code $LASTEXITCODE." }
if ($smokeOutput -notcontains 'Native API version: 1') {
    throw "Smoke test did not report the expected API version. Output: $($smokeOutput -join ' | ')"
}
$smokeOutput

Write-Host '[6/7] virtualization source smoke test'
$virtualizationSmokeDll = Join-Path $repoRoot 'tests\OpenExplorer.Virtualization.SmokeTests\bin\Debug\net10.0\OpenExplorer.Virtualization.SmokeTests.dll'
if (-not (Test-Path -LiteralPath $virtualizationSmokeDll)) { throw "Virtualization smoke test output was not found: $virtualizationSmokeDll" }
$virtualizationOutput = & dotnet $virtualizationSmokeDll
if ($LASTEXITCODE -ne 0) { throw "Virtualization smoke test failed with exit code $LASTEXITCODE." }
if (-not ($virtualizationOutput -match 'Virtualization source: 100000 items, cache <= 1024')) {
    throw "Virtualization smoke test did not report the expected source summary. Output: $($virtualizationOutput -join ' | ')"
}
$virtualizationOutput

Write-Host '[7/7] final WinUI solution build (Debug|x64)'
$msBuild = 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe'
Invoke-Checked $msBuild @('OpenExplorer.sln', '/m', '/p:Configuration=Debug', '/p:Platform=x64')
Write-Host 'Verification completed.'
