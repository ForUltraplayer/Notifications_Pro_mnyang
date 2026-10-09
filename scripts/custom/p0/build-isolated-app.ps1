# @file build-isolated-app.ps1
# @author: rudals252
# @brief P0 only: copy src/NotificationsPro to .p0/isolated-src, redirect its AppData root to the
#        NP_P0_APPDATA environment variable (fail-closed), and build it. src/ is never modified.
#        Reason: Environment.GetFolderPath(ApplicationData) ignores $env:APPDATA on Windows, so the
#        unmodified build would write the original app's %AppData%\NotificationsPro\settings.json.
[CmdletBinding()]
param(
    [string]$RepoRoot
)
$ErrorActionPreference = 'Stop'
# PowerShell 5.1 leaves $PSScriptRoot empty inside param defaults, so resolve it here.
if (-not $RepoRoot) { $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path }

$srcDir = Join-Path $RepoRoot 'src\NotificationsPro'
$outRoot = Join-Path $RepoRoot '.p0\isolated-src'
$dstDir = Join-Path $outRoot 'NotificationsPro'

if (Test-Path $dstDir) { Remove-Item -LiteralPath $dstDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $dstDir | Out-Null

# Copy sources without build outputs.
robocopy $srcDir $dstDir /E /XD bin obj /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed with $LASTEXITCODE" }

$needle = 'Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)'
$replacement = '(Environment.GetEnvironmentVariable("NP_P0_APPDATA") ?? throw new InvalidOperationException("NP_P0_APPDATA is required for the P0 isolated build"))'
$targets = @(
    'Helpers\ManagedAssetPathHelper.cs',
    'Services\ProfileManager.cs',
    'Services\SettingsManager.cs',
    'Services\ThemeManager.cs'
)
foreach ($rel in $targets) {
    $path = Join-Path $dstDir $rel
    $text = [IO.File]::ReadAllText($path)
    if (-not $text.Contains($needle)) { throw "Patch site not found: $rel" }
    [IO.File]::WriteAllText($path, $text.Replace($needle, $replacement), (New-Object Text.UTF8Encoding($true)))
}

# Fail if any ApplicationData lookup remains in the copied sources.
$left = Get-ChildItem -LiteralPath $dstDir -Recurse -Filter *.cs | Select-String -SimpleMatch 'SpecialFolder.ApplicationData'
if ($left) { throw "Unpatched ApplicationData lookups remain: $($left | ForEach-Object { $_.Path + ':' + $_.LineNumber })" }

dotnet build (Join-Path $dstDir 'NotificationsPro.csproj') -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with $LASTEXITCODE" }

$exe = Join-Path $dstDir 'bin\Debug\net8.0-windows10.0.19041.0\NotificationsPro.exe'
if (-not (Test-Path $exe)) { throw "Built exe not found: $exe" }
Write-Output "ISOLATED_EXE=$exe"
