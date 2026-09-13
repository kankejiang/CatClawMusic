# 猫爪音乐 Windows Release 一键打包脚本
# 用法: 双击运行（打包完会暂停等待按键，方便看结果），或 .\build-win-release.ps1
# 输出: release\windows\catclaw.music-<版本>-Setup.exe
# 流程: ① dotnet publish 绿色目录(self-contained, 去pdb, 多语言保留) -> ② ISCC 编译 Inno Setup 安装程序
# 依赖: .NET SDK(已装) + Inno Setup 7(ISCC.exe, 未装时脚本会提示)
# 版本号自动从 csproj 读取 ApplicationDisplayVersion，以后发版只需改 csproj
# 注意: 本文件必须为 UTF-8 with BOM + CRLF（Windows PowerShell 5.1 按 ANSI 解码，无 BOM 中文会乱码）

param(
    [switch]$NoPause,   # 静默模式：不等待按键（CI/命令行用）
    [string]$OutDir = "bin\win-release",   # 输出目录（固定，相对 Maui 项目目录，可自定义）
    [string]$ObjDir = "obj\win-release"    # 中间目录（固定，相对 Maui 项目目录，可自定义）
)

# 只让 PowerShell cmdlet 的错误成为终止性错误。
# 注意: 不要用 "Stop"——原生命令(dotnet/ISCC)写往 stderr 会被当成终止性错误，
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
    # 宿主不支持 RawUI（如非交互主机）时，按已重定向处理
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

function Pause-And-Exit {
    param([int]$Code = 0)
    Write-Host ""
    if ($Code -eq 0) { Write-Msg "打包流程结束。" -Color Green }
    else { Write-Msg "打包流程异常终止（退出码 $Code）。" -Color Red }
    if (-not $NoPause) {
        Write-Host "按 Enter 键关闭窗口..." -ForegroundColor Gray
        Read-Host | Out-Null
    }
    exit $Code
}

# === 配置 ===
$ProjectPath = "CatClawMusic.Maui\CatClawMusic.Maui.csproj"
$Config = "Release"
$Tfm = "net11.0-windows10.0.19041.0"
$Rid = "win-x64"
$IssFile = "build-win-setup.iss"
$DotNetPath = "C:\Program Files\dotnet\dotnet.exe"

# 自动探测 ISCC.exe（Inno Setup 7 / 6 常见位置）
$IsccPath = ""
$candidates = @(
    "$env:USERPROFILE\InnoSetup\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 7\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
)
foreach ($c in $candidates) { if (Test-Path $c) { $IsccPath = $c; break } }

Write-Msg "=== 猫爪音乐 Windows Release 一键打包 ===" -Color Cyan
Write-Msg ""

if (-not (Test-Path $DotNetPath)) {
    Write-Msg "未找到 dotnet.exe: $DotNetPath" -Color Red
    Pause-And-Exit 1
}
if (-not $IsccPath) {
    Write-Msg "未找到 Inno Setup 的 ISCC.exe。" -Color Yellow
    Write-Msg "请先安装 Inno Setup 7（官网 https://jrsoftware.org/isdl.php），" -Color Yellow
    Write-Msg "装好后本脚本会自动识别。" -Color Yellow
    Pause-And-Exit 1
}

# 从 csproj 读取版本号
$csproj = Get-Content $ProjectPath -Raw -Encoding UTF8
$ver = [regex]::Match($csproj, '<ApplicationDisplayVersion>([^<]+)</ApplicationDisplayVersion>').Groups[1].Value
if (-not $ver) {
    Write-Msg "无法从 csproj 读取 ApplicationDisplayVersion" -Color Red
    Pause-And-Exit 1
}
Write-Msg "版本: $ver   目标: $Tfm ($Rid)" -Color Cyan

# [1/2] 发布绿色目录
Write-Msg ""
Write-Msg "[1/2] 发布 Windows 绿色目录（self-contained，无 pdb，多语言保留）..." -Color Yellow
foreach ($d in @("CatClawMusic.Maui\$OutDir", "CatClawMusic.Maui\$ObjDir")) {
    if (Test-Path $d) {
        try { Remove-Item $d -Recurse -Force -ErrorAction Stop }
        catch { Write-Msg "  警告: 清理 $d 失败（文件可能被 Visual Studio 占用），继续尝试..." -Color Yellow }
    }
}

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$exit = Invoke-Native -FilePath $DotNetPath -Arguments @(
    "publish", $ProjectPath, "-c", $Config, "-f", $Tfm,
    "-p:RuntimeIdentifierOverride=$Rid", "-p:SelfContained=true",
    "-p:IntermediateOutputPath=$ObjDir\", "-p:OutputPath=$OutDir\"
) -Label "dotnet publish"
if ($exit -ne 0) {
    Write-Msg "若提示文件被占用：请先关闭 Visual Studio 再重试。" -Color Yellow
    Pause-And-Exit $exit
}
$sw.Stop()
Write-Msg "  发布完成（$($sw.Elapsed.ToString('mm\:ss'))）" -Color Green

$publishDir = "CatClawMusic.Maui\$OutDir\publish"
if (-not (Test-Path "$publishDir\CatClawMusic.Maui.exe")) {
    Write-Msg "未找到发布产物: $publishDir\CatClawMusic.Maui.exe" -Color Red
    Pause-And-Exit 1
}

# [1.5/2] 生成 resources.pri（.NET 11 preview.7 下 MakePri 不会自动生成应用 PRI，
#         缺失会导致安装后加载旧 resources.pri 崩溃 0xC000027B）
Write-Msg ""
Write-Msg "[1.5/2] 生成 resources.pri（MakePri）..." -Color Yellow
$priconfig = "CatClawMusic.Maui\$ObjDir\priconfig.xml"
$makepri = ""
foreach ($v in @("10.0.22621.756", "10.0.22621.1")) {
    $cand = "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools\$v\bin\10.0.22621.0\x86\makepri.exe"
    if (Test-Path $cand) { $makepri = $cand; break }
}
if (-not $makepri -or -not (Test-Path $priconfig)) {
    Write-Msg "  警告: 未找到 makepri.exe 或 priconfig.xml，跳过 resources.pri 生成" -Color Yellow
} else {
    $projRoot = (Resolve-Path "CatClawMusic.Maui").Path
    $priExit = Invoke-Native -FilePath $makepri -Arguments @(
        "new", "/pr", $projRoot, "/cf", $priconfig, "/o", "/of", "$publishDir\resources.pri"
    )
    if ((Test-Path "$publishDir\resources.pri") -and $priExit -eq 0) {
        Write-Msg "  resources.pri 生成完成" -Color Green
    } else {
        Write-Msg "  警告: resources.pri 生成失败（退出码 $priExit）—— 安装后可能启动崩溃 0xC000027B" -Color Yellow
    }
}

# [2/2] 编译安装程序
Write-Msg ""
Write-Msg "[2/2] 编译安装程序（Inno Setup）..." -Color Yellow
$sw2 = [System.Diagnostics.Stopwatch]::StartNew()
$isccExit = Invoke-Native -FilePath $IsccPath -Arguments @(
    "/DMyAppVersion=$ver", "/DMyPublishDir=CatClawMusic.Maui\$OutDir\publish", $IssFile
) -Label "ISCC 编译"
if ($isccExit -ne 0) {
    Pause-And-Exit $isccExit
}
$sw2.Stop()
Write-Msg "  编译完成（$($sw2.Elapsed.ToString('mm\:ss'))）" -Color Green

# 结果
$setupExe = "release\windows\catclaw.music-$ver-Setup.exe"
if (Test-Path $setupExe) {
    $f = Get-Item $setupExe
    Write-Msg ""
    Write-Msg "=== 打包完成 ===" -Color Green
    Write-Msg "  安装包: $($f.FullName)"
    Write-Msg "  大小: $([math]::Round($f.Length / 1MB, 2)) MB"
    Write-Msg "  时间: $($f.LastWriteTime)"
} else {
    Write-Msg ""
    Write-Msg "=== 打包完成（未找到 $setupExe，请检查 release\windows 目录）===" -Color Yellow
}

Pause-And-Exit 0
