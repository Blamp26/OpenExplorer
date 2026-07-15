param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',
    [switch] $SmokeTest
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $repoRoot

function Invoke-Checked {
    param(
        [Parameter(Mandatory)] [string] $FilePath,
        [Parameter(Mandatory)] [string[]] $Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath failed with exit code $LASTEXITCODE."
    }
}

$buildScript = Join-Path $repoRoot 'tools\build.ps1'
& $buildScript -Configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "Build pipeline failed with exit code $LASTEXITCODE."
}

$winAppCandidates = Get-ChildItem -LiteralPath (Join-Path $env:USERPROFILE '.nuget\packages\microsoft.windows.sdk.buildtools.winapp') -Filter 'winapp.exe' -Recurse -File |
    Where-Object { $_.FullName -match '\\tools\\win-x64\\winapp\.exe$' } |
    Sort-Object FullName -Descending
if (-not $winAppCandidates) {
    throw 'The installed Microsoft.Windows.SDK.BuildTools.WinApp winapp.exe was not found.'
}
$winApp = $winAppCandidates[0].FullName

$configurationRoot = Join-Path $repoRoot "apps\OpenExplorer.UI\bin\x64\$Configuration"
$frameworkOutput = Get-ChildItem -LiteralPath $configurationRoot -Directory -Filter 'net*-windows*' | Select-Object -First 1
if (-not $frameworkOutput) {
    throw "The UI output directory was not found under $configurationRoot."
}
$layout = Join-Path $frameworkOutput.FullName 'win-x64'
$manifest = Join-Path $layout 'AppxManifest.xml'
$appxLayout = Join-Path $layout 'AppX'
if (-not (Test-Path -LiteralPath $manifest)) {
    throw "The generated packaged manifest was not found: $manifest"
}

Write-Host "Launching packaged OpenExplorer ($Configuration|x64)..."
$launchOutput = @(& $winApp run $layout --manifest $manifest --output-appx-directory $appxLayout --detach --json 2>&1)
$launchExitCode = $LASTEXITCODE
$launchText = $launchOutput -join [Environment]::NewLine
if ($launchExitCode -ne 0) {
    throw "Packaged launch failed with exit code $launchExitCode.`n$launchText"
}

try {
    $launchInfo = $launchText | ConvertFrom-Json
} catch {
    throw "Packaged launch did not return the expected JSON process record.`n$launchText"
}
$processId = [int]$launchInfo.ProcessId
if ($processId -le 0) {
    throw "Packaged launch returned an invalid process ID: $processId"
}

$process = Get-Process -Id $processId -ErrorAction SilentlyContinue
if (-not $process) {
    throw "OpenExplorer exited immediately after packaged activation (PID $processId)."
}

if (-not $SmokeTest) {
    Write-Host "OpenExplorer launched (PID: $processId)."
    exit 0
}

try {
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    $windowHandle = [IntPtr]::Zero
    while ([DateTime]::UtcNow -lt $deadline) {
        $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
        if (-not $process) {
            throw "OpenExplorer exited before creating a top-level window (PID $processId)."
        }
        $process.Refresh()
        if ($process.MainWindowHandle -ne 0) {
            $windowHandle = [IntPtr]$process.MainWindowHandle
            break
        }
        Start-Sleep -Milliseconds 250
    }
    if ($windowHandle -eq [IntPtr]::Zero) {
        throw "OpenExplorer did not create a top-level window within 15 seconds (PID $processId)."
    }

    $processPath = try { $process.Path } catch { '<unavailable>' }
    $windowTitle = try { $process.MainWindowTitle } catch { '<unavailable>' }
    Write-Host "Launch smoke: PID=$processId Path=$processPath HWND=$windowHandle Title=$windowTitle"

    Start-Sleep -Seconds 5
    $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
    if (-not $process) {
        throw "OpenExplorer exited before the required five-second alive interval completed."
    }
    Write-Host 'Launch smoke passed: top-level window remained alive for 5 seconds.'
    exit 0
} finally {
    $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
    if ($process) {
        try { $process.CloseMainWindow() | Out-Null } catch { }
        try { $process.WaitForExit(5000) } catch { }
        $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
        if ($process) {
            Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
        }
    }
}
