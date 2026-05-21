# 🐾 猫爪音乐 (CatClaw Music)

> 萌系 Android 音乐播放器，.NET 9 + C# 原生开发。支持本地音乐、Navidrome/Subsonic 网络音乐、WebDAV 远程文件、桌面悬浮歌词（可拖拽/锁定/双行KTV）、LRC 歌词同步滚动、全屏歌词体验、音频频谱可视化、睡眠定时、通知栏媒体控制 + MediaSession、播放状态自动保存与恢复。
>
> 📢 **QQ 交流群**: [855383639](https://qm.qq.com/q/Fhu3IEzqa4) 点击链接加入群聊【₍˄·͈༝·͈˄*₎◞ ̑̑】

<div align="center">

![平台](https://img.shields.io/badge/平台-Android-green)
![.NET](https://img.shields.io/badge/.NET-9.0-512bd4)
![语言](https://img.shields.io/badge/C%23-12.0-blue)
![最低版本](https://img.shields.io/badge/最低版本-Android%2012%20\(API%2031\)-orange)
![协议](https://img.shields.io/badge/协议-MIT-yellow)

</div>

***

## 📱 应用截图

<details>
<summary>🖱️ 点击展开查看应用截图</summary>

<br />

<div align="center">

### 播放页面

![播放页面](images/screenshot-4.jpg "播放页面")

### 歌词页面

![歌词页面](images/screenshot-1.jpg "歌词页面")

### 播放列表

![播放列表](images/screenshot-2.jpg "播放列表")

### 音乐库

![音乐库](images/screenshot-3.jpg "音乐库")

</div>

</details>

***

## 🏗️ 项目架构

```
CatClawMusic/
├── CatClawMusic.Core/          # 核心层（接口 + 模型 + 服务）
│   ├── Interfaces/             # 12 个服务接口
│   ├── Models/                 # 11 个数据模型 + 2 枚举
│   └── Services/               # PlayQueue / LyricsService / TagReader / MusicUtility
│
├── CatClawMusic.Data/          # 数据层（数据库 + 网络服务）
│   ├── MusicDatabase.cs        # SQLite（9 表 + 索引 + WAL）
│   ├── SubsonicService.cs      # Navidrome/OpenSubsonic API
│   ├── WebDavService.cs        # WebDAV 协议
│   └── NetworkMusicService.cs  # 网络音乐工厂
│
└── CatClawMusic.UI/            # UI 层（Android 原生界面）
    ├── MainActivity.cs         # ViewPager2 + BottomNav + 侧滑面板 + 迷你播放器
    ├── Fragments/              # 12 个 Fragment
    ├── ViewModels/             # MVVM（CommunityToolkit.Mvvm 源生成器）
    ├── Helpers/                # VisualizerHelper / AudioVisualizerView
    ├── Services/               # 桌面歌词 / 导航 / 播放状态 / 前台服务
    ├── Adapters/               # 歌曲列表 / 播放列表 适配器
    └── Platforms/Android/      # ExoPlayer / SAF / 主题
```

**技术栈**：.NET 9 | C# 12 | AndroidX Media3 ExoPlayer 1.10.0 | CommunityToolkit.Mvvm 8.2.2 | TagLibSharp 2.3.0 | SQLite (sqlite-net-pcl) | Material 3 | Android Visualizer API

***

## ✨ 功能特性

### 🎵 本地音乐

| 特性 | 说明 |
|------|------|
| SAF 文件夹选择 | 系统文件管理器界面，无需 `MANAGE_EXTERNAL_STORAGE` |
| 多文件夹支持 | 管道分隔(\|)存储多个 SAF URI，权限过期自动检测并移除 |
| MediaStore 扫描 | Android 10+ 无需存储权限即可扫描设备音频 |
| 三路径扫描策略 | SAF Picker(优先) → MANAGE_EXTERNAL_STORAGE + MediaStore → MediaStore 只读 |
| 递归扫描 | DocumentsContract.BuildChildDocumentsUriUsingTree 递归遍历 |
| 音频格式 | .mp3 .flac .wav .ogg .oga .opus .m4a .mp4 .aac .wma .aiff .aifc .ape .wv .tta .mka .dsf .dff .mid .midi .rmi .spx .amr .3gp .mkv .webm（共26种） |
| Tag 读取 | TagLibSharp 解析标题/艺术家/专辑/时长/比特率/年份/音轨/流派/封面/嵌入歌词 |
| 增量式扫描 | 每 20 首一批回调入库 + 列表实时刷新，进度条动画 |
| 缓存歌曲批量加载 | 每 50 首一批加载，30ms 间隔给主线程喘息 |
| 歌曲去重 | 本地按 FilePath 去重，网络按 RemoteId 去重，已存在则更新 |
| 封面懒加载 | 滚动到可见时加载，ConcurrentDictionary 去重 + SemaphoreSlim(4) 限流 + 取消支持 |

### ▶️ 音频播放 (ExoPlayer)

| 特性 | 说明 |
|------|------|
| 播放引擎 | AndroidX Media3 ExoPlayer 1.10.0 |
| 播放/暂停/上下曲 | 完整控制 |
| 进度拖动 | Material Slider，松手 seek |
| 音量控制 | 0-100 |
| 自动下一首 | 播放完毕自动切换 |
| 流媒体播放 | 支持 HTTP/HTTPS URL |
| content:// URI | 自定义 DataSource：content:// → ContentDataSource，http → DefaultHttpDataSource |
| Basic Auth | URL 嵌入 `user:pass@host` 自动提取，转为 Authorization 请求头 |
| WakeLock | PARTIAL_WAKE_LOCK 后台播放防 CPU 休眠 |
| WiFi Lock | 高性能模式保持 WiFi 连接，防锁屏断网 |
| 音频焦点 | Gain→恢复 / Loss→暂停 / LossTransient→暂停后恢复 / LossTransientCanDuck→音量降至 1/3 |
| 播放就绪等待 | TaskCompletionSource + 轮询（100ms×100） |
| 播放状态持久化 | 每 ~5 秒自动保存位置/模式，启动时同步恢复模式 + 异步恢复队列和位置 |
| 播放失败处理 | 延迟 1 秒显示错误对话框，期间若恢复播放则取消 |
| 歌曲切换防竞态 | _isSwitchingSong 标志阻止 Stopped 事件误触发 Next() |
| 音频频谱可视化 | Android Visualizer API + FFT，64 频段实时跳动 |
| 频谱算法 | 混合线性-对数频段分布、汉宁窗平滑、RMS 能量计算 |
| 频谱开关 | 控制区一键开启/关闭频谱显示 |
| 睡眠定时 | 10/20/30/45/60/90 分钟 + 自定义时间倒计时 |
| 播完再停 | 可选播完当前歌曲后再暂停 |

### 🔀 播放队列与模式

| 特性 | 说明 |
|------|------|
| 顺序播放 | 到末尾停止 |
| 列表循环 🔁 | 循环播放列表 |
| 单曲循环 🔂 | 重复当前歌曲 |
| 随机播放 🔀 | Fisher-Yates 洗牌算法，双列表设计（原始+洗牌） |
| 播放历史栈 | Stack\<int\>，支持"上一曲"回退 |
| 即将播放预览 | GetUpcomingSongs(N) 显示接下来 N 首 |
| AddNext | 插入当前播放位置之后 |
| PeekNext | 预览下一首，不改变状态 |
| O(1) 歌曲查找 | _songIdToIndex 字典 |

### 🎶 歌词系统

| 特性 | 说明 |
|------|------|
| LRC 格式解析 | 兼容 `[mm:ss.xx]` / `[mm:ss.xxx]` / `[mm:ss]`，支持多时间戳行 |
| 元数据标签 | 解析 `[ti:]` `[ar:]` `[al:]` 等元数据 |
| 多源歌词 | 嵌入歌词 → 同名 .lrc → Navidrome 远程歌词 → 磁盘缓存 |
| 二分查找 | GetCurrentLyricIndex O(log n) 定位当前歌词行 |
| 5 行显示 | 上上/上/当前(高亮)/下/下下 |
| 全屏歌词页 | 毛玻璃模糊背景（Android 12+ RenderEffect），手动滚动暂停 3 秒后恢复自动 |
| 拖拽定位 | 检测拖拽阈值(20px)，显示虚线+跳转按钮，松手 seek |
| 歌词设置 | 拖拽开关 / 字体大小(SeekBar) / 对齐方式(左/中/右) |
| SAF .lrc 查找 | 从 content:// URI 构造同名 .lrc URI |
| 远程歌词(Navidrome) | OpenSubsonic 结构化歌词 → 简单歌词(lyricsBySongId) → 旧版歌词 三级回退 |
| 远程歌词(WebDAV) | 同名 .lrc 文件 > 嵌入歌词 |
| 纯文本歌词降级 | 无时间戳歌词生成伪 LRC（按时长均匀分布） |
| 歌词缓存 | SQLite 持久化 + 网络歌词缓存到 `CacheDir/lyrics/lyrics_{songId}.lrc` |

### 🖥️ 桌面悬浮歌词

| 特性 | 说明 |
|------|------|
| 悬浮窗显示 | SYSTEM_ALERT_WINDOW 权限，ApplicationOverlay(Android O+) / Phone(fallback) |
| 触摸拖拽 | Y 轴拖拽，锁定模式禁止触摸 |
| 锁定模式 | 🔒 锁定位置(NotTouchable) / 🔐 解锁，2 秒后自动隐藏锁定按钮 |
| 单行模式 | 居中跑马灯滚动 |
| 双行 KTV | 当前行左上亮色 + 下一行右下暗色 |
| 字体大小 | 12-36sp，实时预览 |
| 歌词颜色 | 10 色预设色板 |
| 粗体/透明度/边框 | 全部可自定义，实时生效 |
| 淡入淡出 | 1 秒后背景透明，触摸时显示半透明背景(0.35) |
| 圆角背景 | GradientDrawable + 16dp 圆角 |
| 位置持久化 | Y 坐标保存到 SharedPreferences |
| 通知栏快捷控制 | 开/关/锁定/单双行切换 |
| 全厂商适配 | 小米/华为/荣耀/OPPO/vivo/魅族悬浮窗权限 |

### 💚 收藏与播放历史

| 特性 | 说明 |
|------|------|
| 收藏/取消 | ♥ 实时写入 SQLite |
| 收藏列表 | 播放列表→收藏歌曲 Tab |
| 通知栏收藏 | 工具通知一键收藏/取消 |
| 播放历史 | 自动记录全部历史，去重计次（PlayCount 字段） |
| 播放计数 | 每播放一次自动 +1，数据库持久化 |
| 收藏保留 | 网络重新扫描前保存 RemoteId→AddedAt 映射，扫描后按 RemoteId 恢复收藏 |

### 🔔 通知栏 / MediaSession

| 特性 | 说明 |
|------|------|
| 双通知设计 | 主通知(1001, 播放控制) + 工具通知(1002, 快捷操作) |
| MediaStyle 主通知 | 上一曲/播放暂停/下一曲 + 大尺寸专辑封面 |
| 工具通知 | RemoteViews 自定义布局：收藏/桌面歌词/锁定/双行模式 |
| MediaSession | 蓝牙耳机/车载音响/穿戴设备控制 |
| 媒体按钮 | Headsethook/PlayPause/Next/Previous/FastForward(+5s)/Rewind(-5s)/Stop |
| 锁屏显示 | 封面/标题/控制按钮 |
| 前台 Service | foregroundServiceType="mediaPlayback" 保活 |

### 🎨 主题与配色

| 特性 | 说明 |
|------|------|
| 5 色主题 | Purple(默认) / Pink / Blue / Green / Orange |
| 深色模式 | 明亮 / 深色 / 跟随系统 三种设置 |
| 27 个自定义属性 | catClawPageBackground / PrimaryColor / TextPrimary / GradientStart 等 |
| 毛玻璃风格卡片 | CatClawCard(20dp圆角) / CatClawCardSmall(16dp) / CatClawCardImage(24dp) |
| 自定义按钮 | CatClawButtonPrimary(16dp圆角) / CatClawButtonSecondary |
| 规范字体 | sans-serif / sans-serif-medium 中文字体 |
| 主题持久化 | SharedPreferences 保存，运行时 AppCompatDelegate 切换 |

### ☁️ 网络协议

> **已实现**：WebDAV、Navidrome (Subsonic API)　|　**规划中**：SMB、DLNA、FTP、NFS

**Navidrome (Subsonic API)**

| 特性 | 说明 |
|------|------|
| 连接测试 | `ping.view` |
| 增量式扫描 | `getAlbumList2`(alphabeticalByArtist, 200/页, 最多50页) + `getAlbum` 两阶段拉取 |
| 逐专辑回调 | 每个专辑完成后 songCallback 增量入库 + UI 刷新 |
| 封面图 | `getCoverArt.view`，懒加载 |
| 歌词三级回退 | 结构化歌词(lyricsList.structuredLyrics) → 简单歌词 → 旧版歌词 |
| 收藏同步 | `star` / `unstar` |
| 流媒体 | `stream.view` |
| 搜索 | `search3.view` |
| Token 认证 | MD5(password + salt)，兼容 OpenSubsonic |
| JSON 兼容 | EnumerateSongArray 处理单首歌返回对象而非数组 |

**WebDAV**

| 特性 | 说明 |
|------|------|
| PROPFIND | Depth=1 列出目录，Depth=0 获取单文件信息 |
| 递归扫描 | 最大 20 层深度，每 10 首一批入库 |
| GET 流播放 | 支持 HTTP Range 头 |
| 元数据提取 | FetchSongMetadataAsync 下载头部 512KB + TagReader |
| Basic 认证 | Base64 编码用户名密码 |
| SSL 跳过验证 | 支持 NAS 自签名证书 |
| 连接测试 | Depth=0 PROPFIND 验证 |
| SocketsHttpHandler | 绕过 Android 网络栈对 HTTP 方法的限制 |

### 🔍 探索搜索

| 特性 | 说明 |
|------|------|
| 实时搜索 | 300ms 防抖 + CancellationTokenSource 取消旧搜索 |
| SQL JOIN 搜索 | 数据库层面 JOIN Artist/Album，避免 N+1 查询 |
| 多字段匹配 | 标题/艺术家/专辑 |

### 📱 页面导航

| Tab | 页面 | 说明 |
|-----|------|------|
| Tab 0 | 全屏歌词 | 毛玻璃背景，拖拽定位，5 行歌词 |
| Tab 1 | 正在播放 | 封面/歌词/控制/播放列表弹窗 |
| Tab 2 | 播放列表 | 全部/收藏/最近 三个子 Tab |
| Tab 3 | 探索 | 实时搜索标题/艺术家/专辑 |
| Tab 4 | 音乐库 | 本地/网络 Tab 切换 |

**侧滑面板**：80% 宽度设置面板 + 20% 遮罩(60% 黑)，手势拖拽关闭(阈值 100px)，alpha 动画 250ms

**子页面**（Fragment 路由）：设置、通用设置、音乐文件夹、远程音乐、Navidrome 设置、WebDAV 设置、桌面歌词设置、播放列表详情、探索搜索

***

## 🗄️ 数据库结构

**SQLite + WAL 模式**，线程安全初始化（SemaphoreSlim 双重检查锁）

| 表名 | 关键字段 | 说明 |
|------|---------|------|
| Songs | Id, Title, ArtistId(FK), AlbumId(FK), FilePath(Unique), Source, Protocol, RemoteId | 歌曲主表 |
| Artists | Id, Name(Unique), Cover | 艺术家 |
| Albums | Id, Title, ArtistId(FK), CoverArtPath, SongCount, Year | 专辑 |
| Playlists | Id, Name, CreatedAt, UpdatedAt, SongCount, IsSystem | 系统歌单: -1全部/-2收藏/-3最近 |
| PlaylistSongs | Id, PlaylistId(Indexed), SongId(Indexed), Position | 歌单歌曲关联 |
| Favorites | SongId(PK), AddedAt | 收藏 |
| PlayHistory | SongId(Indexed), PlayedAt, PlayCount | 播放历史（去重计次） |
| Lyrics | SongId(PK), LrcPath, Content | 歌词缓存 |
| CachedSongs | Id, SongId, LocalPath, CachedAt, FileSize | 网络歌曲本地缓存 |
| ConnectionProfiles | Id, Name, Protocol, Host, Port, UserName, Password, BasePath, UseHttps | 网络连接配置 |

**索引优化**：idx_songs_artist / idx_songs_album / idx_songs_title / idx_albums_artist / idx_play_history_time

**数据库迁移**：PRAGMA table_info 检测 + MigratePlaylistsTableAsync / MigratePlaylistSongsTableAsync

***

## 🔌 插件体系

猫爪音乐支持通过插件系统扩展功能，**插件 SDK 支持 5 种主流编程语言**，所有语言编译为统一的 `.ccp` 格式。

### 插件 SDK

| 语言 | 编译方式 | 说明 |
|------|---------|------|
| **C#** | `dotnet` 直接编译 | 进程内原生执行 |
| **Java** | IKVM.NET → CIL | `.java` → `javac` → `ikvmc` → `.dll` |
| **Python** | IronPython 嵌入 | `.py` → EmbeddedResource → `.dll` |
| **JavaScript** | Jint 引擎嵌入 | `.js` → EmbeddedResource → `.dll` |
| **Go** | cgo c-shared + P/Invoke | `main.go` → `go build` → `.so` → `DllImport` |

> SDK 源码位于 `plugins/PluginSDK/`，含完整模板和编译脚本。

### 插件接口

| 接口 | 说明 |
|------|------|
| IPlugin | 插件基类：Name / Version / Author / Capabilities / InitializeAsync / ShutdownAsync |
| ILyricsProviderPlugin | 歌词提供者：GetLyricsAsync / IsAvailable |
| ICoverProviderPlugin | 封面提供者：GetCoverAsync / IsAvailable |
| IProtocolProviderPlugin | 协议提供者：ProtocolName / ListFilesAsync / OpenReadAsync / TestConnectionAsync |
| IAudioEnhancerPlugin | 音频增强器：ProcessSamples / Reset |
| IMenuContributorPlugin | 菜单贡献者：GetMenuItems / OnMenuItemClicked |

### 📦 已发布插件

| 插件 | 说明 | 地址 |
|------|------|------|
| **猫爪标签 (CatClawTag)** | 歌词/封面多源搜索、匹配元数据、标签读写 | [GitHub](https://github.com/kankejiang/CatClawTagPlugin) |

***

## 📜 开源协议

MIT License
