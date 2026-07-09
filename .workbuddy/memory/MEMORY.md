# CatClawMusic 项目记忆

## 构建
- **构建入口**: `CatClawMusic.Maui/CatClawMusic.Maui.csproj`（v1.5.3, UseMaui=true, 单项目）。注意：旧的 `CatClawMusic.UI.csproj`（v1.5.1, Xamarin.Android 原生）已不是主线。
- **TargetFramework**: 当前为 `net11.0-android`（旧记录 net10.0-android 已过时）。
- **Debug 验证命令**: `dotnet build CatClawMusic.Maui/CatClawMusic.Maui.csproj -c Debug -f net11.0-android -p:AndroidSdkDirectory="C:/Users/lvjin/AppData/Local/Android/Sdk" -p:JavaSdkDirectory="C:/Program Files/Android/openjdk/jdk-21.0.8"`
- **Release 签名命令**: `dotnet publish CatClawMusic.Maui/CatClawMusic.Maui.csproj -c Release -f net11.0-android -p:AndroidSdkDirectory="C:/Users/lvjin/AppData/Local/Android/Sdk" -p:JavaSdkDirectory="C:/Program Files/Android/openjdk/jdk-21.0.8" -p:CatClawStorePass=catclaw123 -p:CatClawKeyPass=catclaw123`
- **Android SDK 平台要求**: `net11.0-android` 需要 API 37（本机已通过 `dotnet build -t:InstallAndroidDependencies -f net11.0-android "-p:AcceptAndroidSDKLicenses=true"` 安装；本机还装有 35/36）。
- **自定义权限元组命名坑**: 当前 MAUI（microsoft.maui.controls 11.0.0-preview.5.26304.4）的 `Permissions.BasePlatformPermission.RequiredPermissions` 元组元素名为 `(string androidPermission, bool isRuntime)`，重写时必须用 `isRuntime`（不是旧文档的 `showRationale`），否则 CS8139。
- **Android `LayerType` 命名遮蔽坑**: `Android.Views.View` 上 `LayerType` 既有**嵌套枚举类型**又有**同名实例属性**，成员访问 `T.LayerType` 与 `using X = T.LayerType` 别名都会优先选中实例属性（报 CS0120/CS0176/CS0426），无法取得枚举值。唯一正确方式是在 `Android.Views.View` 的**子类体内**用简单名 `LayerType` 命中嵌套枚举（如 `SetLayerType(LayerType.Hardware, null)`）。封装见 `Platforms/Android/HardwareLayerExtensions.cs`（用 `LayerTypeProbe : View` 探针类暴露枚举值）。`Platforms/Android` 下的 .cs 必须 `using View = Android.Views.View;` 消解与 MAUI `Microsoft.Maui.Controls.View` 的歧义（同 `FrostedBackgroundHandler.cs` 做法）。
- **签名密码**: storepass=keypass=catclaw123，alias=catclaw，keystore=../catclaw.keystore
- **产出路径**: `CatClawMusic.Maui/bin/Release/net11.0-android/publish/*-Signed.apk`（Maui csproj 无自动复制 target，与 UI csproj 不同）
- **规则**: 每次有较大代码改动后，自动构建 APK
- **版本**: v1.5.3

## 艺术家元数据
- **模型**: `ArtistSearchResult` 类（在 `IArtistMetadataScraper.cs` 中）包含以下字段：
  - 基本信息：Name, NameAliases, Gender, Area, Birthday, Description, PhotoUrl
  - 扩展信息（v1.4.9新增）：RealName（本名）, Nickname（昵称）, Ethnicity（民族）, BirthPlace（出生地）, Education（毕业院校）, Zodiac（星座）, Height（身高）, Agency（经纪公司）, RepresentativeWorks（代表作品）, Occupation（职业）
- **保存**: `ArtistMetadataSaver.cs` 将元数据保存为 JSON（`artist_info.json`）
- **显示**: `ArtistDetailFragment.cs` 显示本名和扩展信息

## 数据源优先级
- **网易云** → **百度百科** → **豆瓣** → **QQ音乐**
- `BaiduBaikeScraper.cs`：解析百度百科 infobox，提取丰富的艺术家元数据
- `DoubanScraper.cs`：提供中文简介（网页抓取方式）
- `MultiSourcePhotoScraper.cs`：只实现 QQ音乐（已移除 iTunes/Wikipedia）
- 已删除：MusicBrainz、TheAudioDB、iTunes、Wikipedia（对中文艺术家效果差）

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
