# 猫爪音乐 Windows Release 一键打包脚本
# 用法: 双击运行（打包完会暂停等待按键，方便看结果），或 .\build-win-release.ps1
# 输出: release\windows\catclaw.music-<版本>-Setup.exe
# 流程: ① dotnet publish 绿色目录(self-contained, 去pdb, 多语言保留) -> ② ISCC 编译 Inno Setup 安装程序
# 依赖: .NET SDK(已装) + Inno Setup 7(ISCC.exe, 未装时脚本会提示)
# 版本号自动从 csproj 读取 ApplicationDisplayVersion，以后发版只需改 csproj
# 注意: 本文件必须为 UTF-8 with BOM + CRLF（Windows PowerShell 5.1 按 ANSI 解码，无 BOM 中文会乱码）

param(
    [switch]$NoPause,   # 静默模式：不等待按键（CI/命令行用）
    [string]$OutDir = "bin\winpub3",   # 输出目录（相对 Maui 项目目录，可自定义）
    [string]$ObjDir = "obj\winpub3"    # 中间目录（相对 Maui 项目目录，可自定义）
)

$ErrorActionPreference = "Stop"

function Pause-And-Exit {
    param([int]$Code = 0)
    Write-Host ""
    if ($Code -eq 0) { Write-Host "打包流程结束。" -ForegroundColor Green }
    else { Write-Host "打包流程异常终止（退出码 $Code）。" -ForegroundColor Red }
    if (-not $NoPause) {
        Write-Host "按 Enter 键关闭窗口..." -ForegroundColor Gray
        Read-Host | Out-Null
    }
    exit $Code
}

# === 配置 ===
$ProjectPath = "CatClawMusic.Maui\CatClawMusic.Maui.csproj"
$Config = "Release"
$Tfm = "net10.0-windows10.0.19041.0"
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

Write-Host "=== 猫爪音乐 Windows Release 一键打包 ===" -ForegroundColor Cyan
Write-Host ""

if (-not (Test-Path $DotNetPath)) {
    Write-Host "未找到 dotnet.exe: $DotNetPath" -ForegroundColor Red
    Pause-And-Exit 1
}
if (-not $IsccPath) {
    Write-Host "未找到 Inno Setup 的 ISCC.exe。" -ForegroundColor Yellow
    Write-Host "请先安装 Inno Setup 7（官网 https://jrsoftware.org/isdl.php），" -ForegroundColor Yellow
    Write-Host "装好后本脚本会自动识别。" -ForegroundColor Yellow
    Pause-And-Exit 1
}

# 从 csproj 读取版本号
$csproj = Get-Content $ProjectPath -Raw -Encoding UTF8
$ver = [regex]::Match($csproj, '<ApplicationDisplayVersion>([^<]+)</ApplicationDisplayVersion>').Groups[1].Value
if (-not $ver) {
    Write-Host "无法从 csproj 读取 ApplicationDisplayVersion" -ForegroundColor Red
    Pause-And-Exit 1
}
Write-Host "版本: $ver   目标: $Tfm ($Rid)" -ForegroundColor Cyan

# [1/2] 发布绿色目录
Write-Host ""
Write-Host "[1/2] 发布 Windows 绿色目录（self-contained，无 pdb，多语言保留）..." -ForegroundColor Yellow
foreach ($d in @("CatClawMusic.Maui\$OutDir", "CatClawMusic.Maui\$ObjDir")) {
    if (Test-Path $d) {
        try { Remove-Item $d -Recurse -Force -ErrorAction Stop }
        catch { Write-Host "  警告: 清理 $d 失败（文件可能被 Visual Studio 占用），继续尝试..." -ForegroundColor Yellow }
    }
}

$sw = [System.Diagnostics.Stopwatch]::StartNew()
& $DotNetPath publish $ProjectPath -c $Config -f $Tfm `
    -p:RuntimeIdentifierOverride=$Rid -p:SelfContained=true `
    -p:IntermediateOutputPath=$ObjDir\ -p:OutputPath=$OutDir\
if ($LASTEXITCODE -ne 0) {
    Write-Host "dotnet publish 失败（退出码 $LASTEXITCODE）。" -ForegroundColor Red
    Write-Host "若提示文件被占用：请先关闭 Visual Studio 再重试。" -ForegroundColor Yellow
    Pause-And-Exit $LASTEXITCODE
}
$sw.Stop()
Write-Host "  发布完成（$($sw.Elapsed.ToString('mm\:ss'))）" -ForegroundColor Green

$publishDir = "CatClawMusic.Maui\$OutDir\publish"
if (-not (Test-Path "$publishDir\CatClawMusic.Maui.exe")) {
    Write-Host "未找到发布产物: $publishDir\CatClawMusic.Maui.exe" -ForegroundColor Red
    Pause-And-Exit 1
}

# [2/2] 编译安装程序
Write-Host ""
Write-Host "[2/2] 编译安装程序（Inno Setup）..." -ForegroundColor Yellow
$sw2 = [System.Diagnostics.Stopwatch]::StartNew()
& $IsccPath "/DMyAppVersion=$ver" "/DMyPublishDir=CatClawMusic.Maui\$OutDir\publish" $IssFile
if ($LASTEXITCODE -ne 0) {
    Write-Host "ISCC 编译失败（退出码 $LASTEXITCODE）。" -ForegroundColor Red
    Pause-And-Exit $LASTEXITCODE
}
$sw2.Stop()
Write-Host "  编译完成（$($sw2.Elapsed.ToString('mm\:ss'))）" -ForegroundColor Green

# 结果
$setupExe = "release\windows\catclaw.music-$ver-Setup.exe"
if (Test-Path $setupExe) {
    $f = Get-Item $setupExe
    Write-Host ""
    Write-Host "=== 打包完成 ===" -ForegroundColor Green
    Write-Host "  安装包: $($f.FullName)"
    Write-Host "  大小: $([math]::Round($f.Length / 1MB, 2)) MB"
    Write-Host "  时间: $($f.LastWriteTime)"
} else {
    Write-Host ""
    Write-Host "=== 打包完成（未找到 $setupExe，请检查 release\windows 目录）===" -ForegroundColor Yellow
}

Pause-And-Exit 0
