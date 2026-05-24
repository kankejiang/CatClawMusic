# build_native.ps1 - 使用 ndk-build 编译 C++ 原生库为 Android .so 文件
#
# 使用 Android NDK 的 ndk-build 工具链交叉编译
# 输出三个 ABI：arm64-v8a, armeabi-v7a, x86_64

param(
    [string]$NDKPath = "C:\AndroidNDK",
    [string]$ProjectDir = "$PSScriptRoot",
    [string]$OutputDir = "$PSScriptRoot\..\CatClawMusic.UI\libs"
)

$ErrorActionPreference = "Stop"

$NdkBuildPath = Join-Path $NDKPath "ndk-build.cmd"
if (-not (Test-Path $NdkBuildPath)) {
    # 尝试 ndk-bundle 路径
    $NDKPath = "C:\Program Files (x86)\Android\android-sdk\ndk-bundle"
    $NdkBuildPath = Join-Path $NDKPath "ndk-build.cmd"
}

if (-not (Test-Path $NdkBuildPath)) {
    Write-Error "ndk-build not found. Tried:"
    Write-Error "  - C:\Program Files (x86)\Android\AndroidNDK\android-ndk-r27c\ndk-build.cmd"
    Write-Error "  - C:\Program Files (x86)\Android\android-sdk\ndk-bundle\ndk-build.cmd"
    exit 1
}

Write-Host "Using NDK: $NDKPath" -ForegroundColor Cyan
Write-Host "ndk-build: $NdkBuildPath" -ForegroundColor Cyan

# 清理之前的构建
Write-Host "`nCleaning previous build..." -ForegroundColor Yellow
& $NdkBuildPath -C $ProjectDir NDK_PROJECT_PATH=$ProjectDir APP_BUILD_SCRIPT="$ProjectDir\Android.mk" clean 2>$null

# 编译所有 ABI
Write-Host "`nBuilding native library..." -ForegroundColor Yellow
& $NdkBuildPath -C $ProjectDir NDK_PROJECT_PATH=$ProjectDir APP_BUILD_SCRIPT="$ProjectDir\Android.mk" NDK_APPLICATION_MK="$ProjectDir\Application.mk" -j4

if ($LASTEXITCODE -ne 0) {
    Write-Error "ndk-build failed"
    exit 1
}

# 复制 .so 文件到输出目录
$LibsDir = Join-Path $ProjectDir "libs"
if (-not (Test-Path $LibsDir)) {
    Write-Error "Output libs directory not found: $LibsDir"
    exit 1
}

$ABIs = @("arm64-v8a", "armeabi-v7a", "x86_64")
foreach ($ABI in $ABIs) {
    $SrcDir = Join-Path $LibsDir $ABI
    $DstDir = Join-Path $OutputDir $ABI

    $SoFile = Join-Path $SrcDir "libcatclaw_native.so"
    if (Test-Path $SoFile) {
        New-Item -ItemType Directory -Force -Path $DstDir | Out-Null
        Copy-Item $SoFile $DstDir -Force
        $size = (Get-Item (Join-Path $DstDir "libcatclaw_native.so")).Length
        Write-Host "OK: libcatclaw_native.so ($ABI) - $size bytes" -ForegroundColor Green
    } else {
        Write-Host "SKIP: $ABI not built" -ForegroundColor Yellow
    }
}

Write-Host "`n========================================" -ForegroundColor Green
Write-Host "Native library built successfully!" -ForegroundColor Green
Write-Host "Output: $OutputDir" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
