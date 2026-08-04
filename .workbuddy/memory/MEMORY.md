# CatClawMusic 项目记忆

## 构建与发布
- 主线: `CatClawMusic.Maui/CatClawMusic.Maui.csproj` (v1.6.5+, net10.0-android, UseMaui 单项目)。
- Release 签名命令见 `2026-07-20.md` 日志; keystore = 解决方案根 `catclaw.keystore`(alias=catclaw, pass=catclaw123, SHA1 9F:D0:61:3A:7C:76:1E:A8:5A:48:89:4F:4D:35:66:65:8A:89:24:69)。⚠️ 签名必须 `<AndroidKeyStore>true</AndroidKeyStore>` + PropertyGroup 写法, 否则静默回退 debug 密钥致旧用户无法覆盖装。密码经环境变量 `CatClawKeyPass`/`CatClawStorePass` 注入(csproj 用 `$(CatClawKeyPass)`/`$(CatClawStorePass)`)。
- 产出 `bin/Release/net10.0-android/publish/*-Signed.apk`(csproj 无自动复制 target)。
- **构建坑(2026-08-01, 详见当日日志)**: ① `global.json` 原 `rollForward:"latestMajor"` 会滚到 net11 预览 SDK 致 XA0111(aapt2 版本不受支持)→ 已改 `"latestFeature"` 锁定 .NET 10; ② Android 包 36.1.69 的 aapt2 daemon 多实例并行会随机崩(APT2098「failed to open file」, 每次不同文件)→ 必须加 `-p:Aapt2DaemonMaxInstanceCount=0 -m:1`。完整命令:
  `CatClawKeyPass=catclaw123 CatClawStorePass=catclaw123 dotnet publish CatClawMusic.Maui/CatClawMusic.Maui.csproj -c Release -f net10.0-android -p:Aapt2DaemonMaxInstanceCount=0 -m:1`
- **文件锁真凶(2026-08-03)**: 用户开着 Visual Studio 时，VS(如 PID 22180) 持续锁定 `bin\*.pdb`/obj 下文件 → CLI 构建报 MSB3021/3027(pdb 复制失败)或 XAAMP7019/RemoveDirFixed 系列。绕法（无需关 VS）：构建加 `-p:IntermediateOutputPath=obj/dbg2/ -p:OutputPath=bin/dbg2/` 走全新目录；publish Release 前建议让用户关 VS 再打正式包。
- apksigner 验证: `/c/Users/lvjin/AppData/Local/Android/Sdk/build-tools/34.0.0/apksigner.bat verify --print-certs <apk>`(Git Bash 需 `MSYS_NO_PATHCONV=1` + Windows 路径)。jarsigner 报「没有清单」是正常的(.NET Android 用 v2/v3 签名, 非 v1 JAR)。
- **Windows 发布(2026-08-04)**: 多目标项目禁止 `-r win-x64`(Android TFM 也要解析 Mono win-x64 → NU1102), 必须 `-p:RuntimeIdentifierOverride=win-x64`。`App.generated.cs`(手写, 绕 WASDK1.7 XamlCompiler 退出码) 与自动 `App.g.i.cs` 互补: csproj Windows TFM 定义 `DISABLE_XAML_GENERATED_MAIN`, 手写 Program 用 `#if DISABLE_XAML_GENERATED_MAIN`(反向), 手写不定义 App。MSIX 打包: 手写 `Platforms/Windows/Package.appxmanifest` 作模板(MAUI 自动生成最终清单, 勿手动加 AppxManifest item → MSB4094) + `catclaw-win.pfx`(openssl 带 codeSigning EKU + `certutil -user -importPFX`, thumbprint 见 `catclaw-win.thumbprint.txt`) + `-p:WindowsPackageType=MSIX -p:AppxPackage=true -p:AppxPackageSigningEnabled=true -p:PackageCertificateKeyFile=... -p:PackageCertificateThumbprint=... -p:PackageCertificatePassword=catclaw123`。完整命令见 `2026-08-04.md`。Windows Release 已去 pdb/xml(DebugType=None)。
- **Windows 本地化资源策略(2026-08-05 变更)**: 旧约束「禁止 SatelliteResourceLanguages 裁剪 / 保留全语言」已**作废**。现经 `SatelliteResourceLanguages=zh;zh-CN;zh-Hans;zh-Hant;en;en-US` 主动裁剪(应用本身只发 zh-CN+en-US 两语言)。配套(方案B已落地): csproj `RedirectDotNetSatelliteToLocales`(`.resources.dll` 复制阶段重定向到 `locales\`)+ `App.xaml.cs` 运行时 `AssemblyLoadContext.Resolving` 从 `locales\{culture}\` 加载(root `{culture}\` 兜底)。**已删除**原 `MoveCultureDirsToLocales` PowerShell 兜底(复制阶段重定向已覆盖, 冗余且易碎)。WinUI 框架 130+ 个 `.mui` 文化目录**不做裁剪**(用户拍板: 安装后用户不看安装目录)。图片资源经 `RedirectMauiImagesToResources` 输出到 `resources\` 子目录, 由 `WindowsResourceFileImageSourceService` 统一从 `resources\` 解析。⚠️ `build-win-release.ps1` 曾有一段发布后删除"非保留名单目录"的逻辑, 会误删 `locales\`/`resources\`/`Assets\`/`Platforms\`, 已于 2026-08-05 退回 HEAD。

## Android 16 / 16KB 页对齐 (2026-07-20)
- 三星 Android16 闪退根因 = `libe_sqlite3.so`(SQLitePCLRaw 2.1.2) 4KB 对齐。修复: 三 csproj 顶层 `<PackageReference Include="SQLitePCLRaw.bundle_green" Version="2.1.11" />`(Maui 另加 `SQLitePCLRaw.lib.e_sqlite3.android` 2.1.11)。⚠️ 必须 2.1.11(2.1.10 仅消警告, 16KB 设备 DB 仍打不开)。
- 遗留: `Assets/ffmpeg/arm64-v8a/libffmpeg.so` 仍 4KB 对齐 → 仅 16KB 设备转码失败不崩。

## MAUI 已知坑
- Android `LayerType` 遮蔽: `Platforms/Android` 下 `using View = Android.Views.View;` 且子类体内用简单名。
- WinUI: 纯图标按钮用 `<ImageButton>`; 未打包 Windows `Preferences` 静默失效→已迁文件存储(`CustomFolderStore` 等)。
- `ForceLayout()` 已移除→用 `InvalidateMeasure()`/`InvalidateLayout()`。
- 顶部导航栏 `Shell.NavBarIsVisible=False`; 桌面 `#if WINDOWS` 用 DesktopMainPage。
- **ViewPager2 架构(2026-07-19)**: Android 用原生 `ViewPager2` 承载 5 个 MAUI 页(`Controls/NativeTabPager.cs` + `Platforms/Android/NativeTabPagerHandler.cs` + `MauiPagerAdapter`/`PageChangeCallback`, OffscreenPageLimit=全部常驻); Windows 保留 `TranslationX`+懒加载。`MainPage.xaml.cs` `SetupPages` 双端分支, `SwitchToVpIndex` 统一切页, `OnNativeScrollStateChanged` 在 Dragging/Settling 暂停 FrostedBackground。⚠️ net10 抽搐根因是 net10 渲染差异, 勿建议回 net11(本环境 net11 不可编译/调试)。`CollapseNowPlaying` Android 简化为切发现页。
- **SafeArea**: 非全屏 tab 页挂 `SafeAreaPaddingBehavior`(基类 `Behavior<Layout>`, Padding 在 Layout/ScrollView 上); LibraryPage 由 ScrollView 包 Grid; 全屏页(歌词/播放页)不挂。

## 启动性能(GC 风暴 / 主线程卡顿)
- 现象:启动即大量 `Explicit concurrent copying GC`,backtrace 指向主线程 `Bitmap.compress`(MAUI 默认 ImageCache 写 PNG 到 `cache/images`),MIUI 报 `APP_SCOUT_SLOW/WARNING`(主线程 wall 2.5-3.8s)。堆不涨→非泄漏,高分配率。
- 根因:5 个 tab 页 `ViewPager2.OffscreenPageLimit=Pages.Count(=5)` 全部常驻 + `MainPage.OnAppearing` 首屏 `PreloadTabDataAsync()` 全量预加载(本地歌/歌单/发现),几百封面同时解码;自定义 `Caching*ImageSourceService` 虽 `Task.Run` 后台 decode+降采样,但 `ConfigureAwait(true)` 使完成回调集中回主线程 `SetImageDrawable`。详见 `2026-07-21.md`。
- 修复:OffscreenPageLimit→1~2 / 预加载按需分页 / `ConfigureAwait(false)`+`imageView.Post` / `SemaphoreSlim` 限流 / 关 MAUI 默认 ImageCache 写盘。

## 主题与背景图(2026-07-20)
- 5 套主题(橙FF7043/粉EC407A/紫9B7ED8/蓝42A5F5/青26A69A)。枚举 `AppTheme` 删5色留值兼容旧偏好; ThemeMap/ViewModel/Settings页均改5套。
- 背景图: csproj 加 `<MauiImage Include="Resources\Images\Backgrounds\*" />`; `ThemeService.ApplyThemeBackgroundImage` 必须存**纯字符串**(经 ImageSourceConverter 查 MauiImage 注册表), 不能 `ImageSource.FromFile`。5 个 tab 页根 BackgroundColor=Transparent 透出 MainPage 背景图+遮罩。
- 底部 TabBar 毛玻璃: `MainPage.xaml` 插 `controls:FrostedBackground`(Source=ThemeBackgroundImage, TintColor=TabBarGlassTint, TintOpacity=0.42); 需 `xmlns:controls`。

## 遮罩/模糊
- 播放列表弹窗 `AppPopup`(Android `RenderEffect.CreateBlurEffect(24,24)` 模糊背后兄弟视图 + MaskLayer #99000000)。
- 设置面板 `SearchPage` 仿 AppPopup 加同款 Android 模糊(`ApplyBlurToSettingsSiblings`/`RemoveBlurFromSettingsSiblings`)。

## 播放页控件尺寸(2026-07-20)
- NowPlayingPage.xaml: 上一/下一曲 40→52, 播放键 56→73, 底部6键 44→22, 控件区高 56→80。

## 歌词 / 艺术家元数据 / 猫爪圈 / README / 设计流程
- 歌词: LRC + TTML, `LyricsService`/`MusicUtility`/`ILyricsService`。
- 艺术家抓取设施已建未接通(`SearchArtistsAsync` 无调用, `ArtistMatchPage` 空壳)。
- 猫爪圈 P2P: UDP 37821 / TCP 37822 / HTTP 37823 / STUN 37824。
- README 必须 GitHub 友好纯 markdown, 禁更新日志。
- UI 改动先出 HTML 原型; 品牌深空蓝玻璃拟态 #8C7BFF/#55D6FF/#080B1A。

## 横屏/竖屏方向控制 (2026-07-27)
- 状态机：`App.xaml.cs` 维护 `_manualLandscape` + `_manualPortrait` 两个互斥手动标志。
  - `IsLandscapeMode()`：强制横屏→true，强制竖屏→false，否则按 `DeviceDisplay.Current.MainDisplayInfo.Orientation`。
  - 播放页/桌面页旋转按钮调用 `App.ToggleManualLandscape()`：当前横屏→强制竖屏(`SensorPortrait`+切`MainPage`)，否则→强制横屏(`SensorLandscape`+切`DesktopMainPage`)。
  - `OnDisplayOrientationChanged`：仅在设备**实际到达**强制方向后释放对应标志（Landscape→释放`_manualLandscape`；Portrait→释放`_manualPortrait`），之后恢复跟随物理方向/系统自动旋转。
  - `DesktopMainPage.OnBackButtonPressed` 调用 `ReleaseManualLandscape()` 即强制回竖屏。
- 横屏布局实现：`CreateWindow` / `ApplyOrientationLayout` 用 `shell.Items.Clear()+Add(ShellContent)` 直接换根；横屏根为 `DesktopMainPage`，竖屏根为 `MainPage`。

## 播放页封面高分辨率解码
- Android 自定义 `CachingFileImageSourceService` 按 `ImageView.Tag` 识别播放页封面，命中 `PlayerCoverTag` 时按 1024px 解码，否则按视图尺寸桶解码。
- `NowPlayingPage` 在 `OnAppearing`/HandlerChanged 给 `ArtworkImage`/`LandscapeCoverImage` 打 Tag；并在 `ApplyLayoutForOrientation` 横竖屏切换后调用 `ReloadCoverHighResAsync` 重载封面，避免隐藏态首次显示时错过 Tag 导致模糊。

## 进度定时器与进度条冻结 (2026-07-30)
- 现象：音频正常播放，但播放页进度条/当前时间/歌词着色冻结；切歌或恢复后可能短暂恢复。
- 根因：`AudioPlayerService` 的 `System.Threading.Timer` 回调在 ThreadPool 线程触发，原实现用构造期捕获的 `SynchronizationContext.Current`（`_mainContext?.Post`）把 tick 派回主线程读取 `CurrentPosition`。MAUI 在部分启动路径下构造时主线程同步上下文尚未就绪 → `_mainContext` 为 null → 每次 tick 被静默丢弃 → `PositionChanged` 永不触发。
- 修复：`Services/AudioPlayerService.cs` 改用 `Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread` 派发 tick；移除 `_mainContext`/`_tickAction` 字段与构造函数捕获。DEBUG 下保留 `StartPositionTimer` 与前 3 次 tick 的诊断日志。
- 注意：`OnPlayerError` 退避分支**不能**调用 `StopPositionTimer()`，否则 ExoPlayer 从可恢复错误恢复后无人重启定时器，进度条同样冻结。

## 环境
- SteamTools 中间人证书: 合并 CA 包 `C:\Users\Administrator\git-ca-steamtools.crt`(git 全局 sslCAInfo, 勿删); git 用 openssl 后端。
