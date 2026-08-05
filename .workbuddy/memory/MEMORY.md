# CatClawMusic 项目记忆

## 构建与发布
- 主线 csproj: `CatClawMusic.Maui/CatClawMusic.Maui.csproj` (net10.0-android, UseMaui 单项目)。
- Android 签名: keystore=`catclaw.keystore`(alias=catclaw, pass=catclaw123, SHA1 9F:D0:61:3A:7C:76:1E:A8:5A:48:89:4F:4D:35:66:65:8A:89:24:69)。⚠️ 必须 `<AndroidKeyStore>true</AndroidKeyStore>`+PropertyGroup 否则静默回退 debug 密钥。密码经 `CatClawKeyPass`/`CatClawStorePass` 注入。
- Android Release(`global.json` 必须 `rollForward:"latestFeature"` 防滚 net11→XA0111; aapt2 daemon 多实例随机崩→`-p:Aapt2DaemonMaxInstanceCount=0 -m:1`)。完整命令见 `2026-08-01.md`。
- 文件锁: VS 开着锁 bin/obj→CLI 报 MSB3021/3027。绕法: 构建加 `-p:IntermediateOutputPath=obj/dbg2/ -p:OutputPath=bin/dbg2/`; 正式包前关 VS。
- apksigner 验证: `.../build-tools/34.0.0/apksigner.bat verify --print-certs <apk>`(Git Bash 需 `MSYS_NO_PATHCONV=1`)。
- Windows 发布: 多目标禁 `-r win-x64`→用 `-p:RuntimeIdentifierOverride=win-x64`。MSIX: `Platforms/Windows/Package.appxmanifest` 模板 + `catclaw-win.pfx` + 一组 AppxPackage* 属性(见 `2026-08-04.md`)。
- Windows 本地化: `SatelliteResourceLanguages=zh;zh-CN;zh-Hans;zh-Hant;en;en-US` 裁剪; csproj `RedirectDotNetSatelliteToLocales` + `App.xaml.cs` `AssemblyLoadContext.Resolving` 从 `locales\{culture}\` 加载。图片经 `RedirectMauiImagesToResources`→`resources\`, 由 `WindowsResourceFileImageSourceService` 解析。

## Android 16 / 16KB 页对齐
- 三星 Android16 闪退=`libe_sqlite3.so`(SQLitePCLRaw 2.1.2)4KB对齐。修复: 三 csproj 加 `SQLitePCLRaw.bundle_green` 2.1.11(+Maui `SQLitePCLRaw.lib.e_sqlite3.android` 2.1.11)。⚠️ 必须 2.1.11(2.1.10 仅消警告)。
- 遗留: `Assets/ffmpeg/arm64-v8a/libffmpeg.so` 仍 4KB→仅 16KB 设备转码失败不崩。

## MAUI 已知坑
- Android `LayerType`: `Platforms/Android` 下 `using View = Android.Views.View;`。
- WinUI: 纯图标按钮用 `<ImageButton>`; 未打包 Windows `Preferences` 静默失效→文件存储。
- `ForceLayout()` 已移除→`InvalidateMeasure()`/`InvalidateLayout()`。
- `Shell.NavBarIsVisible=False`; 桌面 `#if WINDOWS` 用 DesktopMainPage。
- ViewPager2 架构: Android 原生 ViewPager2 承 5 页(OffscreenPageLimit 全常驻), Windows 用 TranslationX+懒加载。⚠️ net10 抽搐是渲染差异, 勿回 net11。
- SafeArea: 非全屏 tab 挂 `SafeAreaPaddingBehavior`; 全屏页(歌词/播放)不挂。
- ⚠️ **DI 静默 null**: 只注册了 `IThemeService` 时 `GetService<ThemeService>()`(具体类型) 返回 null, 配合 `?? true` 兜底会变成"永远走默认分支"的隐形 bug。兜底值必须中性/可推导。
- Windows 窗口 chrome: `HandlerChanged` 用 `ResolveIsDark(IThemeService?)` + 一次性 `Activated` 补刷(HandlerChanged 时 WinUI `Content` 根面板未必已建好)。`Activated` 委托类型是 `Windows.Foundation.TypedEventHandler<object, Microsoft.UI.Xaml.WindowActivatedEventArgs>`, 无 `WindowActivatedEventHandler`。DWM COLORREF 是 `0x00BBGGRR`(#F8F7FF→`0x00FFF7F8`)。`UpdateWindowsTheme` 中 `DWMWA_BORDER_COLOR`/`DWMWA_CAPTION_COLOR` 必须设 **CLR_NONE(0xFFFFFFFF)** 让 MAUI 内容贴边(深浅色都设, 否则外圈出现白/深边)。`ThemeService.UpdatePlatformStatusBar(bool isDark)` 由 `ApplyTheme` 传已算好值, 勿内部重算 `RequestedTheme`。
- MAUI `MauiImage` 资源查找对扩展名敏感: SVG 可省后缀, PNG/JPG 必须带 `.png`/`.jpg`, 否则易静默 fallback 成空(如 About 页头像漏 `.png`)。

## 启动性能(GC 风暴)
- 根因: ViewPager2 OffscreenPageLimit=5 全常驻 + 首屏全量预加载 + `ConfigureAwait(true)` 集中回主线程解码。修复: OffscreenPageLimit→1~2 / 按需分页 / `ConfigureAwait(false)`+`imageView.Post` / `SemaphoreSlim` 限流 / 关 MAUI ImageCache 写盘。

## 主题与背景
- 5 套主题(橙FF7043/粉EC407A/紫9B7ED8/蓝42A5F5/青26A69A); 背景图存纯字符串(非 `ImageSource.FromFile`); tab 页根 BackgroundColor=Transparent 透出; 底部 TabBar 毛玻璃 `controls:FrostedBackground`(TintOpacity=0.42)。

## 遮罩/模糊
- `AppPopup`(Android `RenderEffect.CreateBlurEffect(24,24)`+MaskLayer #99000000); 设置 `SearchPage` 同款。

## 功能设施
- 歌词 LRC+TTML; 艺术家抓取未接通(`SearchArtistsAsync` 无调用); 猫爪圈 P2P UDP37821/TCP37822/HTTP37823/STUN37824; README 纯 markdown 禁更新日志; UI 改动先出 HTML 原型(品牌深空蓝 #8C7BFF/#55D6FF/#080B1A)。

## 横屏/竖屏
- `App.xaml.cs` `_manualLandscape`+`_manualPortrait` 互斥; `ToggleManualLandscape()` 切 `MainPage`/`DesktopMainPage`; `OnDisplayOrientationChanged` 实际到达方向后释放标志。

## 播放页
- 封面校验(`CoverHelper.IsValidImageFile`)只判文件头 magic+尺寸，**勿苛求尾字节**(JPEG FFD9/PNG IEND/GIF 3B)——真实封面常带填充字节/省略 EOI，严格尾校验会误杀→播放页回退默认封面，发现页(直绑 `CoverArtPath`)却正常(2026-08-06)。发现页与播放页封面是两条链路。
- Android `CachingFileImageSourceService` 按 `PlayerCoverTag` 1024px 解码封面; 进度条冻结根因=`AudioPlayerService` 原用构造期 `_mainContext`(可为 null 致 tick 静默丢弃)→改 `MainThread.BeginInvokeOnMainThread` 派发; `OnPlayerError` 退避不得调 `StopPositionTimer()`。
- Windows 雾面动态背景(`FrostedBackgroundHandler`)：CPU 盒式模糊(两次盒式逼近高斯)+AdjustTone(饱和度1.6/亮度1.12)，分辨率 512。**勿用 Win2D 像素回读管线**(SoftwareBitmap→CanvasBitmap→GaussianBlur→GetPixelBytes)：本环境产出黑/无效位图(不随封面变色)且无法无 GUI 调试。旋转必须**有界振荡**(±9~15°)且基础缩放 1.72，否则长会话累加转角越界后角上漏深底色 `#0B0D20`。**动画门控忽略 `IsActive`(绑 IsPlaying)——PC 端有源即常驻漂移动画**，仅用户滑动列表时暂停。主开关=`IsVisible`(FrostedBackgroundEnabled)。`WindowsStage` 内层渐变已改透明，把雾面背景透出。

## 环境
- SteamTools 中间人证书 `C:\Users\Administrator\git-ca-steamtools.crt`(git 全局 sslCAInfo, 勿删)。
