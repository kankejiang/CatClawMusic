# 🐾 猫爪音乐 (CatClaw Music) v1.0

> 📅 首次发布：2026-05-01 · v1.0.0
>
> 萌系 Android 本地+网络音乐播放器，.NET 9 原生 Android 开发，支持本地播放、Navidrome/Subsonic 网络音乐、频谱可视化、LRC 歌词同步滚动。

![平台](https://img.shields.io/badge/平台-Android-green)
![.NET](https://img.shields.io/badge/.NET-9.0-512bd4)
![语言](https://img.shields.io/badge/C%23-12.0-blue)
![版本](https://img.shields.io/badge/版本-v1.0-brightgreen)

---

## 💬 交流群

> ₍˄·͈༝·͈˄*₎◞ ̑̑

**QQ 群：855383639** — 加入猫爪音乐交流群一起讨论喵~

---

## ✨ 功能特性

### ✅ 已实现

#### 🎵 本地音乐

| 特性 | 状态 | 说明 |
|------|:--:|------|
| SAF 文件选择器选文件夹 | ✅ | 系统文件管理器界面，无需 `MANAGE_EXTERNAL_STORAGE` |
| 传统路径扫描 (`/Music`, `/Download`) | ✅ | 有权限时后备扫描 |
| 递归扫描音频文件 | ✅ | 支持子目录 |
| Tag 信息读取 | ✅ | TagLibSharp 解析标题/艺术家/专辑/时长等 |
| 封面图抽取 | ✅ | 嵌入元数据 + content:// URI |
| 歌曲去重入库 | ✅ | 按文件路径去重，已存在则更新 |
| 下拉刷新扫描 | ✅ | 带 0%→100% 进度条 |
| 后台扫描不阻塞 UI | ✅ | Task.Run + UI 线程调度 |

#### ▶️ 音频播放

| 特性 | 状态 | 说明 |
|------|:--:|------|
| 播放/暂停/恢复 | ✅ | Android MediaPlayer |
| 上一首/下一首 | ✅ | 通过 PlayQueue 管理 |
| 进度拖动 (Seek) | ✅ | Material Slider |
| 音量控制 | ✅ | 0-100 |
| WakeLock 后台保活 | ✅ | 防止播放时 CPU 休眠 |
| 自动播放下一首 | ✅ | Completion 事件触发 |
| 流媒体 URL 播放 | ✅ | 支持 HTTP/HTTPS |

#### 🔀 播放队列与模式

| 特性 | 状态 | 说明 |
|------|:--:|------|
| 顺序播放 | ✅ | |
| 列表循环 (🔁) | ✅ | |
| 单曲循环 (🔂) | ✅ | |
| 随机播放 (🔀) | ✅ | Fisher-Yates 洗牌 |
| 播放历史栈 | ✅ | 支持"上一曲"回溯 |
| 即将播放预览 | ✅ | 显示接下来 3 首 |

#### 🎹 频谱可视化

| 特性 | 状态 | 说明 |
|------|:--:|------|
| 512 点 FFT | ✅ | Cooley-Tukey + 汉宁窗 |
| A 加权 | ✅ | 等响度曲线修正 |
| 32 段对数频段 | ✅ | 30Hz~16kHz |
| 包络动效 | ✅ | attack 0.7 / release 0.5 |
| 峰值保持 | ✅ | 600ms 慢落 |
| AGC | ✅ | 参考电平归一化 |
| 自适应采样率 | ✅ | 44.1k/48k/96kHz |

#### 🎶 歌词

| 特性 | 状态 | 说明 |
|------|:--:|------|
| LRC 格式解析 | ✅ | 兼容 `[mm:ss.xx]` / `[mm:ss.xxx]` / `[mm:ss]` |
| 时间轴同步高亮 | ✅ | 三行滚动（上一行/当前行/下一行） |
| 歌词缓存到 DB | ✅ | Lyric 表持久化 |
| content:// URI 歌词读取 | ✅ | 通过 ContentResolver |

#### 💚 收藏与播放历史

| 特性 | 状态 | 说明 |
|------|:--:|------|
| ♥ 添加/移除收藏 | ✅ | 实时写入 SQLite |
| 收藏列表查看 | ✅ | 播放列表→收藏歌曲 tab |
| 切歌时同步收藏状态 | ✅ | 从 DB 读取避免残留 |
| 自动记录播放历史 | ✅ | 每次播放记录 |
| 历史去重计次 | ✅ | 同一首歌只保留一条，递增计数 |
| 仅保留 20 条 | ✅ | 超出自动清理 |
| 按播放时间排序 | ✅ | 最近优先 |

#### ☁️ 网络协议

| 特性 | 状态 | 说明 |
|------|:--:|------|
| **Navidrome (Subsonic API)** | | |
| Ping 连接测试 | ✅ | `ping.view` |
| 获取全部歌曲 | ✅ | `search3.view` 批量获取 |
| 专辑列表 | ✅ | `getAlbumList2.view` |
| 封面图 | ✅ | `getCoverArt.view` |
| 歌词获取 | ✅ | `getLyricsBySongId` (OpenSubsonic 结构化歌词) |
| 本地/网络歌曲隔离 | ✅ | Tab 切换时清空，按 Source 过滤 |
| 流媒体 URL | ✅ | `stream.view` |
| Token 认证 | ✅ | md5+salt |
| 配置持久化 | ✅ | 主机/端口/用户/密码/HTTPS |
| **WebDAV** | | |
| PROPFIND 文件列表 | ✅ | `allprop` 查询 |
| 目录/文件区分 | ✅ | |
| 文件信息获取 | ✅ | 大小/修改时间 |
| 文件流式读取 | ✅ | OpenReadAsync |
| 连接测试 | ✅ | |
| Basic 认证 | ✅ | |
| 自签名证书跳过 | ✅ | |

#### 📋 页面功能

| 特性 | 状态 | 说明 |
|------|:--:|------|
| **播放列表页** | | |
| 全部歌曲列表 | ✅ | 本地+网络去重合并，本地优先 |
| 收藏歌曲列表 | ✅ | |
| 最近播放列表 | ✅ | |
| Tab 切换 (全部/收藏/最近) | ✅ | |
| 点击歌曲播放 | ✅ | 设置队列+播放+同步迷你播放器 |
| **音乐库页** | | |
| 本地音乐列表 | ✅ | 从 SQLite 加载 |
| 网络音乐列表 | ✅ | 加载所有启用的网络配置 |
| 本地/网络 Tab 切换 | ✅ | |
| SAF 文件夹选择引导 | ✅ | |
| **搜索页** | | |
| 多字段搜索 | ✅ | 标题/艺术家/专辑 |
| **全屏播放器** | | |
| 大尺寸专辑封面 | ✅ | 渐变色背景 |
| 歌曲信息 | ✅ | 标题/艺术家 |
| 歌词三行滚动 | ✅ | 上/当前/下 |
| 进度滑块 | ✅ | |
| 收藏按钮 | ✅ | ♥ 状态切换 |
| 播放模式循环 | ✅ | 🔁→🔂→🔀 |
| 频谱可视化 | ✅ | |
| 上一首/下一首/暂停 | ✅ | |
| **迷你播放器** | | |
| 底部迷你条 | ✅ | MaterialCardView 毛玻璃 |
| 封面/标题/艺术家 | ✅ | |
| 播放/暂停/上/下 | ✅ | |
| 进度指示条 | ✅ | 500ms 更新 |
| 点击跳转全屏 | ✅ | |

#### 🎨 UI / UX

| 特性 | 状态 | 说明 |
|------|:--:|------|
| BottomNavigation 导航 | ✅ | 4 tab：播放/列表/搜索/库 |
| ViewPager2 滑动切换 | ✅ | |
| 播放页沉浸模式 | ✅ | 隐藏工具栏+导航栏 |
| 状态栏适配 | ✅ | FitSystemBars |
| 侧滑设置面板 | ✅ | 手势/点击遮罩关闭 |
| 自定义桌面图标 | ✅ | |
| Material3 紫粉主题 | ✅ | |
| 闪屏主题 | ✅ | |

#### 🗄️ 数据库 (SQLite)

| 表 | 状态 |
|----|:----:|
| Songs (含 ArtistId/AlbumId 索引) | ✅ |
| Artists | ✅ |
| Albums | ✅ |
| Favorites | ✅ |
| PlayHistory (含 PlayCount) | ✅ |
| ConnectionProfiles | ✅ |
| CachedSongs | ✅ |
| Lyrics | ✅ |
| Playlists | ✅ |
| PlaylistSongs | ✅ |

#### 🏗️ 架构

| 特性 | 状态 |
|------|:----:|
| DI 容器 (IServiceProvider) | ✅ |
| MVVM (CommunityToolkit.Mvvm) | ✅ |
| 数据库 WAL 模式 | ✅ |
| 批量预加载避免 N+1 | ✅ |
| 播放状态恢复 | ✅ |

### 🔧 未完成功能

#### P1 — 重要功能缺失

| 特性 | 优先级 | 说明 |
|------|:------:|------|
| WebDAV 音频扫描入库 | 🟠 P1 | `ScanWebDavAsync` 返回空列表，WebDAV 文件操作已实现但未递归扫描音频文件入库 |
| 自定义播放列表 (创建/编辑) | 🟠 P1 | 数据表已建但无 UI 交互 |
| 缓存下载 (CachedSong) | 🟠 P1 | 表已建、清缓存按钮已有，但下载逻辑未实现 |
| 通知栏媒体控制 | 🟠 P1 | 未创建 MediaStyle Notification |
| 锁屏控制 | 🟠 P1 | 依赖通知栏控制 |
| Album/Artist 网格浏览 | 🟠 P1 | 数据层方法存在但无 UI |
| 歌曲长按菜单 | 🟠 P1 | SongAdapter 未实现 Context Menu |
| HomeFragment 首页 | 🟠 P1 | 文件存在但未注册使用 |
| 播放列表搜索 | 🟠 P1 | 搜索在单独 tab 中 |

#### P2 — 增强功能

| 特性 | 优先级 | 说明 |
|------|:------:|------|
| 深色模式 | 🟡 P2 | 仅明亮主题 |
| 均衡器 (Equalizer) | 🟡 P2 | Android AudioEffect 未集成 |
| 睡眠定时器 | 🟡 P2 | |
| 专辑封面墙浏览 | 🟡 P2 | |
| 桌面小部件 (App Widget) | 🟡 P2 | |
| Android Auto | 🟡 P2 | |
| MediaSession 集成 | 🟡 P2 | 蓝牙/穿戴设备控制 |
| 耳机按键控制 | 🟡 P2 | |
| 歌曲元数据编辑 | 🟡 P2 | 无 ID3 标签编辑 |
| **状态栏歌词** | 🟡 P2 | 播放时在 Android 状态栏滚动显示当前歌词 |
| **桌面歌词 (悬浮窗)** | 🟡 P2 | 桌面悬浮歌词窗口，可拖动/缩放/锁定位置 |
| **锁屏控件** | 🟠 P1 | 锁屏界面显示封面/标题/控制按钮 |
| **通知栏控件** | 🟠 P1 | 下拉通知栏显示播放控制、进度条、封面 |
| **音频震动** | 🟡 P2 | 跟随音乐节奏/低频产生触觉反馈（振动马达） |

#### P3 — 协议扩展

| 特性 | 优先级 | 说明 |
|------|:------:|------|
| SMB 协议 | 🔵 P3 | 枚举已定义 |
| FTP 协议 | 🔵 P3 | 枚举已定义 |
| DLNA 协议 | 🔵 P3 | 枚举已定义 |
| NFS 协议 | 🔵 P3 | 枚举已定义 |

---

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

---

## 🎹 频谱引擎

| 模块 | 实现 |
|------|------|
| **采集** | MediaCodec 流式解码 → seek+flush 定位到播放位置 → 512 帧 PCM |
| **FFT** | 512 点 Cooley-Tukey FFT + 汉宁窗 |
| **加权** | A 加权等响度曲线修正，匹配人耳感知 |
| **频段** | 30Hz~16kHz 纯对数 32 段（左密右疏） |
| **动效** | 主柱快上快下（attack 0.7 / release 0.5），背景柱峰值保持 600ms + 慢落 |
| **AGC** | 慢跟踪参考电平归一化，安静段不爆炸 |
| **采样率** | 自动适配 44.1k/48k/96kHz |

---

## 🎶 歌词引擎

| 模块 | 实现 |
|------|------|
| **来源** | 嵌入歌词 → 同名 .lrc（SAF）→ Navidrome 远程歌词（OpenSubsonic 结构化 + 纯文本回退） |
| **解析** | 兼容 `[mm:ss.xx]` / `[mm:ss.xxx]` / `[mm:ss]` 格式 |
| **同步** | 500ms 定时器匹配播放位置 → 三行显示（上/当前/下） |
| **恢复** | 重启后根据保存位置显示对应歌词行 |

---

## 🔧 技术栈

| 层 | 技术 |
|---|---|
| 框架 | .NET 9 (Xamarin.Android) |
| 语言 | C# 12.0 |
| 数据库 | SQLite (sqlite-net-pcl) |
| MVVM | CommunityToolkit.Mvvm |
| 标签 | TagLibSharp |
| 播放器 | Android.Media.MediaPlayer |
| 解码 | Android.Media.MediaCodec |
| 权限 | SAF (Storage Access Framework) |

---

## 🧩 计划：插件扩展系统

> 目标：通过轻量插件机制，让猫爪音乐支持更多功能而无需改动核心代码。

### 插件类型

| 类型 | 说明 | 示例 |
|------|------|------|
| **协议插件** | 实现 `INetworkMusicService` 接口，接入新的网络音乐源 | SMB、FTP、DLNA、NFS |
| **音频引擎插件** | 替换或扩展 `IAudioPlayerService` 实现 | ExoPlayer、VLC engine |
| **封面来源插件** | 实现 `IAlbumCoverProvider`，提供额外封面来源 | 网易云封面、Last.fm |
| **歌词来源插件** | 实现 `ILyricsProvider`，在线搜索或下载歌词 | QQ 音乐歌词、网易云歌词 |
| **显示插件** | 扩展歌词/信息的显示方式 | 状态栏歌词、桌面悬浮歌词 |
| **可视化插件** | 实现自定义频谱/可视化效果 | 波形、环形光谱、花朵绽放 |
| **控制插件** | 扩展系统级播放控制入口 | 锁屏控件、通知栏控件、桌面小部件 |
| **交互插件** | 扩展触觉/体感交互 | 音频震动（节奏触觉反馈） |
| **工具插件** | 提供额外的实用功能 | 睡眠定时器、音乐闹钟、铃声制作 |

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

| 方式 | 说明 |
|------|------|
| **内置插件** | 编译时集成到 APK，无需额外安装 |
| **本地导入** | 通过 `.catclaw-plugin` 安装包（ZIP 格式，内含清单 + 程序集） |
| **远程仓库** | 从插件仓库索引列表一键下载安装 |
| **GitHub 直链** | 输入 GitHub Releases URL 自动识别并安装 |

### 优先级排序

| 优先级 | 插件 | 预计工作量 |
|:------:|------|:----------:|
| 🔴 P0 | 协议插件框架（SMB 作为首个实现） | 中 |
| 🟠 P1 | 在线歌词搜索插件 | 中 |
| 🟠 P1 | 后台下载缓存插件 | 小 |
| 🟠 P1 | 通知栏控件 | 中 |
| 🟠 P1 | 锁屏控件 | 中 |
| 🟡 P2 | 状态栏歌词 | 小 |
| 🟡 P2 | 桌面悬浮歌词 | 中 |
| 🟡 P2 | 音频震动（节奏触觉反馈） | 小 |
| 🟡 P2 | 均衡器插件 | 中 |
| 🟡 P2 | 睡眠定时器插件 | 小 |
| 🔵 P3 | 封面搜索插件 | 中 |
| 🔵 P3 | 可视化效果包 | 大 |

---

------

## 🔨 构建说明

```bash
# 注意：.NET 10 SDK 有 NuGet restore 兼容性问题
# 必须使用 VS MSBuild，而非 dotnet build

# 方式一：双击 build_apk.bat（推荐）
D:\WorkBuddy\CatClawMusic\build_apk.bat

# 方式二：cmd 下运行
cd D:\WorkBuddy\CatClawMusic
cmd /c build_apk.bat

# APK 输出路径：
# CatClawMusic.UI\bin\Release\net9.0-android\CatClawMusic.UI-Signed.apk
```

---

## 📄 开源协议

MIT License © 2026 CatClaw Music

---

**🐾 猫爪音乐 — 让音乐更可爱 🎵**
