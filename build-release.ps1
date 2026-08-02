# 猫爪音乐 Release APK 构建脚本
# 用法: .\build-release.ps1
# 输出: CatClawMusic.Maui\bin\Release\net10.0-android\publish\com.catclaw.music-Signed.apk
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
$TargetFramework = "net10.0-android"
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

# === 检查依赖 ===
Write-Host "=== 猫爪音乐 Release APK 构建 ===" -ForegroundColor Cyan
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
Write-Host "[2/4] 构建 Release APK（签名）..." -ForegroundColor Yellow

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

& $DotNetPath publish $ProjectPath `
    -c $Config `
    -f $TargetFramework `
    -p:Aapt2DaemonMaxInstanceCount=0 `
    -m:1 `
    -p:AndroidSdkDirectory="$AndroidSdk" `
    -p:JavaSdkDirectory="$JavaSdk" `
    -p:AndroidKeyStore=true `
    -p:AndroidSigningKeyStore="$PWD\$KeyStorePath" `
    -p:AndroidSigningKeyAlias="$KeyAlias" `
    -p:AndroidSigningKeyPass="$KeyPass" `
    -p:AndroidSigningStorePass="$StorePass"

$stopwatch.Stop()

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Error "构建失败！"
    Pause-And-Exit $LASTEXITCODE
}

Write-Host ""
Write-Host "[3/4] 构建成功！" -ForegroundColor Green
Write-Host "  用时: $($stopwatch.Elapsed.ToString('mm\:ss'))"

# === 查找输出 APK ===
$ApkPath = "CatClawMusic.Maui\bin\$Config\$TargetFramework\publish\com.catclaw.music-Signed.apk"

if (-not (Test-Path $ApkPath)) {
    Write-Host "  警告: 未在 publish 目录找到 APK，搜索中..." -ForegroundColor Yellow
    $found = Get-ChildItem -Path "CatClawMusic.Maui\bin\$Config" -Filter "*.apk" -Recurse | Select-Object -First 1
    if ($found) {
        $ApkPath = $found.FullName
    } else {
        Write-Error "未找到生成的 APK 文件"
        Pause-And-Exit 1
    }
}

$apkFile = Get-Item $ApkPath
$sizeMB = [math]::Round($apkFile.Length / 1MB, 2)

Write-Host ""
Write-Host "[4/4] 构建结果:" -ForegroundColor Cyan
Write-Host "  文件: $($apkFile.FullName)"
Write-Host "  大小: $sizeMB MB"
Write-Host "  时间: $($apkFile.LastWriteTime)"

Write-Host ""
Write-Host "=== 构建完成 ===" -ForegroundColor Green

Pause-And-Exit 0
