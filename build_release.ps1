# PVZRHTools Build Script
# Auto build and output to .release folder

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$SolutionPath = Join-Path $ScriptDir "PVZRHTools.sln"
$ReleaseDir = Join-Path $ScriptDir ".release"
$PVZRHToolsProjectPath = Join-Path $ScriptDir "PVZRHTools\PVZRHTools.csproj"
$RuntimeIdentifier = "win-x64"
$SelfContainedPublishDir = Join-Path $ScriptDir "PVZRHTools\bin\Release\net8.0-windows\publish-selfcontained"

Write-Host "========================================"
Write-Host "PVZRHTools Build Script"
Write-Host "========================================"

# Resolve dotnet executable from PATH or default install location
$dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
$DotnetExe = $null
if ($dotnetCmd) {
    $DotnetExe = $dotnetCmd.Source
}
if (-not $DotnetExe) {
    $defaultDotnet = "C:\Program Files\dotnet\dotnet.exe"
    if (Test-Path $defaultDotnet) {
        $DotnetExe = $defaultDotnet
    }
}
if (-not $DotnetExe) {
    Write-Host "dotnet not found. Please install .NET SDK or add dotnet to PATH."
    exit 1
}

# Clean + build solution
Write-Host ""
Write-Host "[1/4] Cleaning solution..."
& $DotnetExe clean $SolutionPath -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "Clean failed!"
    exit 1
}

Write-Host ""
Write-Host "[2/4] Building solution (full rebuild, no incremental)..."
& $DotnetExe build $SolutionPath -c Release --no-incremental
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!"
    exit 1
}
Write-Host "Build succeeded!"

Write-Host ""
Write-Host "[3/5] Publishing PVZRHTools as self-contained ($RuntimeIdentifier)..."
if (Test-Path $SelfContainedPublishDir) {
    Remove-Item -Path $SelfContainedPublishDir -Recurse -Force -ErrorAction SilentlyContinue
}
& $DotnetExe publish $PVZRHToolsProjectPath -c Release -r $RuntimeIdentifier --self-contained true -p:PublishSingleFile=false -p:PublishReadyToRun=false -o $SelfContainedPublishDir
if ($LASTEXITCODE -ne 0) {
    Write-Host "Publish failed!"
    exit 1
}
Write-Host "Publish succeeded!"

# Prepare release folders to ensure files are re-output from current build
Write-Host ""
Write-Host "[4/5] Preparing release folders..."
$PluginsDir = Join-Path $ReleaseDir "BepInEx\plugins"
$PVZRHToolsReleaseDir = Join-Path $ReleaseDir "PVZRHTools"
if (Test-Path $PluginsDir) {
    Get-ChildItem -Path $PluginsDir -File -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
} else {
    New-Item -ItemType Directory -Path $PluginsDir -Force | Out-Null
}
if (Test-Path $PVZRHToolsReleaseDir) {
    Get-ChildItem -Path $PVZRHToolsReleaseDir -File -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
    Remove-Item -Path (Join-Path $PVZRHToolsReleaseDir "runtimes") -Recurse -Force -ErrorAction SilentlyContinue
} else {
    New-Item -ItemType Directory -Path $PVZRHToolsReleaseDir -Force | Out-Null
}

# Copy ToolModBepInEx and ToolModData to BepInEx/plugins
Write-Host ""
Write-Host "[5/5] Copying release files..."

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

# Copy PVZRHTools self-contained output to PVZRHTools folder
$PVZRHToolsOutputDir = $SelfContainedPublishDir

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
    # 排除 interop 相关文件
    if ($file -match "interop|Interop") {
        Write-Host "  Skipped: $file (interop file)"
        continue
    }
    
    $SourceFile = Join-Path $PVZRHToolsOutputDir $file
    if (Test-Path $SourceFile) {
        Copy-Item $SourceFile -Destination $PVZRHToolsReleaseDir -Force
        Write-Host "  Copied: $file"
    }
}

# Copy remaining self-contained files (runtime and native dependencies)
Get-ChildItem -Path $PVZRHToolsOutputDir -File | ForEach-Object {
    if ($FilesToCopy -contains $_.Name) {
        return
    }
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $PVZRHToolsReleaseDir $_.Name) -Force
    Write-Host "  Copied: $($_.Name)"
}

# Copy runtimes folder from publish output
$RuntimesSource = Join-Path $PVZRHToolsOutputDir "runtimes"
if (Test-Path $RuntimesSource) {
    Copy-Item $RuntimesSource -Destination $PVZRHToolsReleaseDir -Recurse -Force
    Write-Host "  Copied: runtimes folder"
}

Write-Host ""
Write-Host "========================================"
Write-Host "Build completed! Output: $ReleaseDir"
Write-Host "========================================"
