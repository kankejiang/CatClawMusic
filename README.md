<img src="CatClawMusic.Maui/Resources/Images/app_icon.png" width="180" alt="CatClawMusic" align="right" />

<div align="center">

# 猫爪音乐 CatClawMusic

_Modern cross-platform music player built with .NET MAUI._

> 宿主是空壳、能力靠插件 —— 播放、歌词、AI 助手，都给你安排好啦 🐾

[![Release](https://img.shields.io/github/v/release/kankejiang/CatClawMusic)](https://github.com/kankejiang/CatClawMusic/releases/latest)
[![License](https://img.shields.io/github/license/kankejiang/CatClawMusic)](./LICENSE.txt)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Android-blue)](https://github.com/kankejiang/CatClawMusic/releases/latest)

</div>

---

## Welcome

- 猫爪音乐是基于 .NET MAUI 的跨平台音乐播放器（Windows 桌面 / Android）
  - A modern cross-platform music player built with .NET MAUI

## Feature

- **插件化架构**
  - 宿主是空壳、能力靠插件：不内置任何 `.ccp` 音源插件，搜索 / 歌单 / 排行榜 / 播放直链 / 歌词等在线能力全部由插件扩展；本地音乐库、播放内核、歌词引擎、AI 助手属于宿主自有功能
- **双端播放内核**
  - Android 走 Media3 ExoPlayer（渐进式磁盘缓存、FFmpeg 软解兜底），Windows 走 `Windows.Media.Playback.MediaPlayer`（均衡器切 AudioGraph + Biquad DSP）
- **歌词引擎**
  - 多行歌词：逐字 / 翻译 / 罗马音 / 卡拉OK 高亮；全屏歌词页；桌面歌词双端实现（Android 悬浮窗 / Windows 第二窗口 + 覆盖层）
- **AI 助手 Yuki**
  - 发现页内聊天模式 + Agent 工具循环（24 个内置工具），OpenAI 兼容多模型配置、SSE 流式、工具调用与备用模型回退
- **完善的音乐库**
  - 本地扫描、歌单管理与拖拽排序、多源刮削（网易云 / AI / 豆瓣）、听歌统计、私人漫游 FM
- **远程音乐**
  - Subsonic（含 Navidrome）/ WebDAV / SMB / 猫爪服务器，流式播放与元数据回填
- **下载能力**
  - HTTP 分片断点续传、BitTorrent 下载（MonoTorrent）、FFmpeg 转码、LRU 音频缓存
- **51 种界面语言**

## Quick Start

前往 [Release](https://github.com/kankejiang/CatClawMusic/releases/latest) 页面下载最新版本：

| 文件 | 平台 | 说明 |
|------|------|------|
| `catclaw.music-x.y.z-Setup.exe` | Windows | 安装版（self-contained，无需另装 .NET 运行时） |
| `com.catclaw.music-x.y.z-arm64.apk` | Android | arm64-v8a 签名包 |

**首次使用**：应用本身不带在线音源，先到 **插件管理 → ＋ 添加** 安装音源插件（本地安装 / 网络安装 / 插件商店任选），再开始听歌。

## Link

| QQ Group | [![QQ 交流群](https://img.shields.io/badge/QQ%E7%BE%A4-855383639-blue)](https://qm.qq.com/q/Fhu3IEzqa4) |
|:-:|:-:|
| 相关项目 | [![猫爪影视](https://img.shields.io/badge/%E7%8C%AB%E7%88%AA%E5%BD%B1%E8%A7%86-CatClawVideo-pink)](https://github.com/kankejiang/CatClawVideo) |

使用问题、功能建议、插件开发交流都欢迎进群；也可以在仓库 [Issues](https://github.com/kankejiang/CatClawMusic/issues) 中反馈。

---

## 插件系统

插件是**裸 .NET 程序集**，扩展名 `.ccp`（本质是 DLL）。宿主 `Assembly.Load` 后扫描其中的
`IPlugin` 实现：第一个实例为主插件，其余自动成为子插件。

**契约接口与接线状态**（详细速查表见 `CatClawMusic.Plugins/README.md`）：

| 接口 | 接线状态 | 宿主消费位置 |
| --- | --- | --- |
| `IOnlineMusicPlugin`（搜索 / 歌单 / 直链 / 歌词 / FM / 登录 / 红心） | ✅ | `OnlineMusicAggregator` 聚合搜索、`LyricsService` 歌词路由、`WebViewLoginPage` 浏览器登录、红心同步 |
| `IViewContributorPlugin`（贡献整页入口） | ✅ | 发现页"🧩 扩展"区（`DiscoverPageBase`） |
| 发现子 tab（鸭子类型：`TabTitle` / `TabIcon` / `TabOrder` / `CreateTabView`） | ✅ | `DiscoverPageBase` 反射探测（宿主零 Core 依赖） |
| `ILyricsProviderPlugin`（在线补歌词） | ✅ | `LyricsService` 兜底链 |
| `IMenuContributorPlugin`（歌曲菜单注入项） | ✅ | 播放页更多菜单、下载页完成项菜单 |
| `IQuickEntryPlugin`（发现页 Hero 入口卡） | ✅ | `DesktopDiscoverPage` / `SearchPage` 的 Hero 横滑队列 |
| `IPluginConfigurable`（插件自带配置页） | ⚠️ 部分 | 插件管理页"配置"按钮目前仍**硬编码只对网易云插件**开放 |
| `IThemeProviderPlugin`、`IProtocolProviderPlugin`、`ICoverProviderPlugin`、`IPlayerPagePlugin`、`IAudioVisualizerPlugin`、`IAudioEnhancerPlugin` | ⚠️ | 接口与反射适配器均已就绪，但宿主尚无消费方 |

**安装与分发**

- 应用内：**插件管理 → ＋ 添加 → 本地安装 / 网络安装**（网络安装支持 GitHub / Gitee
  仓库自动取 Release，或直接填 `.ccp` 直链）；另有**插件商店**从
  `CatClawMusic.PluginMarket` 的 `index.json` 拉取清单（jsDelivr 主源 +
  raw.githubusercontent.com 兜底）并按 sha256 校验下载；
- 发布：把 `.ccp` 传到 GitHub Release Assets 即可以上方式分发；
- 插件工程通过 `PackageCcp` 目标在 Release 构建后把 DLL 另存为 `.ccp`；
- `CopyLocalLockFileAssemblies=false`：插件**不携带依赖**，全部由宿主提供，
  因此不要在插件里引入宿主没有的第三方包；
- 宿主统一持有 **JS 运行时（Jint / Acornima）**，插件经 `IJsRuntimeService` 创建脚本引擎，
  不再各自嵌入；插件程序集进默认 ALC，**禁用插件后程序集仍驻留内存，需重启应用才完全卸载**。

**桌面端子页面内嵌**：桌面壳层（`DesktopBlankPage`，无 Shell）通过 `ISubPageHost` /
`SubPageHost` 把插件页面**内嵌到主内容区**（支持多级内嵌栈），不再使用会盖住侧栏与底部播放条的
整窗模态；宿主未注册该抽象时自动回退 Shell / 模态方式。

## 功能详情

应用主界面是一个 5 页横向 ViewPager（**全屏歌词 → 播放页 → 发现页 → 歌单页 → 音乐库页**，
底部 TabBar 对应后 4 页）：Android 用原生 ViewPager2 承载，Windows 走 TranslationX +
懒加载兜底；Windows 桌面另有一套无 Shell 的壳层（`DesktopBlankPage`：左侧栏 + 自定义标题栏 +
主内容区 + 底部播放条）。

### 发现页

- 分类 tab 固定 5 个：**推荐 / 排行 / 歌手 / 专辑 / 统计**；插件可通过鸭子类型
  （`TabTitle` / `TabIcon` / `TabOrder` / `CreateTabView`）在"推荐"右侧插入自己的子 tab；
- 顶部 Hero 横滑队列 = **AI 助手卡**（`🐾 和 Yuki 聊聊`）→ **插件快捷入口卡**
  （`IQuickEntryPlugin`，如"私人漫游"）→ 英雄卡；
- **扩展区**：列出所有提供整页入口的插件（网易云音乐、LX 源音乐、Lrclib…）；
- 竖屏用 `SearchPage`，桌面用 `DesktopDiscoverPage`，共用 `Pages/Base/DiscoverPageBase`。

### AI 助手（Yuki）

- 发现页内的**聊天模式**（`ChatOverlay`，非独立页面），也可从搜索无结果时的"问问 Yuki"、
  设置 → AI 设置进入；聊天记录持久化到 SQLite（载入最近 30 条，超 1000 条裁剪）；
- **Agent 工具循环**：`AgentService` → `AgentToolDispatch`（按 schema 校验参数、结果按
  10 000 字节 / 200 行截断）→ `OpenAiCompatibleLlmClient`（SSE 流式、tool_calls、
  `reasoning_content`、主模型失败按序回退备用模型）；
- **24 个内置工具**（`MauiProgram.cs` 注册）：音乐库 / 歌单类 10 个、播放类 7 个、
  联网 / 下载类 6 个、音源 1 个；只读工具并行执行、写类工具串行；
- **模型配置**：多份 `LlmConfig` 存在 MAUI `Preferences`，内置 10 个 OpenAI 兼容服务商
  （deepseek / modelscope / llamacpp 本地 / zhipu / moonshot / qwen / spark / nvidia /
  opencodego / custom），支持备用模型、`ReasoningEffort`、上下文缓存等参数；
- **记忆与人设**：`ai_memory.json`（最多 50 条，每累计 20 条消息自动抽取）+
  `wordlib.db`（Yuki 人格词库，未配置 LLM 时也能用本地词条回复）；
  上下文预算约 32 000 token，按"段"裁剪并修复 tool / tool_calls 配对；
- **Agent 浏览器**：`browser_open` 在聊天页顶部弹出小窗抓取页面（25 秒超时），不跳页。

### 音乐库

- 本地扫描（标题 / 艺术家 / 专辑 / 时长 / 封面，Android 另有 SAF 扫描路径）、
  按目录与标签浏览，全曲 / 歌手 / 专辑 / 歌单多视图；
- 歌单管理：新建 / 重命名 / 删除、批量移除歌曲、拖拽排序、收藏与播放历史；
- 歌曲上下文菜单：播放 / 下一首播放 / 收藏 / 添加到歌单 / 歌曲信息，
  并追加插件经 `IMenuContributorPlugin` 注入的菜单项；
- 艺术家匹配（`ArtistMatchPage`）：多源刮削补全艺术家资料与照片；
- 远程音乐服务：**Subsonic（含 Navidrome）/ WebDAV / SMB / 猫爪服务器**，
  支持连接测试、目录探测、流式播放与元数据回填（SMB 经本地 HTTP 代理桥接给播放器）；
- 听歌统计（`DonutView` 环形图 + 数据洞察）、私人漫游 FM 模式（音源插件提供）。

### 播放

- 播放队列 4 种模式（顺序 / 随机 / 单曲循环 / 列表循环，默认列表循环），
  随机模式 Fisher-Yates 洗牌并保持当前曲、支持跨重启恢复；上一曲走历史栈；
  **无 gapless**，仅有"提前缓存下一首网络音频"的预缓冲；
- 播放页含封面取色背景、多行歌词引擎、全屏歌词页、横竖屏两套歌词布局；
- 均衡器（5 段原生 / 10 段 FFmpeg 两套预设，±12 dB）与音效页
  （虚拟环绕、低音增强、响度增强、交叉淡变——交叉淡变是单播放器音量淡变模拟）；
- 下载管理（分片 `.part` + HTTP Range 断点续传、并发 1–5、单任务限速、任务持久化）、
  **BitTorrent 下载**（MonoTorrent 3.0.2）、FFmpeg 转码（Android 内置 `libffmpeg.so`）、
  音频缓存（SHA256 键 + 原子改名 + LRU，默认上限 500 MB）。

### 设置

- 外观与个性化：5 种主题色（紫 / 粉 / 蓝 / 橙 / 青）、浅色 / 深色 / 跟随系统、
  背景模式（封面取色 / Monet 壁纸取色 / 自定义图 / 毛玻璃模糊）、启动页设置；
- 歌词设置、本地音乐与音乐文件夹、远程音乐服务、AI 设置与模型管理、服务器设置；
- **插件管理**（安装 / 启用 / 删除 / 配置）、**插件商店**（`PluginMarketPage`）；
- 权限管理、备份与恢复（歌单 / 播放历史 / 收藏 / 艺术家与封面 / LLM 配置 / 聊天记录 /
  AI 记忆，ZIP 打包）、日志查看（`LogPage`）、关于（GitHub Release 检查更新）。

## 技术概览

| | |
| --- | --- |
| 版本 | **1.8.13**（Android versionCode 70） |
| 应用 ID | `com.catclaw.music`（Debug 为 `com.catclaw.music.debug`，可与 Release 共存） |
| 平台 | Windows `net11.0-windows10.0.19041.0`（最低 10.0.17763.0）、Android `net11.0-android`（最低 API 31、目标 API 36，arm64 / x64） |
| 运行时 | **.NET 11 RC1**：SDK `11.0.100-rc.1.26425.128`（见 `global.json`），C# 15 |
| UI / MVVM | .NET MAUI（`Microsoft.Maui.Controls` 由 .NET 11 RC1 工作负载提供 `$(MauiVersion)`，当前为 11.0.0-rc.1.26451.6）、CommunityToolkit.Mvvm 8.4.2 |
| 本地化 | 51 种界面语言（50 个本地化 `.resx` + `zh-CN` 中性资源） |
| 仓库依赖 | 需与 [`CatClaw.Shared`](#相关仓库) **平级放置**（本解决方案以相对路径引用它） |

## 仓库结构

解决方案文件为 `CatClawMusic.sln`（另有新格式 `CatClawMusic.slnx`），共 **6 个工程**，
其中 2 个来自平级的 `CatClaw.Shared` 仓库：

| 工程 / 目录 | 说明 |
| --- | --- |
| `CatClawMusic.Core` | **契约与公共层**：`Interfaces/`（29 个契约接口 + `Log.cs`）、`Models/`（26 个模型）、`Services/`（`PluginManager`、`PluginAdapters`、`OnlineMusicAggregator`、`PlayQueue`、`LyricsService` 及 LRC/TTML/AMLL/KRC 解析、`TagReader`、`M4aMetadataReader`、`JsRuntimeService`）、`Services/AI/`（15 个文件的 AI Agent 子系统）、`ColorQuantize/`（封面取色） |
| `CatClawMusic.Data` | **数据与服务实现层**（38 个文件）：`MusicDatabase`（SQLite，`catclaw.db`，**17 张表**，按 9 个 partial 拆分）、`MusicLibraryService`、`MusicScanner`、`NetworkMusicService`、`SubsonicService`、`WebDavService`、`SmbService`、`CatClawServerClient`、`BackupService`、多源刮削（豆瓣 / 百度百科 / 网易云 / AI 艺术家 / 多源照片） |
| `CatClawMusic.Maui` | **宿主应用**：`Pages/`（65 个 `.cs` + 53 个 `.xaml`）、`ViewModels/`（44 个）、`Services/`（52 个）、`Controls/`（35 个自绘控件 + 11 个 XAML）、`Converters/`（18 个）、`Platforms/`（41 个） |
| `tests/CatClawMusic.Data.Tests` | 数据层单元测试（xUnit，4 个测试类 / 24 个 `[Fact]`） |
| `CatClaw.Shared.Core` / `CatClaw.Shared.Maui` | **猫爪家族共享库**（来自 `..\CatClaw.Shared`，与猫爪影视共用）：`JsRuntimeServiceBase`、`DownloadStatusConverter` 等实现基类；**插件 SDK 接口不下沉**，仍留在各宿主 `Core/Interfaces` |
| `CatClawMusic.Plugins` | **插件开发模板与指南**（`Template/` + `README.md`，写插件从这里开始） |
| `vitrum-src` | VitrumMAUI 控件库源码（GPU 毛玻璃 / 背景模糊） |
| `tools/` | 辅助工具（`LyricExtractor`、`headparse-test`） |
| `docs/` `design/` `release/` | 设计文档、设计稿与出包产物 |

> `docs/` 下除 `discover-page-design-spec.md` 外均为**历史文档**，不代表当前实现。

## 构建与发布

**Windows（桌面 x64 self-contained 安装包）**

```powershell
.\build-win-release.ps1
```

流程：`dotnet publish` 绿色目录 → Inno Setup 编译安装程序。产物
`release\windows\catclaw.music-<版本>-Setup.exe`（需安装 Inno Setup 7 的 `ISCC.exe`）。

**Android（签名 Release APK）**

```powershell
.\build-release.ps1     # arm64 真机包
.\build-x64.ps1         # x64 模拟器包
```

产物为 `CatClawMusic.Maui\bin\Release\net11.0-android\com.catclaw.music-Signed.apk`；
对外发布时复制为 `release\android\com.catclaw.music-<版本>-arm64.apk`。等价的手工命令：

```powershell
dotnet publish CatClawMusic.Maui\CatClawMusic.Maui.csproj -c Release -f net11.0-android `
  -p:ReleaseAbi=arm64 -p:IntermediateOutputPath=obj/relfix/ -p:OutputPath=bin/relfix/ `
  -p:AndroidSdkDirectory="<Android SDK>" -p:JavaSdkDirectory="<JDK 21>" `
  -p:AndroidKeyStore=true -p:AndroidSigningKeyStore="catclaw.keystore" `
  -p:AndroidSigningKeyAlias=catclaw -p:AndroidSigningKeyPass=catclaw123 -p:AndroidSigningStorePass=catclaw123 -m:1
```

> - `-m:1` 必带：否则打包阶段并发写同一 `obj` 目录会失败。
> - `ReleaseAbi` 让 `RuntimeIdentifiers` 收敛为单一 ABI，避免 FFmpeg 原生库在双 ABI 包里重复约 20 MB。
> - 签名密钥为根目录 `catclaw.keystore`（别名 `catclaw`），已在 csproj 中配置，
>   Debug 与 Release 用同一签名以便交替安装。

## 数据目录

| 平台 | 位置 |
| --- | --- |
| Windows | `%LOCALAPPDATA%\CatClawMusic.Maui\CatClawMusic.Maui\Data\`（MAUI `FileSystem.AppDataDirectory`），缓存另在 `...\Cache\` |
| Android | 应用私有目录 `/data/data/com.catclaw.music/files`（非 root 不可直接读写） |

`Data\` 下主要有：`catclaw.db`（SQLite）、`Plugin\`（`.ccp` 插件与 `installed.json` 索引）、
`ai_memory.json`（AI 记忆）、`wordlib.db`（Yuki 词库）、封面缓存、`bt`（BT 缓存）、
`backups`、日志与崩溃转储。

## 已知缺口与开发注意事项

- **`Song.Duration` 契约统一为「秒」**，插件消费侧仍建议防御 `> 1000 ? ms : s`；
- `IAudioPlayerService` **没有** `CurrentSongChanged` 事件，做切歌联动需监听
  `PlaybackCompleted` + 轮询兜底；
- 播放**无 gapless**；交叉淡变是音量淡变模拟；
- Windows **SMTC 已禁用**（`CommandManager.IsEnabled=false`），系统媒体控制面板不生效；
- 响度只有**分析**能力（仅 Android 经 FFmpeg 实现），ReplayGain 批量写入尚未实现；
- `PlaySession` 仅保留最近 **2000** 条；
- 插件**无 manifest**（版本区间 / 依赖声明），兼容性靠反射适配器兜底 ——
  插件请始终引用与宿主**同一份** `Core` 工程；
- 本仓库需与 `CatClaw.Shared` 平级放置，否则跨仓库相对路径引用会还原 / 构建失败；
- AI 助手的 `MusicSourceRegistry.DefaultSources()` **内置了酷我 / 网易云的接口配置**
  （供下载工具直连），因此"宿主不内置任何在线音源"只对 **`.ccp` 插件音源体系**成立；
- 单元测试仅覆盖数据层（24 个用例），UI 与播放链路无自动化测试。

## 相关仓库

音源与能力插件、插件市场、服务端、共享库均为独立仓库、独立发布
（`CatClaw.Shared` 必须以平级目录存在）：

| 仓库 | 说明 |
| --- | --- |
| [`CatClawMusic.Plugins.Netease`](https://github.com/kankejiang/CatClawMusic.Plugins.Netease) | 网易云音乐音源插件（`netEaseMusic` 0.4.0） |
| [`CatClawMusic.Plugins.LxSource`](https://github.com/kankejiang/CatClawMusic.Plugins.LxSource) | LX 源脚本引擎插件（`lxSource` 0.4.4）：运行 lx-music 自定义源 `.js`，接入网易云 / QQ / 酷我 / 酷狗 |
| [`CatClawMusic.Plugins.Lrclib`](https://github.com/kankejiang/CatClawMusic.Plugins.Lrclib) | LRCLIB / Lyrico 多源在线歌词插件（`lrclib` 1.2.0.9） |
| [`CatClawMusic.PluginMarket`](https://github.com/kankejiang/CatClawMusic.PluginMarket) | 插件市场**清单**仓库（非 .NET 工程）：全 GitHub 托管、零服务器 |
| [`catclaw-server`](https://github.com/kankejiang/catclaw-server) | 自托管音乐流媒体服务端（ASP.NET Core + EF Core / SQLite，net11.0），含 **Subsonic 兼容 `/rest/*.view`** |
| `CatClaw.Shared` | 与猫爪影视共用的跨应用共享库（本地平级仓库，暂无远程地址） |
| [`CatClawVideo`](https://github.com/kankejiang/CatClawVideo) | 猫爪影视：基于 .NET MAUI 的跨平台影视播放器，与本项目共用 `CatClaw.Shared` |

## Thanks

- [猫爪影视 CatClawVideo](https://github.com/kankejiang/CatClawVideo) 猫爪家族的影视端，UI 与工程实践一路互相喂招
- [Jint](https://github.com/sebastienros/jint) 让 JS 爬虫与脚本在 C# 里跑起来的 JS 引擎
- [MonoTorrent](https://github.com/alanmcgovern/monotorrent) BitTorrent 内核
- 不过最最重要的，还是需要感谢屏幕前的你哦~

---

## 协议

[MIT](LICENSE.txt) © 2026 kankejiang
