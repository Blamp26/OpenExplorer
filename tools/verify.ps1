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

Write-Host '[1/13] cargo fmt'
Invoke-Checked 'cargo' @('fmt', '--all', '--check')

Write-Host '[2/13] cargo clippy'
Invoke-Checked 'cargo' @('clippy', '--workspace', '--all-targets', '--target', 'x86_64-pc-windows-msvc', '--', '-D', 'warnings')

Write-Host '[3/13] cargo test'
Invoke-Checked 'cargo' @('test', '--workspace', '--target', 'x86_64-pc-windows-msvc')

Write-Host '[4/13] build.ps1 Debug'
& (Join-Path $PSScriptRoot 'build.ps1') -Configuration Debug
if ($LASTEXITCODE -ne 0) { throw "build.ps1 failed with exit code $LASTEXITCODE." }

Write-Host '[5/13] native Interop smoke test'
$smokeDll = Join-Path $repoRoot 'tests\OpenExplorer.Interop.SmokeTests\bin\Debug\net10.0\OpenExplorer.Interop.SmokeTests.dll'
if (-not (Test-Path -LiteralPath $smokeDll)) { throw "Smoke test output was not found: $smokeDll" }
$smokeOutput = & dotnet $smokeDll
if ($LASTEXITCODE -ne 0) { throw "Interop smoke test failed with exit code $LASTEXITCODE." }
if ($smokeOutput -notcontains 'Native snapshot API version: 4, items: 100000, range paging passed') {
    throw "Smoke test did not report the expected API version. Output: $($smokeOutput -join ' | ')"
}
$smokeOutput

Write-Host '[6/13] local file provider smoke test'
$localSmokeDll = Join-Path $repoRoot 'tests\OpenExplorer.LocalFileProvider.SmokeTests\bin\Debug\net10.0\OpenExplorer.LocalFileProvider.SmokeTests.dll'
if (-not (Test-Path -LiteralPath $localSmokeDll)) { throw "Local file provider smoke output was not found: $localSmokeDll" }
$localOutput = & dotnet $localSmokeDll
if ($LASTEXITCODE -ne 0) { throw "Local file provider smoke test failed with exit code $LASTEXITCODE." }
if ($localOutput -notcontains 'Local file provider API version: 4, directory snapshot passed') { throw "Local file provider smoke test did not report the expected result. Output: $($localOutput -join ' | ')" }
$localOutput

Write-Host '[7/13] virtualization source smoke test'
$virtualizationSmokeDll = Join-Path $repoRoot 'tests\OpenExplorer.Virtualization.SmokeTests\bin\Debug\net10.0\OpenExplorer.Virtualization.SmokeTests.dll'
if (-not (Test-Path -LiteralPath $virtualizationSmokeDll)) { throw "Virtualization smoke test output was not found: $virtualizationSmokeDll" }
$virtualizationOutput = & dotnet $virtualizationSmokeDll
if ($LASTEXITCODE -ne 0) { throw "Virtualization smoke test failed with exit code $LASTEXITCODE." }
if (-not ($virtualizationOutput -match 'Snapshot virtualization source: 100000 items, page 256, cache <= 1024')) {
    throw "Virtualization smoke test did not report the expected source summary. Output: $($virtualizationOutput -join ' | ')"
}
$virtualizationOutput

Write-Host '[8/13] navigation smoke test'
$navigationSmokeDll = Join-Path $repoRoot 'tests\OpenExplorer.Navigation.SmokeTests\bin\Debug\net10.0\OpenExplorer.Navigation.SmokeTests.dll'
if (-not (Test-Path -LiteralPath $navigationSmokeDll)) { throw "Navigation smoke output was not found: $navigationSmokeDll" }
$navigationOutput = & dotnet $navigationSmokeDll
if ($LASTEXITCODE -ne 0) { throw "Navigation smoke test failed with exit code $LASTEXITCODE." }
if ($navigationOutput -notcontains 'Navigation model: history, stale requests, and local folder transitions passed') { throw "Navigation smoke test did not report the expected result. Output: $($navigationOutput -join ' | ')" }
$navigationOutput

Write-Host '[9/13] sorting smoke test'
$sortingSmokeDll = Join-Path $repoRoot 'tests\OpenExplorer.Sorting.SmokeTests\bin\Debug\net10.0\OpenExplorer.Sorting.SmokeTests.dll'
if (-not (Test-Path -LiteralPath $sortingSmokeDll)) { throw "Sorting smoke output was not found: $sortingSmokeDll" }
$sortingOutput = & dotnet $sortingSmokeDll
if ($LASTEXITCODE -ne 0) { throw "Sorting smoke test failed with exit code $LASTEXITCODE." }
if ($sortingOutput -notcontains 'Sorting model: native views and controller sort transitions passed') { throw "Sorting smoke test did not report the expected result. Output: $($sortingOutput -join ' | ')" }
$sortingOutput

Write-Host '[10/13] selection smoke test'
$selectionSmokeDll = Join-Path $repoRoot 'tests\OpenExplorer.Selection.SmokeTests\bin\Debug\net10.0\OpenExplorer.Selection.SmokeTests.dll'
if (-not (Test-Path -LiteralPath $selectionSmokeDll)) { throw "Selection smoke output was not found: $selectionSmokeDll" }
$selectionOutput = & dotnet $selectionSmokeDll
if ($LASTEXITCODE -ne 0) { throw "Selection smoke test failed with exit code $LASTEXITCODE." }
if ($selectionOutput -notcontains 'Selection model: transitions, inverted select-all, sorting preservation passed') { throw "Selection smoke test did not report the expected result. Output: $($selectionOutput -join ' | ')" }
$selectionOutput

Write-Host '[11/13] packaged launch smoke test'
$runScript = Join-Path $PSScriptRoot 'run.ps1'
& $runScript -Configuration Debug -SmokeTest
if ($LASTEXITCODE -ne 0) { throw "Packaged launch smoke test failed with exit code $LASTEXITCODE." }

Write-Host '[12/13] final WinUI solution build (Debug|x64)'
$msBuild = 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe'
Invoke-Checked $msBuild @('OpenExplorer.sln', '/m', '/p:Configuration=Debug', '/p:Platform=x64')
Write-Host '[13/13] verification complete'
Write-Host 'Verification completed.'
