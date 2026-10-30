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
- 插件体系：WinUI 端**不再加载 `.ccp`（CLR 程序集）**。定调（2026-10-30）：以后的插件用
  **JS / Python** 形式实现 —— JS 可直接用 Core 已内置的 Jint + Acornima；Python 需评估
  Python.NET 或嵌入式 CPython 的包体与部署成本。`.ccp` 链路只保留给 MAUI 参考实现，不在 WinUI 端接线。

## 2. 复用什么、重写什么

| 层 | 处理 | 说明 |
| --- | --- | --- |
| `Core/Interfaces`（29 个契约）、`Core/Models`、`Core/Services`（LyricsService / OnlineMusicAggregator / PlayQueue / JsRuntimeService / TagReader） | **直接复用** | 纯 C#，无 UI 依赖；`PluginManager`（.ccp）在 WinUI 端暂不使用 |
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
| P3 发现与在线源（**暂缓**） | 发现页先做本地能力（最近播放 / 收藏 / 统计）；在线音源等 JS/Py 插件框架落地后再接 | — |
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
| 插件体系改向 JS / Python | WinUI 不加载 `.ccp`；JS 走 Core 的 Jint + Acornima，Python 需先评估解释器方案（Python.NET / 嵌入式 CPython）与打包体量；框架落地前不做在线音源 |
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

- **2026-10-29 P1 完成**：本地曲库与播放链路
  - 播放内核 `MediaPlayerAudioService`：WinRT `MediaPlayer` + 300ms 位置定时器；
    SMTC 仍按 MAUI 的结论禁用（`CommandManager.IsEnabled=false`）
  - 播放编排 `PlaybackService`：复用 Core `PlayQueue`；模式循环「列表循环 → 单曲循环 → 随机」；
    当前曲/模式写入 settings，启动 `RestoreAsync()` 只恢复不自动开播；播完自动下一首（单曲循环回到曲首）
  - 底部播放条：真实标题/艺术家/时长/进度、播放暂停、上下一曲、播放模式；拖动进度松手才 seek
  - 音乐库页：`FolderPicker`（HWND interop）选目录 → `ScanLocalAsync` 入库 → 歌曲/歌手/专辑三视图、
    关键字搜索、歌手/专辑下钻筛选、右键菜单（播放 / 下一首播放 / 添加到歌单）、空状态
  - 歌单页：自建歌单的列表 / 新建 / 重命名 / 删除 / 播放全部 / 从歌单移除
  - 实测（生成 2 个 WAV 作测试素材）：自动扫描入库 2 首 → 点歌起播、进度推进 → 右键加入歌单成功
  - 已知缺口（后续阶段）：扫描走 Data 的快扫策略（`readDuration:false`），列表时长显示 `--:--`，
    播放时由 MediaPlayer 回填；系统歌单（收藏 / 最近播放）数据源不同，P2 接；封面抽取、EQ、
    交叉淡变、网络音频落盘缓存留到 P2

- **2026-10-30 P2 完成**：播放页 / 歌词 / 桌面歌词
  - 播放页 `PlayingView`：左侧封面 + 曲名/歌手/专辑 + 进度 + 播放控制 + 音量，
    右侧滚动歌词；底部播放条点信息区进入，左上角返回（壳层补了路由历史栈 `GoBack`）
  - 歌词：直接复用 Core `LyricsService`（内嵌 → 外挂 `.lrc` → 插件链路），
    `NowPlayingViewModel` 按播放位置高亮当前行；列表用「上下半屏留白 + ScrollIntoView(Leading)」实现居中滚动；
    点歌词行跳转到对应时间戳
  - 封面：`CoverService` 用 TagLibSharp 抽内嵌图落盘到缓存并把路径回写 Songs 表（网络封面与刮削留到 P3）
  - 桌面歌词：`DesktopLyricWindow` 第二窗口（`OverlappedPresenter.CreateForContextMenu` + 无边框 + 置顶 +
    不进任务栏 + 亚克力底 + 半透明深色层 + 按住拖动），实现 Core `IDesktopLyricService`；
    播放页「桌面歌词」按钮开关，主窗关闭时一并收掉，避免进程不退出
  - 实测：外挂 LRC 正确加载并逐行高亮；桌面歌词窗口深底白字可读（浅色系统主题下也可读）
  - 踩坑：`OverlappedPresenter` 在 WinAppSDK 2.x 没有公开无参构造，要用
    `OverlappedPresenter.CreateForContextMenu()` 再改属性；桌面歌词若只挂亚克力，浅色主题下会白底白字，
    必须再压一层半透明深色
  - 已知缺口：逐字卡拉OK填充未做（当前整行高亮）；歌词翻译/罗马音有数据就显示、无设置开关；
    系统歌单（收藏 / 最近播放）、EQ、交叉淡变、网络音频落盘缓存仍在后续阶段

- **2026-10-30 P2.5 完成**：收藏 / 播放历史 / 系统歌单
  - 播放历史：`PlaybackService` 每次起播写 `MusicDatabase.RecordPlayAsync`（「最近播放」与统计的数据源）
  - 收藏：播放页爱心按钮、音乐库右键「收藏 / 取消收藏」都走 `MusicDatabase.SetFavoriteAsync`
  - 歌单页顶部补两个系统歌单「收藏」「最近播放」（虚拟 id -1 / -2，数据来自 Favorites / PlayHistory，
    不可重命名/删除，歌曲右键只留「播放」）
  - 实测：收藏 1 首、最近播放 2 首，列表与详情一致

- **2026-10-30 插件方向调整（用户定调）**：**暂不做插件；以后的插件用 JS 或 Python 形式实现**
  - 因此撤掉昨晚的 `.ccp` 插件管理页与 `PluginService` 接线（不保留隐藏入口，避免指向已废弃方向）；
    Core 的 `PluginManager` 原样保留，供 MAUI 参考实现使用
  - 顺带证实的旧结论：`.ccp` 插件（如 Lrclib）引用 `Microsoft.Maui.Controls`，在 WinUI 宿主里扫描类型时
    报 `ReflectionTypeLoadException` → 「未找到有效的 IPlugin 实现」，这条路本来也走不通
  - 后续若启动 JS/Py 插件：JS 可直接复用 Core 的 `JsRuntimeService`（Jint + Acornima）；
    Python 先做解释器选型与包体评估，再定协议（建议沿用「来源适配器 + 能力清单」的模型）
