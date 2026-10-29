# Windows 端迁移到 WinUI 3：方案与执行计划

> 定调（2026-10）：Windows 桌面端从 MAUI 迁到 **WinUI 3**；Android 端已改由
> `CatClawMusic.Android`（Kotlin/Compose）负责，MAUI 的 Android 目标弃用。
> 本文件是迁移的单一事实来源：改架构先改这里，再动代码。

## 1. 边界与结论

- **新建工程 `CatClawMusic.WinUI`**，不改造 `CatClawMusic.Maui`（它退为参考实现，Windows 目标过渡期还能出包）。
- **`CatClawMusic.Core` / `CatClawMusic.Data` 原样复用**：两者都是纯 `net11.0`，
  不含任何 UI 框架依赖（TagLibSharp / sqlite-net / Jint / SMBLibrary），WinUI 3 可直接引用。
- 只有 **`CatClawMusic.Maui/Services` 与 `Pages`/`ViewModels`/`Controls`** 是需要重写的部分 ——
  它们耦合了 `FileSystem` / `Preferences` / `MainThread` / `Application.Current.MainPage` / MAUI 控件。
- 插件体系（`.ccp` = 裸 CLR 程序集）**协议不变**：`PluginManager` 在 Core 里用
  `Assembly.Load(byte[])`，与 UI 框架无关；WinUI 端只需把插件管理页与菜单注入点重画。

## 2. 复用什么、重写什么

| 层 | 处理 | 说明 |
| --- | --- | --- |
| `Core/Interfaces`（29 个契约）、`Core/Models`、`Core/Services`（LyricsService / PluginManager / OnlineMusicAggregator / PlayQueue / JsRuntimeService / TagReader） | **直接复用** | 纯 C#，无 UI 依赖 |
| `Data/*`（MusicDatabase 17 张表、MusicLibraryService、Subsonic/WebDav/Smb、BackupService、多源刮削） | **直接复用** | 纯 C#；播放器要的流由服务层给 URL |
| `Maui/Services/AudioPlayerService(.Windows)` | **移植** | `Windows.Media.Playback.MediaPlayer` + `AudioGraph` Biquad EQ 本来就是 WinRT API，可原样搬 |
| `Maui/Services/ThemeService` | **重写** | 配色表（单一樱粉主题 + 深/浅两套背景）搬过来，输出改为 WinUI `ResourceDictionary` / `FrameworkElement.RequestedTheme` |
| `Services/{Dialog,Navigation,Log,MainThread,Permission,Update}Service` | **重写** | 换成 `ContentDialog` / 自建路由 / 文件日志 / `DispatcherQueue` / 空实现 / GitHub Release 检查 |
| `Services/{DownloadManager,AudioCache,ImageCache,CoverHelper,SleepTimer,LocalScan,DesktopLyricManager}` | **移植（改依赖）** | 逻辑基本不变，替换 `FileSystem`→`AppPaths`、`Preferences`→`JsonSettingsStore`、`MainThread`→`DispatcherQueue` |
| `Services/{ThemeService 背景链, GlobalBackgroundService, FrostedBackgroundHandler, KaraokeLabelHandler, Vitrum}` | **重做** | MAUI Handler / Win2D 混合层，WinUI 3 用 `MicaBackdrop`/`DesktopAcrylicBackdrop` + Win2D 重写 |
| `Pages/ViewModels/Controls`（65 页 / 44 VM / 35 控件） | **逐批重写** | 按第 4 节阶段推进；`Desktop*` 页面是 Windows 专用，优先迁 |
| `Resources/AppResources*.resx`（51 种语言） | **复用** | P1 起把 resx 作为 `EmbeddedResource` 链入，`ResourceManager` 直读，不重建翻译 |

## 3. 目标工程结构

```
CatClawMusic.WinUI/
  App.xaml(.cs)                 应用入口 + DI 容器 + 全局异常
  MainWindow.xaml(.cs)          窗口：自绘标题栏 + 左侧栏 + 内容区 + 底部播放条
  Infrastructure/
    AppPaths.cs                 %LOCALAPPDATA%\CatClawMusic\ 下的目录与文件名
    JsonSettingsStore.cs        替代 MAUI Preferences（settings.json）
    ServiceCollectionExtensions.cs  DI 注册（分 Core/Data/WinUI 三段）
  Services/                     WinUI 平台实现（Theme/Dialog/Navigation/Log/Dispatcher/Player…）
  Views/                        页面（UserControl）
  ViewModels/                   CommunityToolkit.Mvvm
  Themes/Tokens.xaml            语义化 Brush/Thickness 资源（唯一改色入口）
  Assets/
```

技术选型：

- **Windows App SDK 2.3.1**（与 MAUI 侧当前解析到的版本一致）、`net11.0-windows10.0.19041.0`、
  最低 `10.0.17763.0`；**unpackaged + `WindowsAppSDKSelfContained`**，沿用 Inno Setup 出包。
- MVVM：`CommunityToolkit.Mvvm 8.4.2`；DI：`Microsoft.Extensions.DependencyInjection`。
- 主题：语义化 Brush 资源 + `FrameworkElement.RequestedTheme`（WinUI 3 运行期不能改 `Application.RequestedTheme`）。
- 播放：`Windows.Media.Playback.MediaPlayer`（与 MAUI Windows 同一实现），系统媒体控制（SMTC）阶段另定。
- 数据目录：`%LOCALAPPDATA%\CatClawMusic\`（与 MAUI 的 `%LOCALAPPDATA%\CatClawMusic.Maui\...` 分开，
  避免两版同时运行时 SQLite 抢锁；需要共用曲库时做显式导入，不共享库文件）。

## 4. 阶段与验收

| 阶段 | 范围 | 验收 |
| --- | --- | --- |
| **P0 骨架（本轮）** | 工程可编译可启动：自定义标题栏、左侧栏（发现/歌单/音乐库/设置）、内容宿主、底部播放条占位；DI；`AppPaths` + `JsonSettingsStore`；主题令牌（樱粉 × 深/浅）；文件日志；空页面 | `dotnet build` 0 错误；启动能看到壳层且四页可切换 |
| P1 曲库 | 本地扫描 → SQLite → 全部歌曲/歌手/专辑/歌单列表；底部播放条接真播放；歌曲右键菜单；歌单增删改 | 能扫描真实目录、起播、切歌、重启保留 |
| P2 播放与歌词 | 播放页（封面取色背景、进度/音量/播放模式）、全屏歌词页、逐字/翻译/罗马音、桌面歌词第二窗口 | 歌词逐行/逐字跟手；桌面歌词可拖动 |
| P3 发现与插件 | 发现页（推荐/排行/歌手/专辑/统计）、聚合搜索、插件管理（安装/启用/删除/配置）、插件菜单注入 | 装一个 `.ccp` 后能搜到歌并播放 |
| P4 AI | Yuki 聊天（流式 + 工具循环 + 记忆/词库）、AI 设置与模型管理 | 工具调用可操作曲库/播放 |
| P5 收尾 | 设置全量（外观/歌词/本地/远程/AI/插件/备份/日志/关于）、下载/BT、备份恢复、i18n、出包安装程序 | 与 MAUI 版逐页对照表全绿；安装包可覆盖升级 |

每个阶段结束都要求：`dotnet build CatClawMusic.WinUI` 通过、`tests/CatClawMusic.Data.Tests` 保持通过
（Core/Data 未改动时必绿），并在本文件「执行记录」补一行。

## 5. 风险与对策

| 风险 | 对策 |
| --- | --- |
| XAML 编译器在 .NET 11 上退出码异常（MAUI 侧已踩过） | 先复现；必要时把 MAUI 的 `XamlCompilerWrapper.bat` 思路搬过来 |
| WinAppSDK 2.3.1 与 Win2D / 第三方包兼容性 | P0 只依赖官方包；Win2D 到 P2 背景/可视化时再引，先验证最小样例 |
| Mica 仅 Win11 可用 | 运行期 `MicaController.IsSupported()` 判断，Win10 回退 `DesktopAcrylicBackdrop` / 纯色 |
| 两版同时运行争 SQLite | 数据目录分离（见第 3 节）；不做库文件共享 |
| 插件 ALC/版本漂移 | `PluginManager` 不变；插件必须引用与宿主同一份 Core —— 迁移完成后重编三个插件仓库的引用路径 |
| 51 种语言的 resx 结构 | P1 直接把 `CatClawMusic.Maui/Resources/AppResources*.resx` 链入（`EmbeddedResource` + `LogicalName`），跑通后再决定是否迁到共享工程 |

## 6. 执行记录

- **2026-10-29 P0 完成**：新建 `CatClawMusic.WinUI`
  - 工程：`net11.0-windows10.0.19041.0` + Windows App SDK 2.3.1，unpackaged + 自包含，
    默认 `RuntimeIdentifier=win-x64`（WindowsAppSDKSelfContained 需要确定的架构；ARM64 出包传 `-r win-arm64`）
  - 壳层：自绘标题栏（`SetTitleBar` + 系统按钮透明底/深浅色前景）、`NavigationView` 左侧栏
    （发现 / 歌单 / 音乐库 + 底部设置）、内容宿主、底部播放条占位
  - 基础设施：`AppPaths`（`%LOCALAPPDATA%\CatClawMusic\`）、`JsonSettingsStore`（替代 Preferences）、
    `FileLogService`（8MB 轮转，注册为 Core 静态 `Log` 提供者）、`WinUiDispatcherService`、
    `WinUiDialogService`（ContentDialog）、`WinUiNavigationService`、`WinUiThemeService`（樱粉 × 深/浅，即时生效并落盘）
  - 复用验证：`MusicDatabase.EnsureInitializedAsync()` 启动即建库成功（17 张表），Core/Data 零改动
  - 验收：`dotnet build` 通过；启动截图确认浅色 / 深色两套令牌、四个页面可切换、设置页深浅切换可用
  - 踩坑：`NavigationView` 的 **PaneFooter 里的 `NavigationViewItem` 不参与选中模型**，
    把它赋给 `SelectedItem` 会抛 `ArgumentOutOfRangeException`（invalid vector subscript）——
    设置入口因此改成普通 `Button` + 自行高亮
