# 插件开发候选清单

> 生成日期：2026-08-11
> 基于对 CatClawMusic 全仓（Core / Data / Maui / Plugins）插件架构与功能面的摸底分析。
> 2026-08-11 补充：新增「七、预制插件蓝图」——播客/有声书、AI DJ、下载入库、批量标签编辑、音频分析、听歌识曲、年度报告、Scrobble、自建服务器同步、Subsonic 补全（详见文末）。

## 一、现状速览

插件基础设施已相当成熟：`PluginManager` 支持 .ccp（裸 .NET 程序集）本地安装与 GitHub Release 安装、installed.json 索引、启用/禁用/卸载、AssemblyResolve 泛化解析 + 反射适配器跨宿主版本兼容。

Core 共定义 11 类插件契约接口，但宿主**真正接线消费的只有 4 类**，其余属于"插座已装好、没电器"的预留扩展点：

| 状态 | 扩展点 | 宿主消费位置 |
|------|--------|--------------|
| ✅ 已接线 | `IOnlineMusicPlugin` | OnlineMusicAggregator 聚合搜索、LyricsService RemoteId 歌词路由、WebViewLogin 浏览器登录、红心同步 |
| ✅ 已接线 | `IViewContributorPlugin` | 发现页顶栏入口按钮（DiscoverPageBase） |
| ✅ 已接线 | 发现子 tab（鸭子类型） | 反射探测 TabTitle/TabIcon/TabOrder/CreateTabView，宿主零 Core 依赖 |
| ✅ 已接线 | `ILyricsProviderPlugin` | LyricsService 歌词兜底链（目前全仓 0 个实现） |
| ⚠️ 预留未接线 | `IThemeProviderPlugin` | ThemeService / 外观设置页未消费 |
| ⚠️ 预留未接线 | `IProtocolProviderPlugin` | WebDAV/SMB/Subsonic 仍硬编码在 Data 层 |
| ⚠️ 预留未接线 | `ICoverProviderPlugin` | 封面走 CoverHelper，无插件兜底链 |
| ⚠️ 预留未接线 | `IMenuContributorPlugin` | 歌曲上下文菜单无插件注入点 |
| ⚠️ 预留未接线 | `IPluginConfigurable` | 插件配置仍是硬编码网易云 Cookie 弹窗 |
| ⚠️ 预留未接线 | `IPlayerPagePlugin` | NowPlayingPage 无插件挂载代码 |
| ⚠️ 预留未接线 | `IAudioVisualizerPlugin` | 配套 `ISpectrumProvider` 连平台实现都没有 |
| ⚠️ 预留未接线 | `IAudioEnhancerPlugin` | 播放采样管线未挂接 |

插件拿宿主服务的方式：各 `Create*(IServiceProvider)` 方法传入完整 DI 容器（PlayQueue、IAudioPlayerService、MusicDatabase、INavigationService、IDialogService 等均可解析）。

## 二、A 档：宿主零改动，现在就能做

完全复刻网易云插件（CatClawMusic.Plugins.Netease）的模式。

| # | 插件 | 实现接口 | 难度 | 说明 |
|---|------|---------|------|------|
| A1 | QQ 音乐插件 | IOnlineMusicPlugin + 发现子 tab + IViewContributorPlugin | 中 | API 成熟，歌词/封面/歌单/排行榜齐全；NeteaseUiKit 可抽成多插件共享模板 |
| A2 | 咪咕插件 | 同上 | 中 | 免费无损是最大差异化卖点 |
| A3 | 酷我 / 酷狗插件 | 同上 | 中 | 直链易得，扩充聚合搜索源数量 |
| A4 | LRCLIB 歌词插件 | ILyricsProviderPlugin | 低 | 按标题/时长/艺人在线匹配歌词，补齐本地歌曲歌词；接口已消费、零实现者，装上即生效 |
| A5 | 歌单工具箱 | IViewContributorPlugin + DI 取 MusicDatabase | 低 | m3u/m3u8/pls/csv 导入导出——宿主完全空白的功能，纯插件实现无需宿主改动 |

## 三、B 档：接口已预留，宿主接线后即可做

| # | 插件 | 需接线的接口 | 宿主改动量 | 说明 |
|---|------|------------|-----------|------|
| B1 | 主题皮肤包 | IThemeProviderPlugin | 小 | ThemeService 增加插件主题来源 + 外观设置页列表；纯数据色板，渲染在宿主，风险最低见效最快 |
| B2 | FTP/SFTP 协议插件 | IProtocolProviderPlugin | 中 | 远程音乐页接入插件协议；ProtocolType 硬编码枚举需改为开放注册 |
| B3 | 网络电台插件 | IProtocolProviderPlugin 或 IOnlineMusicPlugin | 中 | 公开电台目录源（冰/Shoutcast 流） |
| B4 | 在线封面插件 | ICoverProviderPlugin | 小 | CoverHelper 增加插件兜底链，为本地歌曲在线补封面 |
| B5 | 歌曲菜单扩展 | IMenuContributorPlugin | 小 | 歌曲长按菜单注入插件项：导出歌曲、下载封面、发送到设备等入口 |
| B6 | 插件自带配置页 | IPluginConfigurable | 小 | 插件管理页调用 CreateConfigView 替代硬编码 Cookie 弹窗；网易云插件可顺势把登录/音质设置搬回插件内，宿主彻底空壳化 |
| B7 | 频谱可视化 / 播放页替换 | IAudioVisualizerPlugin + ISpectrumProvider / IPlayerPagePlugin | 大 | 需先补平台频谱实现：Android Visualizer + Windows AudioGraph FFT；最炫但工作量最大 |
| B8 | 音效增强器 | IAudioEnhancerPlugin | 中 | ProcessSamples 采样级处理挂入播放管线（Android 可行；EQ 引擎本体平台耦合不宜插件化，适合做"增强器/预设包"） |

## 四、C 档：需新开扩展点

| # | 插件 | 新增接口 | 改动量 | 说明 |
|---|------|---------|--------|------|
| C1 | AI 工具插件（歌词翻译、听歌识曲等） | IAgentToolPlugin | 小 | Yuki 已接 OpenAI 兼容 LLM，但 17 个工具在 MauiProgram 硬编码注册；开放注册即可 |
| C2 | 艺人刮削器插件（MusicBrainz/TheAudioDB） | 刮削接口插件化 | 小 | 现有 5 个 IArtistMetadataScraper 实现本就是 DI 多实现形态，转插件成本低 |
| C3 | Scrobble 插件（ListenBrainz/Last.fm） | IPlugin.InitializeAsync(IServiceProvider) 重载 | 小 | 后台型插件当前拿不到 DI 容器，需补带参初始化重载；**但已有暗路可绕**（见五） |

## 五、顺手可修的宿主问题

做插件过程中会遇到的既有缺口（★ = 本次已逐行核实）：

- ★ **`Song.Duration` 单位不一致（疑似 bug）**：`Song.cs:28` 注释「时长（毫秒）」，但 `TagReader.ReadSongInfo` 三处写入（`:33/:64/:111/:143`）全是 `TotalSeconds` / `DurationSeconds`（**秒**）。消费侧：播放页 `AppViewModels.cs:876` 用 `song.Duration > 1000 ? song.Duration / 1000.0 : 0` 做防御——3 分钟的歌（Duration=180）会被判成 0，靠随后 `DurationChanged` 事件修正；`AllSongsViewModel.cs:341` 按 Duration 排序不受单位影响（纯相对）。**统计页不受牵连**：`ListeningStatsViewModel` 用的是 `PlaySession.DurationMs`（毫秒，由 `LogListenSessionAsync` 写入实际收听毫秒），与 Song.Duration 无关。建议统一为毫秒并修掉 `>1000` 猜测。
- ★ **`IAudioPlayerService` 无 `CurrentSongChanged` 事件**（`IAudioPlayerService.cs` 只有 PlaybackStateChanged/PositionChanged/DurationChanged/PlaybackCompleted）。AI DJ、通知栏联动、歌词滚动等都需要「切歌」信号，建议顺手补一个 `event EventHandler<Song?>? CurrentSongChanged`。
- ★ **`PlaySession` 只保留最近 2000 条**：`MusicDatabase.cs:1165` 每次 `LogListenSessionAsync` 后 `TrimPlaySessionAsync(2000)`。做年度报告/Scrobble 落地的**硬约束**——要么插件趁早做（尽早开始累积），要么先改宿主保留策略（按时间窗裁剪而非纯条数）。
- `ShutdownAllAsync` 已定义但全仓无调用点，应用退出不通知插件关闭。
- `OnlineMusicAdapter` 反射代理未覆盖 `LikeSongAsync` / `FmLikeAsync`——跨版本反射加载的插件红心会静默失效。
- `GetPlayUrlAsync(quality)` 音质参数悬空，无选择 UI。
- 无插件 manifest 机制（宿主版本区间/依赖/权限声明），兼容性全靠 AssemblyResolve + 反射适配器兜底。
- `Assembly.Load(bytes)` 进默认 ALC，禁用插件后程序集仍驻留内存，需重启生效。
- Shell 路由全部静态注册，插件无法注册具名路由（只能 PushAsync 实例页面）。
- ★ **后台插件拿 DI 有暗路**：`MauiProgram.cs:14` 的 `public static IServiceProvider Services` 是 public static，scrobble/AI DJ 这类后台插件可 `MauiProgram.Services.GetService<T>()` 自取，不必等 `InitializeAsync(IServiceProvider)` 重载落地（C3 仍建议补重载作为正规路径）。

## 六、建议实施顺序

1. **A1 或 A2**（新音源插件）：立竿见影，模式完全可复用，共享 UI 模板还能反哺网易云插件。
2. **B1 主题皮肤**：宿主接线量小，用户视觉感知最强。
3. **A4 + A5**（LRCLIB 歌词 + 歌单工具箱）：小而美，各自一两天工作量。
4. **C1 AI 工具插件**：Yuki 生态扩展，打开玩法上限。
5. B6 → B2/B3 → B7 按兴趣推进。

## 七、预制插件蓝图（2026-08-11 补充）

> 以下插件设想基于对宿主基础设施的进一步核验（★ = 已逐行确认宿主侧能力就位）。
> 分类沿用宿主现有扩展面：能纯插件实现的（零宿主改动）与需要小改动的分开标注。

### 7.1 长内容与智能播放（播放链路复用）

| # | 插件 | 核心机制 | 宿主依赖 | 难度 |
|---|------|---------|---------|------|
| P1 | 播客 / 有声书插件 | ★ `OnlineSong.DurationMs` 是 `long`（`OnlineSong.cs:30`）、`Internal` 字典（`:36`）可塞剧集号，结构完全承载长内容；订阅/剧集/续播进度**插件自建存储**（宿主无订阅体系）；播放链路直接复用 | 零 | 中 |
| P2 | AI DJ / 智能连播插件 | ★ `PlaybackCompleted` 事件（`IAudioPlayerService.cs:54`）驱动 + ★ `PlayQueue.AddToEnd(Song)`（`PlayQueue.cs:282`）续歌 + Yuki LLM 或音源插件推荐接口（`IOnlineMusicPlugin` 平台搜索），做「听完自动接相似风格」 | 建议补 `CurrentSongChanged`（见五） | 中 |

**接入源**：小宇宙、iTunes RSS（`itunes.apple.com` lookup/lookup 接口）等 RSS/开放源均可做数据层，聚合为 `IOnlineMusicPlugin`。

### 7.2 工具类（宿主目前全空白）

| # | 插件 | 核心机制 | 宿主依赖 | 难度 |
|---|------|---------|---------|------|
| P3 | 下载入库插件（离线收听） | ★ `DownloadManager.EnqueueStream(displayName, sourceId, fileName, streamProvider)`（`DownloadManager.cs:217`）——`streamProvider` 委托本就是给外部注入下载源的口子；★ `TaskUpdated` 事件（`:161`）完成后写 `MusicDatabase` 入库，给在线歌单做「缓存到本地」 | 零 | 中 |
| P4 | 批量标签编辑器 | ★ `TagReader` 写方法是静态类：`WriteMetadata`/`WriteCoverToFile`/`WriteEmbeddedLyrics`（`TagReader.cs:408/427/452`），插件直接调；批量改标签、刮削结果回写、封面嵌入 | 零 | 低 |
| P5 | 音频分析套件（BPM / ReplayGain 响度均衡 / 重复歌曲检测） | 全仓零痕迹。基础设施已就位：Android FFmpeg WAV 管道可给分析取 PCM；★ `IAudioEnhancerPlugin.ProcessSamples` 采样回调（`IPlugin.cs:73`，待接线）。**ReplayGain 播放音量忽大忽小是真实痛点** | B8 式接线（采样管线） | 高 |
| P6 | 听歌识曲插件 | 音频指纹方向：ACRCloud 或开源 chromaprint + AcoustID；同样完全空白 | 零（或 C1 工具化） | 中 |

### 7.3 数据与统计类

| # | 插件 | 核心机制 | 宿主依赖 | 难度 |
|---|------|---------|---------|------|
| P7 | 年度听歌报告 / 分享图 | ★ `PlaySession` 表可从 DI 直调（`MusicDatabase.GetPlaySessionsAsync`）；用发现子 tab（鸭子类型）挂统计卡片或生成分享图 | 零 | 中 |
| P8 | Scrobble 落地（ListenBrainz/Last.fm） | 不用订阅事件，直接消费宿主每次播放已写好的 `PlaySession` 表最省力 | 零（C3 正规化） | 低 |

⚠️ **硬约束**：P7/P8 都受 `PlaySession` 2000 条截断影响（见五）——「年度」深度数据会被截断。**要趁早做，或先改宿主保留策略**。

### 7.4 联动类（性价比高地）

| # | 插件 | 核心机制 | 宿主依赖 | 难度 |
|---|------|---------|---------|------|
| P9 | 自建服务器同步插件 | ★ `ICatClawServerService` 是「幽灵服务」——功能齐全（连接测试/状态/全量元数据同步/搜索/流地址，`ICatClawServerService.cs`）、已注册 DI（`MauiProgram.cs:315`）、但全仓零消费；★ `ServerSettingsPage` 是空壳（`ServerSettingsPage.xaml.cs` 仅 InitializeComponent）。插件从 DI 拿到它设置 `ServerUrl` 就能复活整套 NAS 曲库同步 | 零 | 低 |
| P10 | Subsonic 协议补全 | 现在只用了 ping/search/stream/coverArt/lyrics；协议里的 **scrobble、jukebox（远控其他设备播放）、star 收藏同步、getPlaylists** 全没用——都可做成插件，配合 Navidrome / Airsonic 生态 | 零（或 B2 协议注册化） | 中 |

### 7.5 实施优先级（本批）

1. **P9 自建服务器同步**（低难度 + 幽灵服务白捡，性价比最高）
2. **P3 下载入库** + **P8 Scrobble 落地**（都是「宿主零改动、直连现有基础设施」的短平快）
3. **P1 播客/有声书**（OnlineSong 结构已验证可承载，先做数据层 + 续播进度）
4. **P2 AI DJ**（需先补 `CurrentSongChanged`，或暂用 PlaybackCompleted + 轮询兜底）
5. **P5 ReplayGain** 单独立项（真实痛点，但依赖采样管线接线，排期最长）
