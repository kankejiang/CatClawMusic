# 🐾 猫爪音乐 (CatClaw Music)

> 萌系 Android 音乐播放器，.NET 9 + C# 原生开发。支持本地音乐、Navidrome/Subsonic 网络音乐、WebDAV 远程文件、桌面悬浮歌词（可拖拽/锁定/双行KTV）、LRC 歌词同步滚动、全屏歌词体验、音频频谱可视化、睡眠定时、通知栏媒体控制 + MediaSession、播放状态自动保存与恢复、MediaStore 极速封面加载、动态流光背景、封面取色主题。
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
│   ├── Interfaces/             # 14 个服务接口
│   ├── Models/                 # 12 个数据模型 + 2 枚举
│   └── Services/               # PlayQueue / LyricsService / TagReader / MusicUtility / PluginManager
│
├── CatClawMusic.Data/          # 数据层（数据库 + 网络服务）
│   ├── MusicDatabase.cs        # SQLite（9 表 + 索引 + WAL）
│   ├── SubsonicService.cs      # Navidrome/OpenSubsonic API
│   ├── WebDavService.cs        # WebDAV 协议
│   ├── SmbService.cs           # SMB/CIFS 协议
│   ├── MusicScanner.cs         # 统一渐进式批量入库
│   ├── MusicLibraryService.cs  # 音乐库服务实现
│   └── NetworkMusicService.cs  # 网络音乐工厂
│
└── CatClawMusic.UI/            # UI 层（Android 原生界面）
    ├── MainActivity.cs         # ViewPager2 + BottomNav + 侧滑面板 + 迷你播放器
    ├── Fragments/              # 18 个 Fragment（含 LocalMusicSettings / FullLyrics 等）
    ├── ViewModels/             # MVVM（CommunityToolkit.Mvvm 源生成器）
    ├── Helpers/                # VisualizerHelper / AudioVisualizerView / BatchObservableCollection
    ├── Services/               # 桌面歌词 / 导航 / 播放状态 / 前台服务 / 扫描设置 / MaterialYou 取色
    ├── Adapters/               # 歌曲列表 / 播放列表 适配器（MediaStore 封面优先加载）
    └── Platforms/Android/      # ExoPlayer / SAF / 主题 / MediaStoreCoverHelper / AndroidMediaScanner
```

**技术栈**：.NET 9 | C# 12 | AndroidX Media3 ExoPlayer 1.10.0 | CommunityToolkit.Mvvm 8.2.2 | TagLibSharp 2.3.0 | SQLite (sqlite-net-pcl) | SMBLibrary | Material 3 | Android Visualizer API

***

## ✨ 功能特性

### 🎵 本地音乐

| 特性 | 说明 |
|------|------|
| SAF 文件夹选择 | 系统文件管理器界面，无需 `MANAGE_EXTERNAL_STORAGE` |
| 多文件夹支持 | 管道分隔(\|)存储多个 SAF URI，权限过期自动检测并移除 |
| MediaStore 扫描 | Android 10+ 无需存储权限即可扫描设备音频 |
| 三路径扫描策略 | SAF Picker(优先) → MANAGE_EXTERNAL_STORAGE + MediaStore → MediaStore 只读 |
| 本地音乐设置页 | 仿椒盐音乐设计：使用 Android 媒体库开关 / 不扫描 60s 以下音频 / 自定义文件夹 / 权限管理 |
| 扫描设置持久化 | ScanSettings（SharedPreferences）：UseMediaStore / FilterShortAudio / MinDurationSec |
| 递归扫描 | DocumentsContract.BuildChildDocumentsUriUsingTree 递归遍历 |
| 音频格式 | .mp3 .flac .wav .ogg .oga .opus .m4a .mp4 .aac .wma .aiff .aifc .ape .wv .tta .mka .dsf .dff .mid .midi .rmi .spx .amr .3gp .mkv .webm（共26种） |
| Tag 读取 | TagLibSharp 解析标题/艺术家/专辑/时长/比特率/年份/音轨/流派/封面/嵌入歌词 |
| 增量式扫描 | 每 20 首一批回调入库 + 列表实时刷新，进度条动画 |
| 缓存歌曲批量加载 | 每 50 首一批加载，30ms 间隔给主线程喘息 |
| 歌曲去重 | 本地按 FilePath 去重，网络按 RemoteId 去重，已存在则更新 |
| MediaStore 极速封面 | LruCache → 磁盘缓存 → MediaStore LoadThumbnail(Q+) → TagLib/网络，毫秒级返回 |
| MediaStoreId 持久化 | Song.MediaStoreId 字段存入数据库，旧数据 BatchFillMediaStoreIds 后台回填 |
| 封面懒加载 | 滚动到可见时加载，ConcurrentDictionary 去重 + SemaphoreSlim(4) 限流 + 取消支持 |
| 封面加载通知 | 加载完成后 NotifyItemChanged 刷新对应 item，解决首页封面不显示问题 |

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
| file:// 本地播放 | CatClawDataSource 识别 file:// / 空 scheme → System.IO.FileStream 直接读取，避免 MalformedURLException |
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
| 频谱默认关闭 | 频谱默认关闭，用户手动开启时才请求录音权限 |
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
| 歌词编码检测 | ReadLyricsFile 自动检测：BOM UTF-8 → 严格 UTF-8 → GBK → GB2312 → 默认，解决中文乱码 |
| 歌词路径匹配 | 精确匹配 → "艺术家 - 标题.lrc" → 目录下模糊匹配，提高歌词查找成功率 |
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
| 紫色调深色模式 | 非纯黑背景，紫色调深色模式，视觉更柔和 |
| 无重启主题切换 | 运行时直接变色，音频不中断，无需重启 Activity |
| 深浅色模式图标 | 月牙🌙(深色) / 太阳☀️(浅色) / 半阳半月🌗(跟随系统) |
| 动态流光背景 | ValueAnimator 8s 循环，3 个大面积色带独立相位漂移 + 呼吸 + 缩放脉冲 |
| 切歌颜色过渡 | TransitionToColors 800ms ArgbEvaluator 平滑过渡背景色和光晕颜色 |
| 封面取色主题 | MaterialYouPalette HSV 色调映射，封面主色驱动播放页背景和光晕配色 |
| 封面切换动画 | AnimateCoverChange：缩小到 92% + 淡出到 30% → 500ms Overshoot 弹回 + 淡入 |
| 封面底部发光 | ApplyCoverGlow 径向渐变发光，颜色跟随封面取色 |
| 27 个自定义属性 | catClawPageBackground / PrimaryColor / TextPrimary / GradientStart 等 |
| 毛玻璃风格卡片 | CatClawCard(20dp圆角) / CatClawCardSmall(16dp) / CatClawCardImage(24dp) |
| 自定义按钮 | CatClawButtonPrimary(16dp圆角) / CatClawButtonSecondary |
| 规范字体 | sans-serif / sans-serif-medium 中文字体 |
| 主题持久化 | SharedPreferences 保存，运行时 AppCompatDelegate 切换 |

### ☁️ 网络协议

> **已实现**：WebDAV、Navidrome (Subsonic API)、SMB/CIFS　|　**规划中**：DLNA、FTP、NFS

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

**SMB/CIFS**

| 特性 | 说明 |
|------|------|
| SMB 协议 | SMBLibrary 实现 SMB1/CIFS 协议通信 |
| 共享目录浏览 | 列出服务器共享目录，选择指定共享文件夹 |
| 递归扫描 | 递归遍历共享目录下所有音频文件，每 20 首一批入库 |
| 域认证 | 支持 Domain 字段，兼容企业 AD 域环境 |
| NTLM 认证 | UseNtlm 开关，支持 NTLMv2 认证 |
| 连接测试 | 列出共享目录验证连接与认证 |
| 流播放 | 支持 HTTP Range 头的流式播放 |

### 🔍 探索搜索

| 特性 | 说明 |
|------|------|
| 实时搜索 | 300ms 防抖 + CancellationTokenSource 取消旧搜索 |
| SQL JOIN 搜索 | 数据库层面 JOIN Artist/Album，避免 N+1 查询 |
| 多字段匹配 | 标题/艺术家/专辑 |

### 🔐 权限管理

| 特性 | 说明 |
|------|------|
| 权限状态总览 | 设置页面可查看所有权限状态和说明 |
| 录音权限 | 频谱可视化需要，手动开启时请求 |
| 悬浮窗权限 | 桌面歌词需要，引导用户至系统设置 |
| 通知权限 | Android 13+ 运行时请求 |

### 📱 页面导航

| Tab | 页面 | 说明 |
|-----|------|------|
| Tab 0 | 全屏歌词 | 毛玻璃背景，拖拽定位，5 行歌词 |
| Tab 1 | 正在播放 | 封面/歌词/控制/播放列表弹窗 |
| Tab 2 | 播放列表 | 全部/收藏/最近 三个子 Tab |
| Tab 3 | 探索 | 实时搜索标题/艺术家/专辑 |
| Tab 4 | 音乐库 | 本地/网络 Tab 切换 |

**侧滑面板**：80% 宽度设置面板 + 20% 遮罩(60% 黑)，手势拖拽关闭(阈值 100px)，alpha 动画 250ms

**子页面**（Fragment 路由）：设置、通用设置、本地音乐设置、音乐文件夹、远程音乐、Navidrome 设置、WebDAV 设置、SMB 设置、桌面歌词设置、播放列表详情、探索搜索、插件管理

***

## 🗄️ 数据库结构

**SQLite + WAL 模式**，线程安全初始化（SemaphoreSlim 双重检查锁）

| 表名 | 关键字段 | 说明 |
|------|---------|------|
| Songs | Id, Title, ArtistId(FK), AlbumId(FK), FilePath(Unique), Source, Protocol, RemoteId, MediaStoreId | 歌曲主表，MediaStoreId 用于 LoadThumbnail 封面加载 |
| Artists | Id, Name(Unique), Cover | 艺术家 |
| Albums | Id, Title, ArtistId(FK), CoverArtPath, SongCount, Year | 专辑 |
| Playlists | Id, Name, CreatedAt, UpdatedAt, SongCount, IsSystem | 系统歌单: -1全部/-2收藏/-3最近 |
| PlaylistSongs | Id, PlaylistId(Indexed), SongId(Indexed), Position | 歌单歌曲关联 |
| Favorites | SongId(PK), AddedAt | 收藏 |
| PlayHistory | SongId(Indexed), PlayedAt, PlayCount | 播放历史（去重计次） |
| Lyrics | SongId(PK), LrcPath, Content | 歌词缓存 |
| CachedSongs | Id, SongId, LocalPath, CachedAt, FileSize | 网络歌曲本地缓存 |
| ConnectionProfiles | Id, Name, Protocol, Host, Port, UserName, Password, BasePath, UseHttps, Domain, ShareName, UseNtlm | 网络连接配置 |

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

## � 版本更新

### v1.0.12

**🎉 新功能**

- **本地音乐设置页**：仿椒盐音乐设计，支持"使用 Android 媒体库"开关、不扫描 60s 以下音频、自定义文件夹管理、外部存储权限管理
- **MediaStore 极速封面加载**：通过 `ContentResolver.LoadThumbnail()` 直接获取系统缓存封面，毫秒级返回，替代 TagLib 逐文件解码（200-400ms/首）
- **动态流光背景**：ValueAnimator 8s 循环驱动 3 个大面积色带独立相位漂移 + 呼吸 + 缩放脉冲，高饱和度高透明度
- **封面取色主题**：MaterialYouPalette HSV 色调映射，封面主色驱动播放页背景和光晕配色
- **封面切换动画**：缩小到 92% + 淡出到 30% → 500ms Overshoot 弹回 + 淡入
- **封面底部发光**：ApplyCoverGlow 径向渐变发光，颜色跟随封面取色
- **切歌颜色过渡**：TransitionToColors 800ms ArgbEvaluator 平滑过渡背景色和光晕颜色

**🐛 修复**

- 修复重启后第一首歌没有总时长（PrepareWithoutPlayAsync 不启动位置定时器 + OnPositionChanged 限制 + ResumeAsync 缺失）
- 修复音乐库第一页除第一首外都没有封面（SemaphoreSlim 竞态 + _loadingCovers 竞态 + 无 NotifyItemChanged 通知）
- 修复点击"管理外部存储权限"闪退（包名错误 + Intent 回退策略）
- 修复本地文件路径被当作 HTTP URL 播放（MalformedURLException），CatClawDataSource 新增 file:// / 空 scheme 分支
- 修复播放失败 NullPointerException（CatClawDataSource file 分支未设置 _contentUri）
- 修复歌词显示"暂无歌词"（编码自动检测 BOM UTF-8 → GBK → GB2312 + 路径匹配扩展）
- 修复封面仍然走 IMediaMetadataRetriever 而非 MediaStore.LoadThumbnail（本地歌曲 MediaStore 优先路径 + BatchFillMediaStoreIds 回填）

**⚡ 优化**

- 封面加载优先级重构：LruCache → 磁盘缓存 → MediaStore（本地歌曲）→ TagLib/网络
- SemaphoreSlim 从 (2,2) 提升到 (4,4)，移除 100ms 延迟
- 流光效果增强：透明度 25%→60%，饱和度 max 0.85，色带半径 500/600/550dp
- 扫描策略支持 ScanSettings 配置（UseMediaStore / FilterShortAudio / MinDurationSec）

***

## �📜 开源协议

MIT License
