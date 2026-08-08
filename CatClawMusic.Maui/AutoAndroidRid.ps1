# AutoAndroidRid.ps1
# 在 Debug + Android 构建时自动探测目标设备，决定 APK 的 ABI 范围：
#   - 唯一在线设备为 x86_64 且开启了 native bridge（ARM 翻译层，如 MuMu 模拟器）时，
#     输出决策 "x64-only"：只打 x86_64 库，避免包管理器把 primaryCpuAbi 定为 arm64-v8a
#     走翻译层执行（mono 调试器连不上宿主 vsdbg，超时卡死 + 崩溃）。
#   - 其余情况（真机 / 多设备 / 无设备 / adb 不可用）输出决策 "default"：保持双 ABI。
# 决策与上次不同时删除旧 APK，强制 MSBuild 重新打包。
#
# 用法：AutoAndroidRid.ps1 -AdbPath <adb.exe> -DecisionFile <auto-rid.txt> -ObjApkDir <obj\...\android\bin> -BinApkDir <bin\...>

param(
    [Parameter(Mandatory = $true)][string]$AdbPath,
    [Parameter(Mandatory = $true)][string]$DecisionFile,
    [Parameter(Mandatory = $true)][string]$ObjApkDir,
    [Parameter(Mandatory = $true)][string]$BinApkDir
)

$ErrorActionPreference = 'SilentlyContinue'
$decision = 'default'
$abi = ''
$bridge = ''
$deviceCount = 0

# adb 路径兜底：参数传的 SDK 路径不存在时，退回用户目录下的 SDK
if (-not (Test-Path $AdbPath)) {
    $alt = Join-Path $env:LOCALAPPDATA 'Android\Sdk\platform-tools\adb.exe'
    if (Test-Path $alt) { $AdbPath = $alt }
}

try {
    if (Test-Path $AdbPath) {
        $out = & $AdbPath devices 2>$null
        $lines = @($out | Where-Object { $_ -match '^\S+\s+device$' })
        $serials = @($lines | ForEach-Object { ($_ -split '\s+')[0] })
        $deviceCount = $serials.Count
        if ($deviceCount -eq 1) {
            $abi = (& $AdbPath -s $serials[0] shell getprop ro.product.cpu.abi 2>$null | Out-String).Trim()
            $bridge = (& $AdbPath -s $serials[0] shell getprop ro.enable.native.bridge.exec 2>$null | Out-String).Trim()
            if ($abi -eq 'x86_64' -and $bridge -eq '1') { $decision = 'x64-only' }
        }
    }
} catch {
    $decision = 'default'
}

# 决策变化 → 删除 APK，强制下次构建重新打包（避免 MSBuild 增量误判 up-to-date）
$prev = ''
if (Test-Path $DecisionFile) { $prev = (Get-Content $DecisionFile -Raw).Trim() }
if ($prev -ne $decision) {
    Get-ChildItem $ObjApkDir -Filter '*.apk' | Remove-Item -Force
    Get-ChildItem $BinApkDir -Filter '*.apk' | Remove-Item -Force
}

Set-Content -Path $DecisionFile -Value $decision -Encoding ASCII
Write-Output "decision=$decision abi=$abi native_bridge=$bridge devices=$deviceCount"
