# 猫爪音乐 Android 原生版 —— 一键发布到 GitHub
#
# 用法:
#   .\release-android.ps1                 # 按 build.gradle.kts 里的版本号发布
#   .\release-android.ps1 -DryRun         # 只构建，不上传、不打 tag
#   .\release-android.ps1 -SkipBuild      # 复用已有 APK（APK 已构建过时）
#   .\release-android.ps1 -NoPause        # CI / 命令行调用
#
# 做的事（缺一样都会让"发布"变成半成品，所以按顺序做全）:
#   1. 从 CatClawMusic.Android/app/build.gradle.kts 读 versionCode / versionName
#   2. 校验前置条件: keystore 在、gh 已登录、版本号与 tag 未用过、工作区干净
#   3. 构建 arm64 单 ABI 的签名 release APK（双 ABI 包 34.9MB，单 ABI 20.9MB；
#      真机只用 arm64，x86_64 那份是给模拟器的，不该进发布资产）
#   4. 校验签名与包信息（apksigner / aapt2），不通过就中止
#   5. 提交版本号与 release notes（若尚未提交）、打 annotated tag、推 origin + gitee
#   6. gh release create 上传 APK，正文取 release-notes-<版本>.md
#
# 前置要求（脚本会检查并在不满足时报错）:
#   - gh CLI 已登录且对仓库有 repo 权限
#   - catclaw.keystore 在仓库根目录（见 SIGNING.md）
#   - 先写好 release-notes-<versionName>.md
#
# 注意: 脚本会临时把 ndk.abiFilters 改成只 arm64 来出单 ABI 包，
#       无论成功失败都会在 finally 里恢复原配置 —— 不要把那段恢复逻辑当成冗余删掉。

param(
    [switch]$DryRun,
    [switch]$SkipBuild,
    [switch]$NoPause
)

$ErrorActionPreference = "Continue"
Set-StrictMode -Version Latest

# ── 输出 ─────────────────────────────────────────────────────────────────
function Write-Msg {
    param([string]$Message = "", [ConsoleColor]$Color = [ConsoleColor]::Gray)
    Write-Host $Message -ForegroundColor $Color
}

function Fail {
    param([string]$Message)
    Write-Msg ""
    Write-Msg "✗ $Message" -Color Red
    if (-not $NoPause) { Write-Host "按 Enter 退出..." -ForegroundColor Gray; Read-Host | Out-Null }
    exit 1
}

function Pause-Done {
    if ($NoPause) { exit 0 }
    Write-Msg ""
    Write-Msg "按 Enter 退出..." -ForegroundColor Gray
    Read-Host | Out-Null
    exit 0
}

# ── 路径与常量 ───────────────────────────────────────────────────────────
$RepoRoot = $PSScriptRoot
$AndroidDir = Join-Path $RepoRoot "CatClawMusic.Android"
$BuildGradle = Join-Path $AndroidDir "app\build.gradle.kts"
$KeyStore = Join-Path $RepoRoot "catclaw.keystore"
$Gradle = Join-Path $AndroidDir "gradlew.bat"
$OutputDir = Join-Path $RepoRoot "publish-apk"

$KeyAlias = "catclaw"
$KeyPass = "catclaw123"
$StorePass = "catclaw123"

$env:JAVA_HOME = "C:\Program Files\Android\openjdk\jdk-21.0.8"
$AndroidSdk = "C:\Users\$env:USERNAME\AppData\Local\Android\Sdk"

Write-Msg "=== 猫爪音乐 Android 发布 ===" -Color Cyan
Write-Msg ""

# ── 1. 读版本号 ──────────────────────────────────────────────────────────
if (-not (Test-Path $BuildGradle)) { Fail "找不到 $BuildGradle" }
$gradleText = Get-Content $BuildGradle -Raw
$mCode = [regex]::Match($gradleText, 'versionCode\s*=\s*(\d+)')
$mName = [regex]::Match($gradleText, 'versionName\s*=\s*"([^"]+)"')
if (-not $mCode.Success -or -not $mName.Success) { Fail "无法从 build.gradle.kts 读出 versionCode / versionName" }

$VersionCode = [int]$mCode.Groups[1].Value
$VersionName = $mName.Groups[1].Value
$TagName = "v$VersionName"
$ReleaseNotes = Join-Path $RepoRoot "release-notes-$VersionName.md"

Write-Msg "版本: $VersionName (versionCode $VersionCode)    tag: $TagName"
Write-Msg ""

# ── 2. 前置条件 ──────────────────────────────────────────────────────────
Write-Msg "[1/6] 检查前置条件..." -Color Yellow

if (-not (Test-Path $KeyStore)) { Fail "找不到签名文件 catclaw.keystore（见 SIGNING.md）" }
if (-not (Test-Path $ReleaseNotes)) { Fail "找不到 release-notes-$VersionName.md —— 先写好发布说明再发" }

$ghCmd = Get-Command gh -ErrorAction SilentlyContinue
if (-not $ghCmd) { Fail "未安装 gh CLI（https://cli.github.com/）" }
$ghAuth = & gh auth status 2>&1
if ($LASTEXITCODE -ne 0) { Fail "gh 未登录，先跑 gh auth login`n$ghAuth" }

# tag 不能重复：重复说明这个版本已经发过，再发会覆盖资产
& git rev-parse -q --verify "refs/tags/$TagName" *> $null
if ($LASTEXITCODE -eq 0) { Fail "tag $TagName 已存在 —— 这个版本已经发布过。要重发请先删本地与远程 tag" }

# 已发布过的 release 也不能重建
$existing = & gh release view $TagName --json tagName 2>&1
if ($LASTEXITCODE -eq 0) { Fail "GitHub 上已有 $TagName 这个 release，不覆盖" }

# 工作区要干净，但**允许**版本号与本次 release notes 的改动 ——
# 脚本自己会提交它们（第 4 步）。除此之外的脏文件说明有别的工作没提交完，
# 那种情况下打出来的 tag 会混进无关代码，宁可中止。
$allowedDirty = @(
    "CatClawMusic.Android/app/build.gradle.kts",
    "release-notes-$VersionName.md",
    # 脚本自身（首次引入时是 untracked），不属于发布内容
    "release-android.ps1"
)
$dirty = & git status --porcelain | Where-Object {
    $line = $_.Substring(3).Trim().Trim('"')
    $allowedDirty -notcontains $line
}
if ($dirty) {
    Fail "工作区有未提交的改动，先提交或清理再发布：`n$($dirty -join "`n")"
}

Write-Msg "  签名文件、release notes、gh 登录、tag 可用性、工作区 均正常" -Color Green

# ── 3. 构建 arm64 单 ABI 签名包 ──────────────────────────────────────────
Write-Msg ""
Write-Msg "[2/6] 构建 arm64 签名 release APK..." -Color Yellow

$apkPath = Join-Path $AndroidDir "app\build\outputs\apk\release\app-release.apk"

if (-not $SkipBuild) {
    # 单 ABI：临时收窄 abiFilters。真机只用 arm64；带上 x86_64 会让包从 20.9MB 涨到 34.9MB。
    $origText = Get-Content $BuildGradle -Raw
    $armOnlyText = $origText -replace 'abiFilters \+= setOf\("arm64-v8a", "x86_64"\)', 'abiFilters += setOf("arm64-v8a")'
    if ($armOnlyText -eq $origText) {
        Fail "没能在 build.gradle.kts 里找到 abiFilters 那行 —— 它的写法变了，请同步更新本脚本的替换逻辑"
    }

    try {
        Set-Content -Path $BuildGradle -Value $armOnlyText -NoNewline
        Write-Msg "  已临时收窄为 arm64 单 ABI"

        Push-Location $AndroidDir
        try {
            & $Gradle :app:assembleRelease --no-build-cache `
                "-Pandroid.injected.signing.store.file=$KeyStore" `
                "-Pandroid.injected.signing.store.password=$StorePass" `
                "-Pandroid.injected.signing.key.alias=$KeyAlias" `
                "-Pandroid.injected.signing.key.password=$KeyPass" 2>&1 |
                Select-String -Pattern 'BUILD SUCCESS|BUILD FAILED|e: file' | ForEach-Object { Write-Msg "  $($_.Line)" }
            $buildExit = $LASTEXITCODE
        } finally {
            Pop-Location
        }
        if ($buildExit -ne 0) { Fail "Gradle 构建失败（退出码 $buildExit）" }
    } finally {
        # 一定要恢复：否则仓库里就留下一个"只打 arm64"的改动
        Set-Content -Path $BuildGradle -Value $origText -NoNewline
        Write-Msg "  已恢复 build.gradle.kts 的双 ABI 配置" -Color DarkGray
    }
} else {
    Write-Msg "  跳过构建（-SkipBuild），复用现有 APK" -Color DarkGray
    if (-not (Test-Path $apkPath)) { Fail "没有可复用的 APK：$apkPath" }
}

if (-not (Test-Path $apkPath)) { Fail "构建完成但找不到 APK：$apkPath" }
$apkSizeMB = [math]::Round((Get-Item $apkPath).Length / 1MB, 2)
Write-Msg "  APK: $apkSizeMB MB" -Color Green

# ── 4. 校验签名与包信息 ──────────────────────────────────────────────────
Write-Msg ""
Write-Msg "[3/6] 校验 APK..." -Color Yellow

$buildTools = Get-ChildItem "$AndroidSdk\build-tools" -Directory -ErrorAction SilentlyContinue |
    Sort-Object Name -Descending | Select-Object -First 1
if ($buildTools) {
    $apksigner = Join-Path $buildTools.FullName "apksigner.bat"
    if (Test-Path $apksigner) {
        $cert = & $apksigner verify --print-certs $apkPath 2>&1
        if ($LASTEXITCODE -ne 0) { Fail "APK 签名校验失败：`n$($cert -join "`n")" }
        $dn = ($cert | Select-String -Pattern 'certificate DN:' | Select-Object -First 1).Line
        Write-Msg "  $($dn -replace '.*certificate DN:\s*','签名: ')" -Color Green
    }

    $aapt2 = Join-Path $buildTools.FullName "aapt2.exe"
    if (Test-Path $aapt2) {
        $badging = & $aapt2 dump badging $apkPath 2>&1
        $pkgLine = ($badging | Select-String -Pattern "^package: name=" | Select-Object -First 1).Line
        if ($pkgLine -notmatch "versionCode='$VersionCode'") { Fail "APK 的 versionCode 与 build.gradle.kts 不一致：$pkgLine" }
        if ($pkgLine -notmatch "versionName='$([regex]::Escape($VersionName))'") { Fail "APK 的 versionName 与 build.gradle.kts 不一致：$pkgLine" }
        Write-Msg "  包名/版本号一致（versionCode $VersionCode / $VersionName）" -Color Green
    }
} else {
    Write-Msg "  未找到 build-tools，跳过签名与包信息校验" -Color Yellow
}

# ── 5. 整理交付目录 ──────────────────────────────────────────────────────
$releaseDir = Join-Path $OutputDir $VersionName
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
$assetName = "catclaw.music-$VersionName-arm64.apk"
$assetPath = Join-Path $releaseDir $assetName
Copy-Item $apkPath $assetPath -Force
Copy-Item $ReleaseNotes (Join-Path $releaseDir "release-notes-$VersionName.md") -Force
$sha = (Get-FileHash $assetPath -Algorithm SHA256).Hash
Write-Msg ""
Write-Msg "  交付目录: $releaseDir"
Write-Msg "  SHA-256 : $sha"

if ($DryRun) {
    Write-Msg ""
    Write-Msg "=== -DryRun：构建与校验完成，未提交 / 未打 tag / 未上传 ===" -Color Yellow
    Pause-Done
}

# ── 6. 提交版本信息、打 tag、推送 ────────────────────────────────────────
Write-Msg ""
Write-Msg "[4/6] 提交版本号与 release notes..." -Color Yellow

Push-Location $RepoRoot
try {
    & git add $BuildGradle $ReleaseNotes
    & git commit -m "chore(android): 版本号 $VersionName (versionCode $VersionCode)" *> $null
    if ($LASTEXITCODE -ne 0) { Fail "提交失败（可能没有需要提交的改动）" }
    Write-Msg "  已提交" -Color Green

    Write-Msg ""
    Write-Msg "[5/6] 打 tag 并推送..." -Color Yellow
    & git tag -a $TagName -m "CatClawMusic $VersionName"
    if ($LASTEXITCODE -ne 0) { Fail "创建 tag 失败" }

    foreach ($remote in @("origin", "gitee")) {
        & git remote get-url $remote *> $null
        if ($LASTEXITCODE -ne 0) { Write-Msg "  跳过 $remote（未配置）" -Color DarkGray; continue }
        & git push $remote master --follow-tags 2>&1 |
            Select-String -Pattern 'master ->|new tag|rejected|error' | ForEach-Object { Write-Msg "  [$remote] $($_.Line.Trim())" }
        if ($LASTEXITCODE -ne 0) { Fail "推送到 $remote 失败" }
    }
    Write-Msg "  已推送" -Color Green

    # ── 7. 上传 GitHub Release ───────────────────────────────────────────
    Write-Msg ""
    Write-Msg "[6/6] 上传到 GitHub Release..." -Color Yellow
    & gh release create $TagName $assetPath `
        --title "CatClawMusic $VersionName" `
        --notes-file $ReleaseNotes 2>&1 | ForEach-Object { Write-Msg "  $_" }
    if ($LASTEXITCODE -ne 0) { Fail "gh release create 失败" }
} finally {
    Pop-Location
}

Write-Msg ""
Write-Msg "=== 发布完成 ===" -Color Green
Write-Msg "  版本: $VersionName (versionCode $VersionCode)"
Write-Msg "  APK : $assetName  $apkSizeMB MB"
Write-Msg "  SHA : $sha"
Write-Msg "  地址: $(gh release view $TagName --json url -q .url 2>$null)"

Pause-Done
