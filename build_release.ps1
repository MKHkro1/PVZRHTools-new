# PVZRHTools Build Script
# Auto build and output to .release folder

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$SolutionPath = Join-Path $ScriptDir "PVZRHTools.sln"
$ReleaseDir = Join-Path $ScriptDir ".release"

Write-Host "========================================"
Write-Host "PVZRHTools Build Script"
Write-Host "========================================"

# Build solution
Write-Host ""
Write-Host "[1/3] Building solution..."
dotnet build $SolutionPath -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!"
    exit 1
}
Write-Host "Build succeeded!"

# Copy ToolModBepInEx and ToolModData to BepInEx/plugins
Write-Host ""
Write-Host "[2/3] Copying BepInEx plugin files..."
$PluginsDir = Join-Path $ReleaseDir "BepInEx\plugins"

$ToolModBepInExDll = Join-Path $ScriptDir "ToolModBepInEx\bin\Release\net6.0\ToolModBepInEx.dll"
$ToolModDataDll = Join-Path $ScriptDir "ToolModData\bin\Release\net6.0\ToolModData.dll"

if (Test-Path $ToolModBepInExDll) {
    Copy-Item $ToolModBepInExDll -Destination $PluginsDir -Force
    Write-Host "  Copied: ToolModBepInEx.dll"
}

if (Test-Path $ToolModDataDll) {
    Copy-Item $ToolModDataDll -Destination $PluginsDir -Force
    Write-Host "  Copied: ToolModData.dll"
}

# Copy PVZRHTools to PVZRHTools folder
Write-Host ""
Write-Host "[3/3] Copying PVZRHTools files..."
$PVZRHToolsOutputDir = Join-Path $ScriptDir "PVZRHTools\bin\Release\net8.0-windows"
$PVZRHToolsReleaseDir = Join-Path $ReleaseDir "PVZRHTools"

$FilesToCopy = @(
    "PVZRHTools.exe",
    "PVZRHTools.dll",
    "PVZRHTools.deps.json",
    "PVZRHTools.runtimeconfig.json",
    "ToolModData.dll",
    "CommunityToolkit.Mvvm.dll",
    "FastHotKeyForWPF.dll",
    "HandyControl.dll",
    "Microsoft.Extensions.DependencyInjection.dll",
    "Microsoft.Extensions.DependencyInjection.Abstractions.dll",
    "System.CodeDom.dll",
    "System.Management.dll"
)

foreach ($file in $FilesToCopy) {
    $SourceFile = Join-Path $PVZRHToolsOutputDir $file
    if (Test-Path $SourceFile) {
        Copy-Item $SourceFile -Destination $PVZRHToolsReleaseDir -Force
        Write-Host "  Copied: $file"
    }
}

# Copy runtimes folder
$RuntimesSource = Join-Path $PVZRHToolsOutputDir "runtimes"
$RuntimesDest = Join-Path $PVZRHToolsReleaseDir "runtimes"
if (Test-Path $RuntimesSource) {
    Copy-Item $RuntimesSource -Destination $PVZRHToolsReleaseDir -Recurse -Force
    Write-Host "  Copied: runtimes folder"
}

Write-Host ""
Write-Host "========================================"
Write-Host "Build completed! Output: $ReleaseDir"
Write-Host "========================================"
