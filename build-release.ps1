# 猫爪音乐 Release APK 构建脚本（真机 arm64 版）
# 用法: .\build-release.ps1
# 输出: CatClawMusic.Maui\bin\Release\net11.0-android\com.catclaw.music-Signed.apk
#
# 说明: 与 build-x64.ps1（模拟器用）一起拆分两个单 ABI 包。本脚本固定输出 arm64 单包，
#       供 ARM64 真机安装。发布指定 -r android-arm64，AndroidSupportedAbis 自动派生为
#       arm64-v8a，避免 FFmpeg 原生库在双 ABI 包里重复约 20MB。
#
# 说明: 脚本结尾会等待按键再关闭窗口，便于在双击运行时查看构建结果/报错。
#       若从已打开的终端运行，构建完成后按 Enter 即可退出。
#       CI/自动化场景可传 -NoPause 跳过等待。

param(
    [switch]$NoPause   # 静默模式：不等待按键（CI/命令行用）
)

# 只让 PowerShell cmdlet 的错误成为终止性错误。
# 注意: 不要用 "Stop"——原生命令(dotnet)写往 stderr 会被当成终止性错误，
# 导致脚本在管道调用(& script.ps1 2>&1 | Tee-Object)时误中断、退出码假报 1。
$ErrorActionPreference = "Continue"

# 保留严格模式，尽早暴露未定义变量/属性等低级错误
Set-StrictMode -Version Latest

# === 输出封装 ===
# Write-Host 只写宿主控制台，不进入任何数据流，所以 `| Tee-Object` 之类的管道
# 无法把脚本自身的输出写进日志文件。
# 这里做「按需分发」：
#   - 输出被重定向/接入管道时：只写信息流(6)，由调用方通过 `*>&1` 捕获，避免重复
#   - 直接运行时：只写宿主控制台，保留彩色输出
function Test-OutputRedirected {
    try { return -not $Host.UI.RawUI -or [Console]::IsOutputRedirected } catch { return $true }
}

function Write-Msg {
    param(
        [Parameter(Position = 0)][string]$Message = "",
        [ConsoleColor]$Color = [ConsoleColor]::Gray
    )
    if ($script:OutputRedirected) {
        Write-Information -MessageData $Message -InformationAction Continue
    } else {
        Write-Host $Message -ForegroundColor $Color
    }
}

$script:OutputRedirected = Test-OutputRedirected

# === 原生命令调用封装 ===
# 统一处理: 捕获 stdout/stderr、回显输出、检查退出码。
# 这样无论脚本是被双击运行、直接调用、还是接入管道/重定向调用，行为都一致。
function Invoke-Native {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$Arguments = @(),
        [string]$Label = ""
    )
    $prevEA = $ErrorActionPreference
    # 局部屏蔽: 保证原生命令的 stderr 输出不会升级为终止性错误
    $ErrorActionPreference = "Continue"
    try {
        & $FilePath @Arguments 2>&1 | ForEach-Object {
            # 原生命令输出统一按普通文本回显，避免 stderr 触发终止性错误
            $text = if ($_ -is [System.Management.Automation.ErrorRecord]) { $_.ToString() } else { [string]$_ }
            Write-Msg $text
        }
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $prevEA
    }
    if ($code -ne 0 -and $Label) {
        Write-Msg "$Label 失败（退出码 $code）。" -Color Red
    }
    return $code
}

# === 暂停并退出（成功时等待 60 秒后自动退出，或按 Enter 立即退出；失败时不自动退出） ===
function Pause-And-Exit {
    param(
        [int]$Code = 0
    )
    if ($NoPause) { exit $Code }
    Write-Msg ""
    if ($Code -eq 0) {
        Write-Msg "构建流程结束。" -Color Green
    } else {
        Write-Msg "构建流程异常终止（退出码 $Code）。" -Color Red
        Write-Host "按 Enter 键关闭窗口..." -ForegroundColor Gray
        Read-Host | Out-Null
        exit $Code
    }

    # 构建成功：倒计时 60 秒自动退出；期间按 Enter 立即退出（有输入则提前关闭）
    $countdown = 60
    Write-Host "构建完毕，按 Enter 立即退出，或 $countdown 秒后自动退出..." -ForegroundColor Gray
    try {
        # 有可读主机：轮询是否按键（Enter），超时则自动退出
        for ($i = $countdown; $i -gt 0; $i--) {
            if ($Host.UI.RawUI.KeyAvailable) {
                $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
                break
            }
            Start-Sleep -Seconds 1
        }
    } catch {
        # 非交互主机（如 CI/终端工具）：读不到按键时直接等满 60 秒
        Start-Sleep -Seconds $countdown
    }
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

# === 检查依赖 ===
Write-Msg "=== 猫爪音乐 Release APK 构建 ===" -Color Cyan
Write-Msg ""

if (-not (Test-Path $DotNetPath)) {
    Write-Msg "未找到 dotnet.exe: $DotNetPath" -Color Red
    Pause-And-Exit 1
}

if (-not (Test-Path $AndroidSdk)) {
    Write-Msg "未找到 Android SDK: $AndroidSdk" -Color Red
    Pause-And-Exit 1
}

if (-not (Test-Path $JavaSdk)) {
    Write-Msg "未找到 Java SDK: $JavaSdk" -Color Red
    Pause-And-Exit 1
}

if (-not (Test-Path $KeyStorePath)) {
    Write-Msg "未找到签名文件: $KeyStorePath" -Color Red
    Pause-And-Exit 1
}

Write-Msg "[1/4] 清理旧构建..." -Color Yellow
Get-ChildItem -Path "CatClawMusic.Maui\bin\$Config" -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
Get-ChildItem -Path "CatClawMusic.Maui\obj\$Config" -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
Write-Msg "  清理完成" -Color Green

Write-Msg ""
Write-Msg "[2/4] 构建 Release APK（签名，arm64）..." -Color Yellow

$OutputDir = "CatClawMusic.Maui\bin\$Config\$TargetFramework"

# 单 ABI（arm64）：传 ReleaseAbi=arm64，由 csproj 的 SelectReleaseAbi Target 把
# RuntimeIdentifiers 覆盖为 android-arm64，AndroidSupportedAbis 自动派生为 arm64-v8a
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

$androidExit = Invoke-Native -FilePath $DotNetPath -Arguments @(
    "publish", $ProjectPath,
    "-c", $Config,
    "-f", $TargetFramework,
    "-p:ReleaseAbi=arm64",
    "-p:Aapt2DaemonMaxInstanceCount=0",
    "-m:1",
    "-p:AndroidSdkDirectory=$AndroidSdk",
    "-p:JavaSdkDirectory=$JavaSdk",
    "-p:AndroidKeyStore=true",
    "-p:AndroidSigningKeyStore=$PWD\$KeyStorePath",
    "-p:AndroidSigningKeyAlias=$KeyAlias",
    "-p:AndroidSigningKeyPass=$KeyPass",
    "-p:AndroidSigningStorePass=$StorePass"
) -Label "dotnet publish (android)"

if ($androidExit -ne 0) {
    Write-Msg ""
    Write-Msg "构建失败！" -Color Red
    Pause-And-Exit $androidExit
}

$signedApk = "$OutputDir\publish\com.catclaw.music-Signed.apk"
if (-not (Test-Path $signedApk)) {
    $found = Get-ChildItem -Path $OutputDir -Filter "com.catclaw.music-Signed.apk" -Recurse | Select-Object -First 1
    if (-not $found) {
        Write-Msg "构建成功但未找到签名 APK" -Color Red
        Pause-And-Exit 1
    }
    $signedApk = $found.FullName
}

# 复制到统一的交付文件名
$dest = Join-Path $OutputDir "com.catclaw.music-Signed.apk"
Copy-Item $signedApk $dest -Force
$builtApk = Get-Item $dest

$stopwatch.Stop()

Write-Msg ""
Write-Msg "[3/4] 构建成功！" -Color Green
Write-Msg "  用时: $($stopwatch.Elapsed.ToString('mm\:ss'))"

Write-Msg ""
Write-Msg "[4/4] 构建结果:" -Color Cyan
$sizeMB = [math]::Round($builtApk.Length / 1MB, 2)
Write-Msg "  文件: $($builtApk.FullName)"
Write-Msg "  大小: $sizeMB MB"
Write-Msg "  时间: $($builtApk.LastWriteTime)"

Write-Msg ""
Write-Msg "=== 构建完成 ===" -Color Green

Pause-And-Exit 0
