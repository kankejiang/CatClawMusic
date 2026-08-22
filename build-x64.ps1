# 猫爪音乐 Release APK 构建脚本（模拟器 x64 版）
# 用法: .\build-x64.ps1
# 输出: CatClawMusic.Maui\bin\Release\net11.0-android\com.catclaw.music-x64-Signed.apk
#
# 说明: 与 build-release.ps1（真机用）一起拆分两个单 ABI 包。本脚本固定输出 x86_64 单包，
#       供 x64 模拟器（如 MuMu）安装。发布指定 -r android-x64，AndroidSupportedAbis 自动
#       派生为 x86_64。注意：FFmpeg 目前只有 arm64 原生库，x64 包不包含 FFmpeg 转码功能。
#
# 说明: 脚本结尾会等待按键再关闭窗口，便于在双击运行时查看构建结果/报错。
#       若从已打开的终端运行，构建完成后按 Enter 即可退出。

$ErrorActionPreference = "Stop"

# === 暂停并退出（保持窗口不自动关闭） ===
function Pause-And-Exit {
    param(
        [int]$Code = 0
    )
    Write-Host ""
    if ($Code -eq 0) {
        Write-Host "构建流程结束。" -ForegroundColor Green
    } else {
        Write-Host "构建流程异常终止（退出码 $Code）。" -ForegroundColor Red
    }
    Write-Host "按 Enter 键关闭窗口..." -ForegroundColor Gray
    Read-Host | Out-Null
    exit $Code
}

# === 配置 ===
$ProjectPath = "CatClawMusic.Maui\CatClawMusic.Maui.csproj"
$TargetFramework = "net11.0-android"
$Config = "Release"

# 签名信息（与 SIGNING.md 一致）
$KeyStorePath = "catclaw.keystore"
$KeyAlias = "catclaw"
$KeyPass = "catclaw123"
$StorePass = "catclaw123"

# SDK 路径（自动检测）
$AndroidSdk = "C:\Users\$env:USERNAME\AppData\Local\Android\Sdk"
$JavaSdk = "C:\Program Files\Android\openjdk\jdk-21.0.8"

# dotnet 路径
$DotNetPath = "C:\Program Files\dotnet\dotnet.exe"

# 输出 APK 文件名（与 arm64 区分）
$OutApkName = "com.catclaw.music-x64-Signed.apk"

# === 检查依赖 ===
Write-Host "=== 猫爪音乐 Release APK 构建（x86_64 / 模拟器）===" -ForegroundColor Cyan
Write-Host ""

if (-not (Test-Path $DotNetPath)) {
    Write-Error "未找到 dotnet.exe: $DotNetPath"
    Pause-And-Exit 1
}

if (-not (Test-Path $AndroidSdk)) {
    Write-Error "未找到 Android SDK: $AndroidSdk"
    Pause-And-Exit 1
}

if (-not (Test-Path $JavaSdk)) {
    Write-Error "未找到 Java SDK: $JavaSdk"
    Pause-And-Exit 1
}

if (-not (Test-Path $KeyStorePath)) {
    Write-Error "未找到签名文件: $KeyStorePath"
    Pause-And-Exit 1
}

Write-Host "[1/4] 清理旧构建..." -ForegroundColor Yellow
Get-ChildItem -Path "CatClawMusic.Maui\bin\$Config" -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
Get-ChildItem -Path "CatClawMusic.Maui\obj\$Config" -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "  清理完成" -ForegroundColor Green

Write-Host ""
Write-Host "[2/4] 构建 Release APK（签名，x64）..." -ForegroundColor Yellow

$OutputDir = "CatClawMusic.Maui\bin\$Config\$TargetFramework"

# 单 ABI（x64）：传 ReleaseAbi=x64，由 csproj 的 SelectReleaseAbi Target 把
# RuntimeIdentifiers 覆盖为 android-x64，AndroidSupportedAbis 自动派生为 x86_64
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

& $DotNetPath publish $ProjectPath `
    -c $Config `
    -f $TargetFramework `
    -p:ReleaseAbi=x64 `
    -p:Aapt2DaemonMaxInstanceCount=0 `
    -m:1 `
    -p:AndroidSdkDirectory="$AndroidSdk" `
    -p:JavaSdkDirectory="$JavaSdk" `
    -p:AndroidKeyStore=true `
    -p:AndroidSigningKeyStore="$PWD\$KeyStorePath" `
    -p:AndroidSigningKeyAlias="$KeyAlias" `
    -p:AndroidSigningKeyPass="$KeyPass" `
    -p:AndroidSigningStorePass="$StorePass"

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Error "构建失败！"
    Pause-And-Exit $LASTEXITCODE
}

$signedApk = "$OutputDir\publish\com.catclaw.music-Signed.apk"
if (-not (Test-Path $signedApk)) {
    $found = Get-ChildItem -Path $OutputDir -Filter "com.catclaw.music-Signed.apk" -Recurse | Select-Object -First 1
    if (-not $found) {
        Write-Error "构建成功但未找到签名 APK"
        Pause-And-Exit 1
    }
    $signedApk = $found.FullName
}

# 复制到区分的交付文件名
$dest = Join-Path $OutputDir $OutApkName
Copy-Item $signedApk $dest -Force
$builtApk = Get-Item $dest

$stopwatch.Stop()

Write-Host ""
Write-Host "[3/4] 构建成功！" -ForegroundColor Green
Write-Host "  用时: $($stopwatch.Elapsed.ToString('mm\:ss'))"

Write-Host ""
Write-Host "[4/4] 构建结果:" -ForegroundColor Cyan
$sizeMB = [math]::Round($builtApk.Length / 1MB, 2)
Write-Host "  文件: $($builtApk.FullName)"
Write-Host "  大小: $sizeMB MB"
Write-Host "  时间: $($builtApk.LastWriteTime)"

Write-Host ""
Write-Host "=== 构建完成 ===" -ForegroundColor Green

Pause-And-Exit 0