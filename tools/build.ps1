[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $repoRoot

$target = 'x86_64-pc-windows-msvc'
$profileDirectory = if ($Configuration -eq 'Release') { 'release' } else { 'debug' }
$msBuild = 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe'
$nativeSource = Join-Path $repoRoot "target\$target\$profileDirectory\fast_explorer_ffi.dll"
$nativeDirectory = Join-Path $repoRoot "artifacts\native\win-x64\$Configuration"
$nativeDestination = Join-Path $nativeDirectory 'fast_explorer_ffi.dll'

function Invoke-Checked([string]$FilePath, [string[]]$Arguments) {
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath failed with exit code $LASTEXITCODE."
    }
}

Write-Host "[1/4] Rust workspace ($Configuration)"
$cargoArguments = @('build', '--workspace', '--target', $target)
if ($Configuration -eq 'Release') { $cargoArguments += '--release' }
Invoke-Checked 'cargo' $cargoArguments

Write-Host '[2/4] Copy native DLL'
if (-not (Test-Path -LiteralPath $nativeSource)) {
    throw "Native DLL was not produced: $nativeSource"
}
New-Item -ItemType Directory -Path $nativeDirectory -Force | Out-Null
Copy-Item -LiteralPath $nativeSource -Destination $nativeDestination -Force
Write-Host "Copied $nativeSource to $nativeDestination"

Write-Host '[3/4] Validate MSBuild'
if (-not (Test-Path -LiteralPath $msBuild)) { throw "MSBuild was not found: $msBuild" }

Write-Host '[4/4] Managed solution (x64)'
Invoke-Checked $msBuild @('FastExplorer.sln', '/restore', '/m', "/p:Configuration=$Configuration", '/p:Platform=x64')
Write-Host "Build completed: $Configuration|x64"
