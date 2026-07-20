# CatClawMusic 项目记忆

## 构建与发布
- 主线入口: `CatClawMusic.Maui/CatClawMusic.Maui.csproj` (v1.6.5 / build 42, UseMaui, 单项目)。旧 `CatClawMusic.UI.csproj`(Xamarin) 已非主线。
- TargetFramework: `net10.0-android` (TargetAndroidApi=36；本机 SDK 另装 35/36)。
- 默认出 **Release 签名包**: `dotnet publish CatClawMusic.Maui/CatClawMusic.Maui.csproj -c Release -f net10.0-android -p:AndroidSdkDirectory="C:/Users/Administrator/AppData/Local/Android/Sdk" -p:JavaSdkDirectory="C:/Program Files/Microsoft/jdk-21.0.11.10-hotspot/" -p:CatClawStorePass=catclaw123 -p:CatClawKeyPass=catclaw123`
- Debug 仅快速验证编译: `dotnet build CatClawMusic.Maui/CatClawMusic.Maui.csproj -c Debug -f net10.0-android -p:AndroidSdkDirectory="C:/Users/Administrator/AppData/Local/Android/Sdk" -p:JavaSdkDirectory="C:/Program Files/Microsoft/jdk-21.0.11.10-hotspot/"`。
- 产出: `CatClawMusic.Maui/bin/Release/net10.0-android/publish/*-Signed.apk` (csproj 无自动复制 target)。
- keystore: 已创建于 `catclaw.keystore`(解决方案根, PKCS12, 2026-07-18 17:57)。alias=catclaw, storepass=keypass=catclaw123, dname CN=CatClaw/OU=CatClaw/O=CatClaw/C=CN, 有效期~27年(至2053)。SHA1=9F:D0:61:3A:7C:76:1E:A8:5A:48:89:4F:4D:35:66:65:8A:89:24:69。
- ✅ **正确 csproj 签名写法(2026-07-20 修过)**: 必须在 `<PropertyGroup Condition="...Release...android...">` 内写属性, **且必须显式 `<AndroidKeyStore>true</AndroidKeyStore>`**:
  `<AndroidKeyStore>true</AndroidKeyStore>` + `<AndroidSigningKeyStore>..\catclaw.keystore</AndroidSigningKeyStore>` + `<AndroidSigningKeyAlias>catclaw</AndroidSigningKeyAlias>` + `<AndroidSigningKeyPass>$(CatClawKeyPass)</AndroidSigningKeyPass>` + `<AndroidSigningStorePass>$(CatClawStorePass)</AndroidSigningStorePass>`。
- ⚠️ **签名回退 debug 的坑(2026-07-20 用户反馈"签名不同无法安装"定位)**: 旧写法把签名项塞进 `<ItemGroup><AndroidSigningKeyStore Include=...>` 且**漏了 `AndroidKeyStore>true`** → MAUI 签名 target 不读 Item、且 AndroidKeyStore 默认 false → **静默回退 Android Debug 密钥**(签名者 `CN=Android Debug`), 与 keystore 证书不一致, 已侧载旧版用户无法覆盖安装。修正为上方 PropertyGroup 写法后, `apksigner verify --print-certs` 确认 APK 签名者 DN=CN=CatClaw 且 SHA1 与 keystore 完全一致。
- ⚠️ 换正式签名后, 若用户之前装的是 debug 版 apk, 仍无法覆盖升级(需卸载重装)。务必备份 keystore 文件与密码(丢失则旧用户无法增量升级)。

## Android 16 / 16KB 内存页兼容(2026-07-20)
- **症状**: 三星 One UI 8.5 (Android 16) 启动闪屏后闪退(其他设备正常)。根因 = **原生 `.so` 未做 16KB 页对齐**。Android 15/16 新机(三星旗舰/Pixel 9)默认 16KB 内存页, `dlopen` 4KB 对齐的 `.so` 直接 `dlopen failed: not 16 KB aligned` → SIGABRT。
- **诊断法**: 拆 APK 检查每个 `lib/*/*.so` 的 ELF PT_LOAD `p_align`, 须 ≥ `0x4000`(16KB); `0x1000`(4KB)=会崩。合格库(libcoreclr/libmonodroid 等 .NET10 运行时)先加载→闪屏能显示; `libe_sqlite3` 稍后 DB 初始化时加载才崩→表现为"闪屏后闪退"。
- **元凶**: `sqlite-net-pcl 1.9.172` 传递依赖 `SQLitePCLRaw 2.1.2` 的 `libe_sqlite3.so` 是 4KB 对齐。
- **修复(已做)**: 三个 csproj (Maui/Data/Core) 顶层显式 `<PackageReference Include="SQLitePCLRaw.bundle_green" Version="2.1.11" />` 覆盖传递依赖(Maui 另加 `SQLitePCLRaw.lib.e_sqlite3.android` 2.1.11 仅 Android)。⚠️ **必须 2.1.11, 不能只到 2.1.10**——2.1.10 仅消除 XA0141 警告但 16KB 设备上 DB 仍打不开(见 ericsink/SQLitePCL.raw#621)。已验证 2.1.11 的 arm64-v8a libe_sqlite3.so = 0x4000。
- **遗留**: `Assets/ffmpeg/arm64-v8a/libffmpeg.so`(自编译, 20MB) 也是 4KB 对齐, 但它经 `linker64`/`exec` 当独立程序运行、**非 dlopen 进进程**, 故只会导致 16KB 设备上 m4a/flac/ape 等**转码失败**, 不会崩启动。要彻底修需用 NDK r27+ 加 `-Wl,-z,max-page-size=16384` 重新编译 ffmpeg。
- **JNI 隐患(顺带记)**: `MainActivity.SetupHighRefreshRate` 用 `GetFieldID(...,"preferredDisplayRefreshRate","I")` 签名错误(该字段是 `float`, 应为 `"F"`), 故 120Hz hack 其实一直没生效(被 try/catch 吞), 非崩溃源, 但可择机修正。

## MAUI 已知坑(高频)
- **Android `LayerType` 命名遮蔽**: 嵌套枚举 vs 同名实例属性冲突，唯一正确是在 `Android.Views.View` 子类体内用简单名 `LayerType`(见 `Platforms/Android/HardwareLayerExtensions.cs` 的 `LayerTypeProbe`)。`Platforms/Android` 下 .cs 必须 `using View = Android.Views.View;` 消解与 MAUI `Controls.View` 歧义。
- **权限元组**: `RequiredPermissions` 元素名 `(string androidPermission, bool isRuntime)` (非旧文档 `showRationale`)。
- **WinUI 图标按钮**: 纯图标无文字按钮用 `<ImageButton>` 而非 `<Button ImageSource>`(后者 Windows 不渲染图标)。XAML 字面量 `Image Source` Windows 也可能不渲染，需后台 `ImageSourceHelper.FromNameOriginal(...)` 显式赋。
- **未打包 Windows `Preferences` 静默失效**: `WindowsPackageType=None` 下 `ApplicationData.Current` 为 null, `Preferences` Set/Get 无操作。自定义文件夹已迁 `FileSystem.AppDataDirectory/custom_music_folders.json`(`CustomFolderStore`)。其他设置(ffmpeg/media_store/saf/主题)若反馈不保存亦需迁移文件存储。
- **Windows 命名空间**: `Platforms/Windows` 下 `Windows.Storage.*` 加 `global::` 前缀;类名勿用 `FolderPicker`(与 MAUI 冲突)。
- **键盘快捷键**: `ContentPage`/`Microsoft.UI.Xaml.Window.KeyDown` 都拿不到;在 `OnAppearing` 对 `Handler.PlatformView as Microsoft.UI.Xaml.UIElement` 订阅 `KeyDown`,按键用 `Windows.System.VirtualKey`, 用完全限定名避免 `using Microsoft.UI.Xaml;` 冲突。
- **顶部导航栏**: `AppShell.xaml` 全局样式对所有 `ContentPage` 设 `Shell.NavBarIsVisible="False"`;底部 TabBar 不受影响。
- **桌面根页**: `#if WINDOWS` 下 `App.xaml.cs CreateWindow` 用 `DesktopMainPage`(1200×800, Min 900×600) 替代手机 `MainPage`+TabBar。
- **`ForceLayout()` 已移除(Xamarin→MAUI API 变更)**: `View`/`VisualElement` 不再有 `ForceLayout()`(CS1061);强制重排用 `InvalidateMeasure()`(或 `Layout` 子类用 `InvalidateLayout()`)。项目惯例见 `MainPage.xaml.cs`(`this.Content?.InvalidateMeasure()`)。
- **自定义 ViewPager 滑动性能**: 移动端 tab 切换架构 **2026-07-19 已改为双端分支**：
  - ⚠️ **真因（2026-07-19 最终定位）**：抽搐是 **.NET 10 vs .NET 11 渲染差异**，不是代码逻辑。发行的 `v1.6.5` 标签(`80cb27f`)是 **`net11.0-android`**，其 `OnPanUpdated` 就**带看门狗 + 拖拽期 `SetHardwareLayersEnabled(true)` 全页硬件层**，在 net11 上靠 GPU 合成 `TranslationX` 所以顺滑。当前 HEAD(`7e77c4c` "适配 MAUI 10")降级到 `net10` 后，不靠硬件层、每帧重绘 CollectionView 子树 → 抽搐。看门狗在 v1.6.5 里本就有且 net11 下不抽搐，故**看门狗不是元凶**（之前误判）。
  - ⚠️ **net11 退路已堵死（2026-07-19 用户确认）**：net11 在本环境**无法调试、且不能用 mono 编译**，故「回 net11 修底部空白」不可行。抽搐必须在 **net10** 上通过改机制解决，**不要再建议回 net11**。
  - **最终方案（用户选「原生 ViewPager2」）**：**Android 端用原生 AndroidX `ViewPager2` 承载 5 个 MAUI 页**，水平分页位移走原生 GPU 合成，彻底摆脱 MAUI `TranslationX` 重绘 → 根治 net10 抽搐。**Windows 端保留原手动 `PanGestureRecognizer + TranslationX + 懒加载 + 硬件层`**（Windows 无此问题，作兜底）。
    - 新增 `Controls/NativeTabPager.cs`：跨平台 `View`（`IList<ContentPage> Pages` + `PageSelected`/`ScrollStateChanged` 事件 + `SetCurrentItem`/`SetOffscreenLimit`）；`ScrollState` 枚举 Idle/Dragging/Settling。
    - 新增 `Platforms/Android/NativeTabPagerHandler.cs`：`ViewHandler<NativeTabPager, ViewPager2>`，`ConnectHandler` 内建 `MauiPagerAdapter`(`RecyclerView.Adapter`，**整页 `page.ToPlatform(mauiContext)` 承载**，每页独立 viewType+`HasStableIds` 防跨页回收) + `PageChangeCallback`(转发选中/滑动态) + `OffscreenPageLimit = Pages.Count`(全部常驻，规避回收重绑风险) + 注入 `PlatformSetCurrentItem/PlatformSetOffscreen`。`MauiProgram.cs` 已注册 handler。
    - `MainPage.xaml.cs`：`SetupPages` 双端分支——Android 直接把 `_tabPages`(5 个 `ContentPage`) 交给 `NativeTabPager`（不再抽取 `Content`，故 `BindingContext`/SafeArea 行为照常；**不再挂 `AddPanToLayouts` 手势**，手势交 ViewPager2 原生处理）；Windows 走旧 `ViewPagerGrid` 抽取+懒加载。`SwitchToVpIndex(idx)` 统一切页入口：Android→`NativePager.SetCurrentItem`，Windows→`AnimateToPage`。`OnNativePageSelected` 更新 `_currentIndex`/生命周期/TabBar；`OnNativeScrollStateChanged` 在 Dragging/Settling 调 `BeginInteraction("TabSwipe")` 暂停 `FrostedBackground`，Idle 恢复。
    - ⚠️ **`CollapseNowPlaying` 在 Android 简化为「直接切到发现页」(index 2)**——原生 ViewPager2 无法在分页之上叠加纵向抽屉收起动画，抽屉效果仅 Windows 保留。若以后要在 Android 恢复抽屉感，需把收起做成 ViewPager2 之上的独立 Overlay 层。
    - ✅ **已真机验证（2026-07-19 用户确认「丝滑」）**：原生 ViewPager2 方案根治了 Android/net10 的左右滑动抽搐，无需再回 net11。本环境只能编译验证（Android + Windows 均 0 错误），真机滑动顺滑度已通过。
    - csproj 已加 `Xamarin.AndroidX.ViewPager2` 1.1.0.10（缓存命中）；构建有 NU1608 警告（Lifecycle.Process 2.9.2.1 期望 Lifecycle.Runtime 2.9.x，实际解到 2.10.0.2），仅为警告，若真机报 AndroidX 类找不到再 pin 版本。
  - **第八轮（2026-07-19，用户选"跟手+懒加载"）新增的懒加载相邻页逻辑现仅用于 Windows 端**：`UpdatePageVisibility()`/`SetTransitionVisibility()`/`UpdatePagePositions±1`/`SetHardwareLayersEnabled`(按可见性开层) 在 Android 原生路径不再调用。
- **状态栏 SafeArea**: 非全屏 Tab 页(发现/歌单/音乐库, ViewPager index>1)在 `MainPage.SetupPages` 各自挂 `SafeAreaPaddingBehavior`(行为基类是 `Behavior<Layout>`，本 MAUI 版 `Padding` 在 `Layout`/`ScrollView` 上、`View` 基类无 `Padding` 属性！)；`LibraryPage` 根由 `ScrollView` 包了一层 `Grid` 以便挂行为。`SettingsPage`(独立 Shell 页)XAML 根 `Grid` 挂同行为。ViewPagerGrid 不再加顶部 padding(改由各页自理，更健壮)。全屏页(歌词/播放页 index 0/1)不挂，保持边缘到边。

## 主题与背景图(2026-07-20)
- **主题精简为 5 套**(橙/粉/紫/蓝/青, 对应 hex FF7043/EC407A/9B7ED8/42A5F5/26A69A): 改了 `IThemeService.AppTheme` 枚举(删除 Green/Red/Yellow/Indigo/Cyan, 保留值 Purple=0/Pink=1/Blue=2/Orange=4/Teal=6 以兼容旧偏好)、`ThemeService.ThemeMap`+3 个 color map、`AppearanceSettingsViewModel` 两处 switch、`AppearanceSettingsPage.xaml` 色块、`BackgroundImageMap`(橙/粉/紫/蓝/青 → `bg_{color}_{starry|sky}`)。旧保存的绿/红/黄等 int 在 `LoadSettings` 因不在 ThemeMap 被重置为 Purple(安全)。
- **背景图层结构**: MainPage 根 Grid 底层 = `ThemeBackgroundImage`(Image) + `BackgroundMask`(BoxView, 深色 45%黑/浅色 40%白), 其上是原生 `ViewPager2` 承载的 5 个 tab 页。**遮罩在 MainPage、内容页之下**——内容页必须透明才能透出图+遮罩。
- **黑屏三大根因(均已修)**:
  1. **csproj 未打包背景图**: 顶层 `<MauiImage Include="Resources\Images\*" />` 不递归子目录 → `Backgrounds/` 下 10 张 jpg 不进 APK。修复: 新增 `<MauiImage Include="Resources\Images\Backgrounds\*" />`。
  2. **`ImageSource.FromFile` 解析失败**: `ThemeService.ApplyThemeBackgroundImage` 原用 `ImageSource.FromFile("bg_orange_starry")`, 但 MauiImage jpg 在 APK 是 `res/drawable/bg_orange_starry.jpg`(带扩展名), FileImageSource 按名找 drawable 不补 `.jpg` → 找不到→空白→黑屏。⚠️ **必须用纯字符串** `"bg_orange_starry"` 存入字典, 让 `Image.Source` 经 `ImageSourceConverter` 查 MauiImage 注册表(它知道真实 drawable 名)。这与 Tab 图标 `Source="ic_play"` 同一机制。
  3. **tab 内容页不透明背景盖住图**: FullLyrics/NowPlaying/Search/Playlist/Library 5 页根 `BackgroundColor=WindowBackgroundColor` + 内层 `PageBackgroundBrush` 不透明, 正好盖在 MainPage 背景图上。已改为 `Transparent`(图不可用时回退到 MainPage 根 Grid 的 `PageBackgroundBrush`, 不会变黑)。
- **验证**: 解包 APK `unzip -l ...-Signed.apk | grep bg_` 可见 `res/drawable/bg_{orange,pink,purple,blue,teal}_{starry,sky}.jpg` 共 10 张。

## 歌词
- LRC(标准) + TTML(W3C, v1.5.1+, 逐字时间戳, 支持 .lrc/.ttml/.xml 自动检测)。解析 `LyricsService.cs` / 文件查找 `MusicUtility.cs` / 接口 `ILyricsService.cs`。匹配: 精确 `歌曲名.lrc` → 模糊 `歌曲名*.lrc`。

## 艺术家元数据(MAUI 主线缺口)
- 抓取设施已建好但未接通: `IArtistMetadataScraper` 5 实现(网易云/AI/多源照片已注册 DI;百度百科/豆瓣为孤儿实现未调用)。全仓 `SearchArtistsAsync` 无调用方, `ArtistMatchPage` 为空壳。
- 本地 Artist 表仅存 Name(扫描拆名入库), Gender/Birthday/Region/Description/Cover 不填。`ArtistDetailPage` 现仅 Name+歌曲封面头像。
- 要做资料卡需: ①接通抓取 ②持久化(扩列或 JSON 缓存) ③把 `BaiduBaikeScraper` 重新注册(厚字段最全来源)。

## 猫爪圈跨网 P2P(CatClawMusic.Core/ClawCircle)
- 端口: UDP 发现 37821 / TCP 直传 37822 / HTTP 媒体中心 37823 / STUN 37824。
- 阶段: ①NAS 媒体中心(Navidrome 兼容) ②WS tracker `/ws/clawcircle`(须 `app.UseWebSockets()` 在前) ③UDP NAT 打洞+分块直传。
- 坑: relay 反序列化须 `PropertyNameCaseInsensitive=true`; UDP 片 < 65507(默认 16KB); `SemaphoreSlim(0)` 勿多次 Release。验证 Harness 双节点真实服务端跑通。

## README 约定(公开仓库)
- `README.md` 必须 GitHub 友好纯 markdown: 禁 `<style>`/`<script>`;截图 `<details>` 默认收起;徽章 shields.io 图片;版本以 csproj 为准(v1.6.4)。本地花哨版留 `README.html` 勿覆盖。
- **不要显示更新日志**: 用户要求删除且以后不加回。

## 设计流程约定
- UI 改动一律「先出 HTML 原型确认视觉, 再落地 MAUI 代码」。历史原型在 `docs/`(listening-stats / song-detail / music-library-detail / music-library-overview / artist-detail / artist-list / album-list / now-playing / log-page 等)。品牌: 深空蓝玻璃拟态, 紫蓝渐变 #8C7BFF/#55D6FF/#080B1A, 字体 Space Grotesk + Noto Sans SC。
- 用户偏好: 不要播放控制假组件、不要假数据(本地播放器无法区分性别/地区)、GitHub 友好 markdown。
