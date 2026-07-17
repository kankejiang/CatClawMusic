# CatClawMusic 项目记忆

## 构建
- **构建入口**: `CatClawMusic.Maui/CatClawMusic.Maui.csproj`（v1.5.3, UseMaui=true, 单项目）。注意：旧的 `CatClawMusic.UI.csproj`（v1.5.1, Xamarin.Android 原生）已不是主线。
- **TargetFramework**: 当前为 `net11.0-android`（旧记录 net10.0-android 已过时）。
- **默认构建目标**: **Release 签名包**（用户要求 2026-07-08 起默认出 Release。Debug 仅用于快速验证编译）。
- **Release 签名命令（默认）**: `dotnet publish CatClawMusic.Maui/CatClawMusic.Maui.csproj -c Release -f net11.0-android -p:AndroidSdkDirectory="C:/Users/Administrator/AppData/Local/Android/Sdk" -p:JavaSdkDirectory="C:/Program Files/Microsoft/jdk-21.0.11.10-hotspot/" -p:CatClawStorePass=catclaw123 -p:CatClawKeyPass=catclaw123`
- **Debug 验证命令（仅快速验证）**: `dotnet build CatClawMusic.Maui/CatClawMusic.Maui.csproj -c Debug -f net11.0-android -p:AndroidSdkDirectory="C:/Users/Administrator/AppData/Local/Android/Sdk" -p:JavaSdkDirectory="C:/Program Files/Microsoft/jdk-21.0.11.10-hotspot/"`
- **Android SDK 平台要求**: `net11.0-android` 需要 API 37（本机已通过 `dotnet build -t:InstallAndroidDependencies -f net11.0-android "-p:AcceptAndroidSDKLicenses=true"` 安装；本机还装有 35/36）。
- **自定义权限元组命名坑**: 当前 MAUI（microsoft.maui.controls 11.0.0-preview.5.26304.4）的 `Permissions.BasePlatformPermission.RequiredPermissions` 元组元素名为 `(string androidPermission, bool isRuntime)`，重写时必须用 `isRuntime`（不是旧文档的 `showRationale`），否则 CS8139。
- **Android `LayerType` 命名遮蔽坑**: `Android.Views.View` 上 `LayerType` 既有**嵌套枚举类型**又有**同名实例属性**，成员访问 `T.LayerType` 与 `using X = T.LayerType` 别名都会优先选中实例属性（报 CS0120/CS0176/CS0426），无法取得枚举值。唯一正确方式是在 `Android.Views.View` 的**子类体内**用简单名 `LayerType` 命中嵌套枚举（如 `SetLayerType(LayerType.Hardware, null)`）。封装见 `Platforms/Android/HardwareLayerExtensions.cs`（用 `LayerTypeProbe : View` 探针类暴露枚举值）。`Platforms/Android` 下的 .cs 必须 `using View = Android.Views.View;` 消解与 MAUI `Microsoft.Maui.Controls.View` 的歧义（同 `FrostedBackgroundHandler.cs` 做法）。
- **签名密码**: storepass=keypass=catclaw123，alias=catclaw，keystore=../catclaw.keystore
- **产出路径**: `CatClawMusic.Maui/bin/Release/net11.0-android/publish/*-Signed.apk`（Maui csproj 无自动复制 target，与 UI csproj 不同）
- **规则**: 每次有较大代码改动后，自动构建 **Release 签名 APK**（默认目标，见上方 Release 命令）
- **版本**: v1.6.4（ApplicationDisplayVersion 1.6.4 / ApplicationVersion 41，见 csproj）
- ⚠️ **README 约定（重要）**：仓库公开于 GitHub（github.com/kankejiang/CatClawMusic）。`README.md` 必须是 **GitHub 友好的纯 markdown**——不能用 `<style>`/`<script>`（GitHub 会过滤，线上变成裸标签）。截图用 `<details>` 默认收起；徽章用 shields.io 图片链接；功能用 markdown 表格；可爱感只靠 emoji + 排版。本地花哨版保留为 `README.html`（仅供本地浏览器预览，不要提交覆盖 README.md）。版本号以 csproj 为准（当前 1.6.4），勿写死旧值。

## 艺术家元数据（MAUI 主线真实情况，2026-07-16 核实）
- ⚠️ 下方旧记录描述的是已非主线的 Xamarin 项目 `CatClawMusic.UI`（含 `ArtistMetadataSaver.cs`/`artist_info.json`/`ArtistDetailFragment.cs`）。**当前主线 `CatClawMusic.Maui` 现状不同，见下。**
- **抓取接口**：`IArtistMetadataScraper`（`CatClawMusic.Data/IArtistMetadataScraper.cs`），含 `SearchArtistsAsync(name)→List<ArtistSearchResult>` 与 `DownloadAndCacheArtistCoverAsync`。`ArtistSearchResult` 字段齐全：基础(Name/Gender/Region/Birthday/Description/Alias) + 扩展(RealName/Nickname/Ethnicity/BirthPlace/Education/Zodiac/Height/Agency/RepresentativeWorks/Occupation)。
- **5 个实现**：`NetEaseMusicScraper`(网易云) / `AiArtistScraper`(AI·LLM，填基础字段) / `MultiSourcePhotoScraper`(多源照片) / `BaiduBaikeScraper`(百度百科，解析 infobox 填**全部**扩展字段，最全) / `DoubanScraper`(豆瓣)。
- **DI 注册优先级（MauiProgram.cs:237-239）**：网易云 → AI → 多源照片。**百度百科与豆瓣当前未注册进 `IArtistMetadataScraper`、也未被任何代码调用（孤儿实现）。**
- **关键缺口**：全仓 `SearchArtistsAsync(` 只有定义、无调用方；`ArtistMatchPage`/`ArtistMatchDetailPage` 是空壳；MAUI 内**无 `ArtistMetadataSaver`、无 `artist_info.json`**。即"抓取能力已建好，但没接到保存与展示"。
- **本地 Artist 表只存 Name**：`MusicDatabase.GetOrCreateArtistIdAsync` 扫描歌曲拆名后 `new Artist { Name = n }` 入库（MusicDatabase.cs:718），Gender/Birthday/Region/Description/Cover 本地扫描均不填。`ArtistDetailPage` 现仅显示 Name + 用歌曲封面回填的头像 + 专辑/歌曲。
- **唯一能间接回填厚字段的路径**：`BackupService` 恢复备份时把 Gender/Birthday/Region/Description 写回 Artist 表（`UpdateArtistAsync`）。
- **要做"艺人资料卡"需补**：① 接通抓取（进入 ArtistDetailPage 或手动"补全资料"时遍历注册 scrapers 取最佳匹配）；② 持久化（扩展 Artist 表加扩展列并 `UpdateArtistAsync`，或按名存 JSON 缓存）；③ 把 `BaiduBaikeScraper` 重新注册进 `IArtistMetadataScraper`（厚字段最全来源），合并逻辑优先采用其扩展字段。

## 探索设置
- 来源选择 Chip 定义在 `fragment_artist_match.xml` 和 `fragment_artist_match_detail.xml`
- C# 映射在 `ArtistMatchFragment.SourceChipToName` 和 `ArtistMatchDetailFragment.SourceChipToName`
- 当前来源选项：网易云 / 百科 / 豆瓣 / QQ音乐
- 多源来源的搜索/下载通过 `MultiSourcePhotoScraper` 按 Source 前缀过滤

## 歌词支持
- **LRC 格式**：标准歌词格式（原有支持）
- **TTML 格式**：v1.5.1 新增，W3C 标准（常用于 Apple Music）
  - 支持文件扩展名：`.lrc`、`.ttml`、`.xml`（自动检测是否为 TTML）
  - 支持逐字时间戳（`<span begin="..." end="...">`）
  - 支持时间戳格式：`HH:MM:SS.mmm`、`MM:SS.mmm`、秒数、`PT...S`
  - **v1.5.2 修复**：改进对唱歌曲的 TTML 解析，添加详细调试日志，改进异常处理
- **文件匹配规则**（精确 → 模糊）：
  1. `歌曲名.lrc` / `歌曲名.ttml` / `歌曲名.xml`
  2. `歌曲名*.lrc` / `歌曲名*.ttml` / `歌曲名*.xml`
- **相关文件**：`LyricsService.cs`（解析器）、`MusicUtility.cs`（文件查找）、`ILyricsService.cs`（接口）

## MAUI 顶部导航栏（NavBar）
- **全局隐藏**：在 `AppShell.xaml` 中通过全局样式对所有 `ContentPage` 设置 `Shell.NavBarIsVisible="False"`
- 效果：所有 Tab 页和二级页面均不再显示顶部导航栏标题区域，页面内容延伸到状态栏下方
- 底部 TabBar 不受影响
- 二级页面返回仍通过系统返回键/手势正常工作

## MAUI WinUI 图标按钮坑（重要）
- **`Button.ImageSource` 在 WinUI 上对纯图标按钮不渲染图标**。凡是「只显示图标、无文字」的按钮，一律用 `<ImageButton Source="{Binding ...}">`，**不要**用 `<Button ImageSource="{Binding ...}">`（后者 Windows 上图标始终空白，Android 正常，极具迷惑性）。
  - ImageButton 保留 `Command`/`Clicked`/`WidthRequest`/`HeightRequest`/`CornerRadius`/`BackgroundColor`/对齐；建议加 `Aspect="AspectFit"` + 适当 `Padding` 防止图标贴边。
  - 只捕获点击的透明按钮（`Text=""`、无图标）可继续用 `<Button>`。
  - 已修复文件：`NowPlayingPage.xaml`（PhoneControls/DesktopControls）、`DesktopMainPage.xaml`（底部迷你播放栏）。详见 2026-07-09 日志。
- 同源现象：XAML 字面量 `Image Source="ic_xxx"` 在 Windows 上也可能不渲染，需在代码后台用 `ImageSourceHelper.FromNameOriginal(...)` 显式赋 `Image.Source`（如 NowPlayingPage 的 BackButtonIcon）。

## Windows 端平台特性（MAUI net11.0-windows）
- **添加音乐文件夹**：Windows 用 `Platforms/Windows/WindowsFolderPicker.cs`（WinUI `Windows.Storage.Pickers.FolderPicker`
  + `InitializeWithWindow.Initialize` 绑定 HWND），选中真实路径后通过 `CustomFolderStore`（见下）写入
  `FileSystem.AppDataDirectory/custom_music_folders.json` 文件。**切勿再用 `Preferences` 存自定义文件夹**（未打包 Windows 上静默失效，见下）。
  扫描服务 `LocalScanService` 读 `custom_music_folders` 全平台通用，所以 Windows 添加即生效。
- **命名空间坑**：`Platforms/Windows` 下文件命名空间含 `...Platforms.Windows`，`Windows.Storage.*` 必须加 `global::` 前缀，
  否则被解析成嵌套命名空间报 CS0234；类名勿用 `FolderPicker`（与 MAUI 内置冲突）。详见 2026-07-07 日志。
- Android-only 的扫描开关（MediaStore / SAF）在 Windows 的 `LocalMusicSettingsPage.xaml` 已用
  `IsVisible="{OnPlatform Default=false, Android=true}"` 隐藏。
- **键盘快捷键坑（net11.0-preview）**：`ContentPage` 没有 `KeyDown` 事件；WinUI `Microsoft.UI.Xaml.Window.KeyDown` 在此版本也拿不到。
  正确做法：在页面 `OnAppearing`（此时 `this.Handler` 已就绪）对底层 WinUI 可视元素订阅路由事件
  `if (this.Handler?.PlatformView is Microsoft.UI.Xaml.UIElement rootUi) rootUi.KeyDown += Handler;`，
  handler 签名 `(object?, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)`，按键用 `Windows.System.VirtualKey`（**不是** `Microsoft.UI.Xaml.Input`）。
  **不可** `using Microsoft.UI.Xaml;`（与 MAUI `Window`/`GridLength`/`Thickness` 冲突 CS0104），必须用完全限定名。
  非 Windows 用 `#else` 提供空 `AttachKeyboard()`。详见 2026-07-07 日志。
- **桌面根页**：`App.xaml.cs` `CreateWindow` 在 `#if WINDOWS` 下用 `DesktopMainPage` 作为 ShellContent（窗口 1200×800，Min 900×600），
  替代手机端 `MainPage` + TabBar。PC 化改造都集中在 `DesktopMainPage.xaml/.xaml.cs`。
- **未打包 Windows 上 MAUI `Preferences` 静默失效（重要坑）**：本应用为 `WindowsPackageType=None` 的未打包 WinUI 程序，
  `Windows.Storage.ApplicationData.Current` 为 null，MAUI 的 `Preferences` 在 Windows 上被内部空值守卫**静默失效**
  （`Set` 无操作、`Get` 返回默认值，且不抛异常）。任何用 `Preferences` 持久化的数据在未打包 Windows 上都「看似成功、实则没存」。
  **已踩雷**：自定义音乐文件夹 `custom_music_folders` 用 `Preferences` 存储 → Windows 上添加文件夹后扫描永远 0 首（报「本地音乐库已清空」）。
  **修复**：改为 `FileSystem.AppDataDirectory` 下的 JSON 文件存储（见 `Services/CustomFolderStore.cs`，首次读取会从 `Preferences` 迁移旧数据，兼容 Android）。
  **仍受影响**：`LocalMusicSettingsViewModel` 里的 `ffmpeg_enabled` / `use_media_store` / `use_saf_scan` 及主题等 `Preferences` 读取在未打包 Windows 上同样可能不持久；
  若用户反馈这些设置不保存，需同样迁移到文件存储。详见 2026-07-09 日志。

## 猫爪圈跨网 P2P（三阶段，服务端在 D:\Code\CatClawMusicServer）
- **端口族**：UDP 发现 37821 / TCP 直传(LAN) 37822 / 媒体中心 HTTP 37823 / STUN UDP 37824(=37823+1)。
- **阶段1**：CatClawMusicServer = NAS 媒体中心（扫描/流式/Subsonic 兼容/鉴权），自包含 EXE 发布到 `D:\catclaw-server-publish`。客户端走 Navidrome 协议直连。
- **阶段2**：服务端 WebSocket tracker/信令 `/ws/clawcircle`（自带 token 鉴权）。**必须 `app.UseWebSockets()` 在中间件前**，否则 IsWebSocketRequest=false→426。
- **阶段3**（客户端，CatClawMusic.Core/ClawCircle/）：UDP NAT 打洞 + 分块直传/做种。**关键：打洞需 UDP 反射端点，由服务端 STUN(37824) 观察**（WS 的 TCP 端点不能用）。引擎 `Start()` 用同一 UdpDirectChannel 向 STUN 打 `{deviceId}` 包；`EnsureSelfEndpointAsync` 用 query_peer(self) 重试拿自身 UDP 端点。
- **Stage3 坑**：① relay `data.Deserialize<P2PRelayMessage>()` 默认大小写敏感，但信令以 camelCase 发送→须 `PropertyNameCaseInsensitive=true`，否则 Kind 空、对端不回打。② **UDP 片大小必须 < 65507**（默认改 16KB；256KB 会 SendAsync 静默失败）。③ `SemaphoreSlim(0,1)` 多次 Release 会 SemaphoreFullException，用 `new SemaphoreSlim(0)`。
- **无头验证**：`CatClawMusic.P2P.Harness`（控制台，引用 Core，内存数据提供者）双节点经真实服务端跑通 STUN→打洞→96 片分块→整体 SHA256 校验。Python WS 回归测试 `D:\catclaw-test\clawcircle_ws_test.py`。
- **MAUI 集成**：`ClawCircleP2PService`(Data 门面) + `ClawCircleP2PDataProvider`(按 songKey 索引/按片读流/.part 落盘校验) 已注册；`ClawCircleSettings` 加 TrackerUrl/TrackerToken。真实跨 NAT 需双网络真机联调（对称型 NAT 可能仍需中继回退，已留 RelayOnly）。
