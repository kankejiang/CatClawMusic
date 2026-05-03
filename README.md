# 🐾 猫爪音乐 (CatClaw Music) v1.02

> 📅 首次发布：2026-05-01 · v1.0.0  |  更新：2026-05-03 · v1.0.1  →  2026-05-03 · v1.0.2
>
> 萌系 Android 本地+网络音乐播放器，.NET 9 原生 Android 开发，支持本地播放、Navidrome/Subsonic 网络音乐、WebDAV 远程音乐、频谱可视化、LRC 歌词同步滚动、全屏歌词体验。

![平台](https://img.shields.io/badge/平台-Android-green)
![.NET](https://img.shields.io/badge/.NET-9.0-512bd4)
![语言](https://img.shields.io/badge/C%23-12.0-blue)
![版本](https://img.shields.io/badge/版本-v1.0.2-brightgreen)

***

## 🚀 快速入门

### 本地音乐

1. 打开应用，进入「音乐库」页面
2. 点击「选择文件夹」按钮，通过 SAF 选择你的音乐文件夹
3. 应用会自动扫描并加载音乐（支持增量扫描）
4. 点击歌曲开始播放

### Navidrome 远程音乐

1. 打开侧边栏设置，点击「网络服务」
2. 选择「Navidrome」，填写服务器地址、端口、用户名和密码
3. 启用后，在「音乐库」的「网络」标签页中即可看到远程歌曲

### 全屏歌词

- 在播放页（Tab 1），**左滑**或点击歌词区域可进入全屏歌词页
- **右滑**可返回播放页
- 全屏歌词页支持手动滚动，3 秒后自动恢复同步
- 支持毛玻璃模糊背景，沉浸体验

***

## 🆕 v1.0.1 更新日志（2026-05-03）

### ✨ 新增

- 🎵 **ExoPlayer (Media3) 播放引擎**：原生支持 content:// URI，更好的流媒体兼容性
- 🖼️ **封面懒加载**：滚动到可见时再加载封面，不阻塞列表渲染（支持本地内嵌封面 + 网络 API 下载）
- 📈 **增量式网络扫描**：扫描过程中实时显示进度条，每扫描完一个专辑即更新列表
- 📈 **增量式本地扫描**：与网络扫描一致，每发现 20 首歌曲即增量刷新列表
- 🎤 **全屏歌词页**：NowPlaying 页左滑或点击歌词区域进入，右滑返回
- 🌫️ **毛玻璃歌词背景**：封面图实时模糊（RenderEffect，API 31+），深色遮罩，沉浸式体验
- 📊 **设置页重构**：音乐文件夹 + 远程音乐服务双卡片，远程服务子页含 Navidrome/WebDAV 快捷开关
- 📈 **设置状态预览**：实时显示文件夹数、歌曲数、已启用服务数

### 🐛 修复

- 修复 ViewPager2 切换 Tab 0（全屏歌词）时自动弹回的问题
- 修复歌词区域点击与 ViewPager2 滑动手势冲突
- 修复歌词三行显示截断问题（maxLines 调整 + wrap\_content）
- 修复 NowPlayingViewModel 歌词属性未触发通知的问题
- 修复 Android 13+ 全文件访问权限检测（`Environment.IsExternalStorageManager`）
- 修复某些设备/ROM 上 MediaStore 缺少 `year` / `track` 列导致的崩溃（`GetColumnIndex` 安全检查）
- 修复 `Android.Graphics.Path` 与 `System.IO.Path` 命名冲突编译错误
- 修复 `.NET 10 SDK` 的 `dotnet build` NuGet restore 崩溃（必须使用 VS MSBuild）
- 修复 `CollectionChanged` → `NotifyDataSetChanged` 逐首刷新导致的主线程 ANR（改为 `NotifyItemRangeInserted`）

### 🔧 优化

- **播放引擎迁移**：`Android.Media.MediaPlayer` → `Xamarin.AndroidX.Media3.ExoPlayer`（Media3）
- **增量式架构**：扫描、入库、UI 刷新三阶段全部增量流水线，不再等全部完成
- **封面加载优化**：移除批量预提取 `ExtractCoversAsync`，改为 `SongAdapter` 滚动懒加载 + 磁盘缓存
- **主线程性能**：`SongAdapter.AddRange()` 使用 `NotifyItemRangeInserted`，O(n²) → O(1)
- **SAF 文件夹隔离**：有 SAF URI 时跳过 MediaStore 全设备扫描，只扫用户选择的文件夹
- **去重优化**：`HashSet<string>` 替代 `Any()` 线性搜索
- Tab 顺序调整：全屏歌词(0) / NowPlaying(1) / Playlist(2) / Search(3) / Library(4)
- 全屏歌词页当前行紫色高亮 + 自动滚动，手动滚动暂停 3 秒
- 底部导航栏在歌词页自动隐藏

***

## 💬 交流群

> ₍˄·͈༝·͈˄\*₎◞ ̑̑

**QQ 群：855383639** — 加入猫爪音乐交流群一起讨论喵\~

***

## ✨ 功能特性

### ✅ 已实现

#### 🎵 本地音乐

| 特性                             |  状态 | 说明                                     |
| ------------------------------ | :-: | -------------------------------------- |
| SAF 文件选择器选文件夹                  |  ✅  | 系统文件管理器界面，无需 `MANAGE_EXTERNAL_STORAGE` |
| 传统路径扫描 (`/Music`, `/Download`) |  ✅  | 有权限时后备扫描                               |
| 递归扫描音频文件                       |  ✅  | 支持子目录                                  |
| Tag 信息读取                       |  ✅  | TagLibSharp 解析标题/艺术家/专辑/时长等            |
| 封面图懒加载                         |  ✅  | SongAdapter 滚动时懒加载 + 磁盘缓存，不再批量预提取      |
| 歌曲去重入库                         |  ✅  | 按文件路径去重，已存在则更新                         |
| **增量式扫描（新）**                   |  ✅  | 逐批回调控件 + 入库，列表实时增量刷新，进度条动画             |
| **SAF 文件夹隔离（新）**               |  ✅  | 有 SAF URI 时跳过 MediaStore，只扫用户选择的文件夹    |

#### ▶️ 音频播放

| 特性                       |  状态 | 说明                                                                 |
| ------------------------ | :-: | ------------------------------------------------------------------ |
| 播放/暂停/恢复                 |  ✅  | **ExoPlayer (Media3)**：`AndroidX.Media3.ExoPlayer.SimpleExoPlayer` |
| 上一首/下一首                  |  ✅  | 通过 PlayQueue 管理                                                    |
| 进度拖动 (Seek)              |  ✅  | Material Slider                                                    |
| 音量控制                     |  ✅  | 0-100                                                              |
| WakeLock 后台保活            |  ✅  | 防止播放时 CPU 休眠                                                       |
| 自动播放下一首                  |  ✅  | Completion 事件触发                                                    |
| 流媒体 URL 播放               |  ✅  | 支持 HTTP/HTTPS                                                      |
| **原生 content:// URI（新）** |  ✅  | ExoPlayer 原生支持，无需 `ParcelFileDescriptor` 绕路                        |

#### 🔀 播放队列与模式

| 特性        |  状态 | 说明              |
| --------- | :-: | --------------- |
| 顺序播放      |  ✅  | <br />          |
| 列表循环 (🔁) |  ✅  | <br />          |
| 单曲循环 (🔂) |  ✅  | <br />          |
| 随机播放 (🔀) |  ✅  | Fisher-Yates 洗牌 |
| 播放历史栈     |  ✅  | 支持"上一曲"回溯       |
| 即将播放预览    |  ✅  | 显示接下来 3 首       |

#### 🎹 频谱可视化

| 特性        |  状态 | 说明                       |
| --------- | :-: | ------------------------ |
| 512 点 FFT |  ✅  | Cooley-Tukey + 汉宁窗       |
| A 加权      |  ✅  | 等响度曲线修正                  |
| 32 段对数频段  |  ✅  | 30Hz\~16kHz              |
| 包络动效      |  ✅  | attack 0.7 / release 0.5 |
| 峰值保持      |  ✅  | 600ms 慢落                 |
| AGC       |  ✅  | 参考电平归一化                  |
| 自适应采样率    |  ✅  | 44.1k/48k/96kHz          |

#### 🎶 歌词

| 特性                  |  状态 | 说明                                          |
| ------------------- | :-: | ------------------------------------------- |
| LRC 格式解析            |  ✅  | 兼容 `[mm:ss.xx]` / `[mm:ss.xxx]` / `[mm:ss]` |
| 时间轴同步高亮             |  ✅  | 三行滚动（上一行/当前行/下一行）                           |
| 歌词缓存到 DB            |  ✅  | Lyric 表持久化                                  |
| content:// URI 歌词读取 |  ✅  | 通过 ContentResolver                          |

#### 💚 收藏与播放历史

| 特性        |  状态 | 说明             |
| --------- | :-: | -------------- |
| ♥ 添加/移除收藏 |  ✅  | 实时写入 SQLite    |
| 收藏列表查看    |  ✅  | 播放列表→收藏歌曲 tab  |
| 切歌时同步收藏状态 |  ✅  | 从 DB 读取避免残留    |
| 自动记录播放历史  |  ✅  | 每次播放记录         |
| 历史去重计次    |  ✅  | 同一首歌只保留一条，递增计数 |
| 仅保留 20 条  |  ✅  | 超出自动清理         |
| 按播放时间排序   |  ✅  | 最近优先           |

#### ☁️ 网络协议

| 特性                           |   状态   | 说明                                            |
| ---------------------------- | :----: | --------------------------------------------- |
| **Navidrome (Subsonic API)** | <br /> | <br />                                        |
| Ping 连接测试                    |    ✅   | `ping.view`                                   |
| 获取全部歌曲                       |    ✅   | `search3.view` 批量获取，**增量式回调**：每专辑完成后立即入库+刷新列表 |
| 专辑列表                         |    ✅   | `getAlbumList2.view`                          |
| 封面图                          |    ✅   | `getCoverArt.view`，**懒加载**：列表滚动时按需下载          |
| 歌词获取                         |    ✅   | `getLyricsBySongId` (OpenSubsonic 结构化歌词)      |
| 本地/网络歌曲隔离                    |    ✅   | Tab 切换时清空，按 Source 过滤                         |
| 流媒体 URL                      |    ✅   | `stream.view`                                 |
| Token 认证                     |    ✅   | md5+salt                                      |
| 配置持久化                        |    ✅   | 主机/端口/用户/密码/HTTPS                             |
| 扫描进度条                        |    ✅   | `IProgress` 实时报告扫描进度                          |

#### 📋 页面功能

| 特性                |   状态   | 说明                        |
| ----------------- | :----: | ------------------------- |
| **播放列表页**         | <br /> | <br />                    |
| 全部歌曲列表            |    ✅   | 本地+网络去重合并，本地优先            |
| 收藏歌曲列表            |    ✅   | <br />                    |
| 最近播放列表            |    ✅   | <br />                    |
| Tab 切换 (全部/收藏/最近) |    ✅   | <br />                    |
| 点击歌曲播放            |    ✅   | 设置队列+播放+同步迷你播放器           |
| **音乐库页**          | <br /> | <br />                    |
| 本地音乐列表            |    ✅   | 从 SQLite 加载，增量式每 50 首一批刷新 |
| 网络音乐列表            |    ✅   | 加载所有启用的网络配置，增量式逐专辑刷新      |
| 本地/网络 Tab 切换      |    ✅   | <br />                    |
| SAF 文件夹选择引导       |    ✅   | <br />                    |
| **扫描进度条（新）**      |    ✅   | 实时显示扫描进度和状态               |
| **搜索页**           | <br /> | <br />                    |
| 多字段搜索             |    ✅   | 标题/艺术家/专辑                 |
| **全屏播放器**         | <br /> | <br />                    |
| 大尺寸专辑封面           |    ✅   | 渐变色背景                     |
| 歌曲信息              |    ✅   | 标题/艺术家                    |
| 歌词三行滚动            |    ✅   | 上/当前/下                    |
| 进度滑块              |    ✅   | <br />                    |
| 收藏按钮              |    ✅   | ♥ 状态切换                    |
| 播放模式循环            |    ✅   | 🔁→🔂→🔀                  |
| 频谱可视化             |    ✅   | <br />                    |
| 上一首/下一首/暂停        |    ✅   | <br />                    |
| **迷你播放器**         | <br /> | <br />                    |
| 底部迷你条             |    ✅   | MaterialCardView 毛玻璃      |
| 封面/标题/艺术家         |    ✅   | <br />                    |
| 播放/暂停/上/下         |    ✅   | <br />                    |
| 进度指示条             |    ✅   | 500ms 更新                  |
| 点击跳转全屏            |    ✅   | <br />                    |

#### 🎨 UI / UX

| 特性                  |  状态 | 说明               |
| ------------------- | :-: | ---------------- |
| BottomNavigation 导航 |  ✅  | 4 tab：播放/列表/搜索/库 |
| ViewPager2 滑动切换     |  ✅  | <br />           |
| 播放页沉浸模式             |  ✅  | 隐藏工具栏+导航栏        |
| 状态栏适配               |  ✅  | FitSystemBars    |
| 侧滑设置面板              |  ✅  | 手势/点击遮罩关闭        |
| 自定义桌面图标             |  ✅  | <br />           |
| Material3 紫粉主题      |  ✅  | <br />           |
| 闪屏主题                |  ✅  | <br />           |

#### 🗄️ 数据库 (SQLite)

| 表                             |  状态 |
| ----------------------------- | :-: |
| Songs (含 ArtistId/AlbumId 索引) |  ✅  |
| Artists                       |  ✅  |
| Albums                        |  ✅  |
| Favorites                     |  ✅  |
| PlayHistory (含 PlayCount)     |  ✅  |
| ConnectionProfiles            |  ✅  |
| CachedSongs                   |  ✅  |
| Lyrics                        |  ✅  |
| Playlists                     |  ✅  |
| PlaylistSongs                 |  ✅  |

#### 🏗️ 架构

| 特性                           |                                状态                                |
| ---------------------------- | :--------------------------------------------------------------: |
| DI 容器 (IServiceProvider)     |                                 ✅                                |
| MVVM (CommunityToolkit.Mvvm) |                                 ✅                                |
| **ExoPlayer (Media3) 播放引擎**  |                  ✅ 替代 Android.Media.MediaPlayer                  |
| 增量式扫描架构                      |       ✅ `IProgress` + `Func<List<Song>,Task>` 回调，逐批入库+增量 UI      |
| 封面懒加载                        |              ✅ SongAdapter 内部懒加载，`_boundSongId` 防错位              |
| 适配器增量刷新                      | ✅ `NotifyItemRangeInserted` 替代 `NotifyDataSetChanged`，O(n²)→O(1) |
| 数据库 WAL 模式                   |                                 ✅                                |

### 🔧 未完成功能

#### P1 — 重要功能缺失

| 特性                |  优先级  | 说明                                                             |
| ----------------- | :---: | -------------------------------------------------------------- |
| WebDAV 音频扫描入库     | 🟠 P1 | `ScanWebDavAsync` 返回空列表，PROPFIND/文件读取等底层操作已实现但未完成音频扫描、入库、播放全流程 |
| 自定义播放列表 (创建/编辑)   | 🟠 P1 | 数据表已建但无 UI 交互                                                  |
| 缓存下载 (CachedSong) | 🟠 P1 | 表已建、清缓存按钮已有，但下载逻辑未实现                                           |
| 通知栏媒体控制           | 🟠 P1 | 未创建 MediaStyle Notification                                    |
| 锁屏控制              | 🟠 P1 | 依赖通知栏控制                                                        |
| Album/Artist 网格浏览 | 🟠 P1 | 数据层方法存在但无 UI                                                   |
| 歌曲长按菜单            | 🟠 P1 | SongAdapter 未实现 Context Menu                                   |
| HomeFragment 首页   | 🟠 P1 | 文件存在但未注册使用                                                     |
| 播放列表搜索            | 🟠 P1 | 搜索在单独 tab 中                                                    |

#### P2 — 增强功能

| 特性                 |  优先级  | 说明                       |
| ------------------ | :---: | ------------------------ |
| 深色模式               | 🟡 P2 | 仅明亮主题                    |
| 均衡器 (Equalizer)    | 🟡 P2 | Android AudioEffect 未集成  |
| 睡眠定时器              | 🟡 P2 | <br />                   |
| 专辑封面墙浏览            | 🟡 P2 | <br />                   |
| 桌面小部件 (App Widget) | 🟡 P2 | <br />                   |
| Android Auto       | 🟡 P2 | <br />                   |
| MediaSession 集成    | 🟡 P2 | 蓝牙/穿戴设备控制                |
| 耳机按键控制             | 🟡 P2 | <br />                   |
| 歌曲元数据编辑            | 🟡 P2 | 无 ID3 标签编辑               |
| **状态栏歌词**          | 🟡 P2 | 播放时在 Android 状态栏滚动显示当前歌词 |
| **桌面歌词 (悬浮窗)**     | 🟡 P2 | 桌面悬浮歌词窗口，可拖动/缩放/锁定位置     |
| **锁屏控件**           | 🟠 P1 | 锁屏界面显示封面/标题/控制按钮         |
| **通知栏控件**          | 🟠 P1 | 下拉通知栏显示播放控制、进度条、封面       |
| **音频震动**           | 🟡 P2 | 跟随音乐节奏/低频产生触觉反馈（振动马达）    |

#### P3 — 协议扩展

| 特性      |  优先级  | 说明    |
| ------- | :---: | ----- |
| SMB 协议  | 🔵 P3 | 枚举已定义 |
| FTP 协议  | 🔵 P3 | 枚举已定义 |
| DLNA 协议 | 🔵 P3 | 枚举已定义 |
| NFS 协议  | 🔵 P3 | 枚举已定义 |

***

## 🏗 项目结构

```
CatClawMusic/
├── CatClawMusic.Core/              # 核心库
│   ├── Models/                     # Song/Album/Playlist/Lyrics/ConnectionProfile
│   ├── Interfaces/                 # IAudioPlayerService/ILyricsService/PlayQueue
│   └── Services/                   # PlayQueue/LyricsService/TagReader/MusicUtility
│
├── CatClawMusic.UI/                # UI 层（原生 Xamarin.Android）
│   ├── Fragments/                  # NowPlayingFragment/SettingsFragment/LibraryFragment
│   ├── Views/                      # SpectrumView（自定义绘制频谱）
│   ├── ViewModels/                 # NowPlayingViewModel/LibraryViewModel/SettingsViewModel
│   ├── Services/                   # PlaybackStateManager/PositionSyncedSpectrum/FftAnalyzer
│   ├── Platforms/Android/          # AudioPlayerService/PermissionService/MainActivity
│   └── Resources/                  # 图标/字体/布局/配色
│
└── CatClawMusic.sln
```

***

## 🎹 频谱引擎

| 模块      | 实现                                                  |
| ------- | --------------------------------------------------- |
| **采集**  | MediaCodec 流式解码 → seek+flush 定位到播放位置 → 512 帧 PCM    |
| **FFT** | 512 点 Cooley-Tukey FFT + 汉宁窗                        |
| **加权**  | A 加权等响度曲线修正，匹配人耳感知                                  |
| **频段**  | 30Hz\~16kHz 纯对数 32 段（左密右疏）                          |
| **动效**  | 主柱快上快下（attack 0.7 / release 0.5），背景柱峰值保持 600ms + 慢落 |
| **AGC** | 慢跟踪参考电平归一化，安静段不爆炸                                   |
| **采样率** | 自动适配 44.1k/48k/96kHz                                |

***

## 🎶 歌词引擎

| 模块     | 实现                                                            |
| ------ | ------------------------------------------------------------- |
| **来源** | 嵌入歌词 → 同名 .lrc（SAF）→ Navidrome 远程歌词（OpenSubsonic 结构化 + 纯文本回退） |
| **解析** | 兼容 `[mm:ss.xx]` / `[mm:ss.xxx]` / `[mm:ss]` 格式                |
| **同步** | 500ms 定时器匹配播放位置 → 三行显示（上/当前/下）                                |
| **恢复** | 重启后根据保存位置显示对应歌词行                                              |

***

## 🔧 技术栈

| 层    | 技术                              |
| ---- | ------------------------------- |
| 框架   | .NET 9 (Xamarin.Android)        |
| 语言   | C# 12.0                         |
| 数据库  | SQLite (sqlite-net-pcl)         |
| MVVM | CommunityToolkit.Mvvm           |
| 标签   | TagLibSharp                     |
| 播放器  | **ExoPlayer (AndroidX Media3)** |
| 解码   | Android.Media.MediaCodec (频谱提取) |
| 权限   | SAF (Storage Access Framework)  |

***

## 🧩 计划：插件扩展系统

> 目标：通过轻量插件机制，让猫爪音乐支持更多功能而无需改动核心代码。

### 插件类型

| 类型         | 说明                                     | 示例                   |
| ---------- | -------------------------------------- | -------------------- |
| **协议插件**   | 实现 `INetworkMusicService` 接口，接入新的网络音乐源 | SMB、FTP、DLNA、NFS     |
| **音频引擎插件** | 替换或扩展 `IAudioPlayerService` 实现         | ExoPlayer、VLC engine |
| **封面来源插件** | 实现 `IAlbumCoverProvider`，提供额外封面来源      | 网易云封面、Last.fm        |
| **歌词来源插件** | 实现 `ILyricsProvider`，在线搜索或下载歌词         | QQ 音乐歌词、网易云歌词        |
| **显示插件**   | 扩展歌词/信息的显示方式                           | 状态栏歌词、桌面悬浮歌词         |
| **可视化插件**  | 实现自定义频谱/可视化效果                          | 波形、环形光谱、花朵绽放         |
| **控制插件**   | 扩展系统级播放控制入口                            | 锁屏控件、通知栏控件、桌面小部件     |
| **交互插件**   | 扩展触觉/体感交互                              | 音频震动（节奏触觉反馈）         |
| **工具插件**   | 提供额外的实用功能                              | 睡眠定时器、音乐闹钟、铃声制作      |

### 插件接口设计（草案）

```csharp
/// <summary>
/// 插件基类接口
/// </summary>
public interface IPlugin
{
    string Id { get; }                 // 唯一标识
    string Name { get; }               // 显示名称
    string Version { get; }
    string? IconPath { get; }          // 可选图标
    Task<bool> InitializeAsync();      // 初始化
    Task ShutdownAsync();              // 卸载
}

/// <summary>
/// 协议扩展插件
/// </summary>
public interface INetworkProtocolPlugin : IPlugin
{
    ProtocolType Protocol { get; }
    ConnectionProfile DefaultProfile { get; }
    Task<List<Song>> ScanAsync(ConnectionProfile profile);
    Task<List<Song>> SearchAsync(string keyword, ConnectionProfile profile);
    Task<Stream?> GetCoverAsync(string songId, ConnectionProfile profile);
    Task<string> GetStreamUrlAsync(Song song, ConnectionProfile profile);
}

/// <summary>
/// 歌词来源插件
/// </summary>
public interface ILyricsProviderPlugin : IPlugin
{
    Task<LrcLyrics?> SearchLyricsAsync(string title, string artist, string? album);
    Task<string?> DownloadLyricsTextAsync(string sourceUrl);
}
```

### 插件管理页面（规划）

```
设置 → 插件管理
├── 已安装插件列表
│   ├── 名称 / 版本 / 状态开关
│   └── 卸载按钮
├── 插件市场（浏览可用插件）
│   ├── 远程源（GitHub Releases / 插件仓库 URL）
│   └── 本地导入（.catclaw-plugin 安装包）
└── 插件设置（各插件自定义配置页）
```

### 插件发现与安装

| 方式            | 说明                                          |
| ------------- | ------------------------------------------- |
| **内置插件**      | 编译时集成到 APK，无需额外安装                           |
| **本地导入**      | 通过 `.catclaw-plugin` 安装包（ZIP 格式，内含清单 + 程序集） |
| **远程仓库**      | 从插件仓库索引列表一键下载安装                             |
| **GitHub 直链** | 输入 GitHub Releases URL 自动识别并安装              |

### 优先级排序

|  优先级  | 插件                 | 预计工作量 |
| :---: | ------------------ | :---: |
| 🔴 P0 | 协议插件框架（SMB 作为首个实现） |   中   |
| 🟠 P1 | 在线歌词搜索插件           |   中   |
| 🟠 P1 | 后台下载缓存插件           |   小   |
| 🟠 P1 | 通知栏控件              |   中   |
| 🟠 P1 | 锁屏控件               |   中   |
| 🟡 P2 | 状态栏歌词              |   小   |
| 🟡 P2 | 桌面悬浮歌词             |   中   |
| 🟡 P2 | 音频震动（节奏触觉反馈）       |   小   |
| 🟡 P2 | 均衡器插件              |   中   |
| 🟡 P2 | 睡眠定时器插件            |   小   |
| 🔵 P3 | 封面搜索插件             |   中   |
| 🔵 P3 | 可视化效果包             |   大   |

***

***

## 🔨 构建说明

```bash
# 注意：.NET 10 SDK 有 NuGet restore 兼容性问题
# 必须使用 VS MSBuild，而非 dotnet build

# 构建前需设置 Android SDK 环境变量（build_apk.bat 已自动处理）：
#   ANDROID_HOME = C:\Users\lvjin\AppData\Local\Android\Sdk
#   ANDROID_SDK_ROOT = C:\Users\lvjin\AppData\Local\Android\Sdk

# 方式一：双击 build_apk.bat（推荐）
D:\WorkBuddy\CatClawMusic\build_apk.bat

# 方式二：cmd 下运行
cd D:\WorkBuddy\CatClawMusic
cmd /c build_apk.bat

# APK 输出路径：
# CatClawMusic.UI\bin\Release\net9.0-android\CatClawMusic.UI-Signed.apk
```

***

## 📄 开源协议

MIT License © 2026 CatClaw Music

***

**🐾 猫爪音乐 — 让音乐更可爱 🎵**
