# 🐾 猫爪音乐 (CatClaw Music)

> 萌系 Android 音乐播放器，.NET 9 + C# 原生开发。支持本地音乐播放、Navidrome/Subsonic 网络音乐、WebDAV 远程文件、桌面悬浮歌词（可拖拽/锁定/双行KTV）、LRC 歌词同步滚动、全屏歌词体验、通知栏媒体控制 + MediaSession。

![平台](https://img.shields.io/badge/平台-Android-green)
![.NET](https://img.shields.io/badge/.NET-9.0-512bd4)
![语言](https://img.shields.io/badge/C%23-12.0-blue)
![最低版本](https://img.shields.io/badge/最低版本-Android%2012%20(API%2031)-orange)
![协议](https://img.shields.io/badge/协议-MIT-yellow)

---

## ✨ 功能特性

### 🎵 本地音乐播放

| 特性 | 说明 |
|------|------|
| SAF 文件夹选择 | 系统文件管理器界面，无需 `MANAGE_EXTERNAL_STORAGE` |
| MediaStore 扫描 | Android 10+ 无需存储权限即可扫描设备音频 |
| 递归扫描 | 支持子目录，自动发现音频文件 |
| Tag 信息读取 | TagLibSharp 解析标题/艺术家/专辑/时长/比特率/封面 |
| 增量式扫描 | 逐批入库 + 列表实时刷新，进度条动画 |
| 歌曲去重 | 按文件路径去重，已存在则更新 |
| 封面懒加载 | 滚动到可见时加载，磁盘缓存，防错位 |

### ▶️ 音频播放 (ExoPlayer)

| 特性 | 说明 |
|------|------|
| 播放引擎 | AndroidX Media3 ExoPlayer |
| 播放/暂停/上下曲 | 完整控制 |
| 进度拖动 | Material Slider |
| 音量控制 | 0-100 |
| 自动下一首 | 播放完毕自动切换 |
| 流媒体播放 | 支持 HTTP/HTTPS URL |
| content:// URI | ExoPlayer 原生支持 SAF 路径 |
| WakeLock | 后台播放防 CPU 休眠 |
| WiFi Lock | 防止锁屏断网 |

### 🔀 播放队列与模式

| 模式 | 说明 |
|------|------|
| 顺序播放 | 到末尾停止 |
| 列表循环 🔁 | 循环播放列表 |
| 单曲循环 🔂 | 重复当前歌曲 |
| 随机播放 🔀 | Fisher-Yates 洗牌算法 |
| 播放历史栈 | 支持"上一曲"回溯 |
| 即将播放预览 | 显示接下来 3 首 |

### 🎶 歌词系统

| 特性 | 说明 |
|------|------|
| LRC 格式解析 | 兼容 `[mm:ss.xx]` / `[mm:ss.xxx]` / `[mm:ss]` |
| 多源歌词 | 嵌入歌词 → 同名 .lrc → Navidrome 远程歌词 |
| 时间轴同步 | 5 行显示（上上/上/当前/下/下下），当前行高亮 |
| 全屏歌词页 | 毛玻璃模糊背景，手动滚动暂停 3 秒 |
| 拖拽定位 | 长按拖动选择歌词行，松手 seek |
| 歌词设置 | 拖拽开关、字体大小、对齐方式（左/中/右） |
| 歌词缓存 | 解析结果持久化到 SQLite |
| 远程歌词 | OpenSubsonic 结构化歌词 + 纯文本回退 |

### 🖥️ 桌面悬浮歌词

| 特性 | 说明 |
|------|------|
| 悬浮窗显示 | SYSTEM_ALERT_WINDOW 权限 |
| 触摸拖拽 | Y 轴拖动，锁定模式禁止 |
| 锁定模式 | 🔒 锁定位置 / 🔐 解锁 |
| 单行模式 | 居中跑马灯滚动 |
| 双行 KTV | 当前行左上亮色 + 下一行右下暗色 |
| 字体大小 | 12-36sp，实时预览 |
| 歌词颜色 | 10 色预设色板 |
| 粗体/透明度/边框 | 全部可自定义 |
| 位置持久化 | Y 坐标保存到 SharedPreferences |
| 通知栏快捷控制 | 开/关/锁定/单双行切换 |
| 全厂商适配 | 小米/华为/荣耀/OPPO/vivo/魅族悬浮窗权限 |

### 💚 收藏与播放历史

| 特性 | 说明 |
|------|------|
| 收藏/取消 | ♥ 实时写入 SQLite |
| 收藏列表 | 播放列表→收藏歌曲 Tab |
| 通知栏收藏 | 一键收藏/取消 |
| 播放历史 | 自动记录，去重计次 |
| 历史上限 | 保留最近 20 条 |

### 🔔 通知栏 / MediaSession

| 特性 | 说明 |
|------|------|
| MediaStyle 通知 | 播放控制 + 工具控制双通道 |
| 封面显示 | 大尺寸专辑封面 |
| MediaSession | 蓝牙耳机/车载音响/穿戴设备控制 |
| 锁屏显示 | 封面/标题/控制按钮 |
| 高优先级 | 播放通道可绕过勿扰模式 |
| 前台 Service | TypeMediaPlayback 保活 |

### ☁️ 网络协议

**Navidrome (Subsonic API)**

| 特性 | 说明 |
|------|------|
| Ping 连接测试 | `ping.view` |
| 增量式扫描 | `getAlbumList2` + `getAlbum` 两阶段拉取 |
| 封面图 | `getCoverArt.view`，懒加载 |
| 歌词获取 | OpenSubsonic 结构化歌词 + 纯文本回退 |
| 收藏同步 | `star` / `unstar` |
| 流媒体 | `stream.view` |
| Token 认证 | MD5(password + salt) |

**WebDAV**

| 特性 | 说明 |
|------|------|
| PROPFIND | 列出文件和目录 |
| GET | 读取文件流 |
| Basic 认证 | 用户名/密码 |
| SSL 证书 | 支持跳过验证 |
| 连接测试 | Depth=0 PROPFIND 验证 |

### 📱 页面导航

| Tab | 页面 | 说明 |
|-----|------|------|
| Tab 0 | 全屏歌词 | 毛玻璃背景，拖拽定位 |
| Tab 1 | 正在播放 | 封面/歌词/控制/播放列表弹窗 |
| Tab 2 | 播放列表 | 全部/收藏/最近三个子 Tab |
| Tab 3 | 搜索 | 实时搜索标题/艺术家/专辑 |
| Tab 4 | 音乐库 | 本地/网络 Tab 切换 |

**子页面**（Fragment 路由）：设置、通用设置、音乐文件夹、远程音乐、Navidrome 设置、WebDAV 设置、桌面歌词设置、播放列表详情

---

## 🏗️ 项目结构

```
CatClawMusic/
├── CatClawMusic.Core/                  # 核心层
│   ├── Models/                         # 数据模型 (Song, Album, Artist, Playlist, Lyric, Favorite, PlayHistory, CachedSong, ConnectionProfile)
│   ├── Interfaces/                     # 接口定义 (IAudioPlayerService, ILyricsService, IMusicLibraryService, INetworkMusicService, ISubsonicService, INetworkFileService, IPlugin 等)
│   └── Services/                       # 核心服务 (PlayQueue, LyricsService, TagReader, MusicUtility)
│
├── CatClawMusic.Data/                  # 数据层
│   ├── MusicDatabase.cs                # SQLite 数据库 (WAL 模式, 10 张表)
│   ├── MusicLibraryService.cs          # 本地音乐库服务 (扫描/入库/搜索/合并去重)
│   ├── NetworkMusicService.cs          # 网络音乐聚合服务 (协议分发/增量入库)
│   ├── SubsonicService.cs              # Subsonic/Navidrome API 客户端
│   └── WebDavService.cs                # WebDAV PROPFIND 文件服务
│
├── CatClawMusic.UI/                    # UI 层 (原生 Android)
│   ├── Fragments/                      # 14 个 Fragment (播放/歌词/设置/库/搜索/桌面歌词等)
│   ├── ViewModels/                     # 8 个 ViewModel (MVVM + CommunityToolkit.Mvvm)
│   ├── Services/                       # 平台服务 (DesktopLyricService, ForegroundPlayerService, NavigationService, PlaybackStateManager, AndroidLocalScanner 等)
│   ├── Adapters/                       # 列表适配器 (SongAdapter, PlaylistAdapter, UpcomingSongAdapter)
│   ├── Platforms/Android/              # Android 平台实现 (AudioPlayerService, PermissionService, FolderPicker, SafeContentScanner)
│   ├── Helpers/                        # UI 工具 (BindingHelper, FontHelper, UiHelper)
│   └── Resources/                      # Android 资源 (布局/图标/字体/动画/配色)
│
├── docs/                               # 文档
│   └── 技术方案.md                      # 技术设计文档
├── CatClawMusic.sln                    # 解决方案文件
├── Directory.Build.props               # 构建属性 (Android SDK 路径)
├── global.json                         # .NET SDK 版本锁定 (9.0.313)
└── build_apk.bat                       # APK 构建脚本
```

---

## 🗄️ 数据库 (SQLite)

10 张表，WAL 模式，5 个额外索引：

| 表 | 说明 |
|----|------|
| Songs | 歌曲 (含 ArtistId/AlbumId 索引, RemoteId 网络去重) |
| Artists | 艺术家 (名称唯一) |
| Albums | 专辑 (含 ArtistId 索引) |
| Playlists | 播放列表 (含系统列表标识) |
| PlaylistSongs | 播放列表-歌曲关联 (含位置排序) |
| Favorites | 收藏 (以 SongId 为主键) |
| PlayHistory | 播放历史 (含播放次数, 保留 20 条) |
| Lyrics | 歌词缓存 (以 SongId 为主键) |
| CachedSongs | 离线缓存 (下载逻辑待实现) |
| ConnectionProfiles | 网络连接配置 (支持多协议) |

---

## 🎨 UI 设计

**设计风格**：毛玻璃 (Glassmorphism) + Material 3

| 用途 | 色值 | 说明 |
|------|------|------|
| 主色 | `#9B7ED8` | 柔和紫粉 |
| 辅色 | `#98B8E8` | 淡蓝 |
| 强调色 | `#90D5C8` | 薄荷绿 |
| 页面背景 | `#F0F2F5` | 浅灰白 |
| 卡片背景 | `#E6FFFFFF` | 80% 不透明白色 |
| 主文字 | `#2D2438` | 深紫灰 |
| 副文字 | `#6B5E7A` | 中紫灰 |

**字体**：站酷快乐体 2016 (标题/按钮) + 系统默认 (正文)

---

## 🔧 技术栈

| 层 | 技术 |
|----|------|
| 框架 | .NET 9 (Android) |
| 语言 | C# 12.0 |
| 架构 | MVVM + 三层架构 |
| DI | Microsoft.Extensions.DependencyInjection |
| MVVM | CommunityToolkit.Mvvm 8.2.2 |
| 数据库 | SQLite (sqlite-net-pcl + WAL) |
| 标签读取 | TagLibSharp 2.3.0 |
| 播放器 | ExoPlayer (AndroidX Media3 1.10.0) |
| UI 组件 | Material 3 + ViewPager2 + RecyclerView |
| 权限 | SAF (Storage Access Framework) |
| 通知 | MediaStyle + 自定义布局双通道 |
| 媒体会话 | Android.Media.Session.MediaSession |

---

## 🔨 构建说明

### 环境要求

- .NET SDK 9.0+
- Visual Studio 2022+ (需使用 MSBuild 构建，`dotnet build` 存在兼容性问题)
- Android SDK (API 31+)
- JDK 21


## 🧩 插件扩展系统 (规划中)

通过轻量插件机制扩展功能，已定义接口：

| 插件类型 | 接口 | 说明 |
|----------|------|------|
| 协议插件 | `IProtocolProviderPlugin` | 接入新网络音乐源 (SMB/FTP/DLNA/NFS) |
| 歌词插件 | `ILyricsProviderPlugin` | 在线搜索/下载歌词 |
| 基础插件 | `IPlugin` | 插件生命周期管理 |

`ProtocolType` 枚举已预留：`WebDAV(0)`, `Navidrome(1)`, `SMB(2)`, `DLNA(3)`, `FTP(4)`, `NFS(5)`

---

## 📋 待完成功能

### P1 — 重要功能缺失

| 功能 | 说明 |
|------|------|
| WebDAV 音频扫描/播放 | PROPFIND/文件读取已实现，音频全流程待完善 |
| 自定义播放列表 | 数据表已建，无 UI 交互 |
| 离线缓存下载 | CachedSong 表已建，下载逻辑未实现 |
| Album/Artist 网格浏览 | 数据层方法存在，无 UI |
| 歌曲长按菜单 | SongAdapter 未实现 Context Menu |

### P2 — 增强功能

| 功能 | 说明 |
|------|------|
| 深色模式 | 仅明亮主题 |
| 均衡器 | Android AudioEffect 未集成 |
| 睡眠定时器 | — |
| 桌面小部件 | App Widget |
| Android Auto | — |
| 歌曲元数据编辑 | 无 ID3 标签编辑 |
| 状态栏歌词 | — |

### P3 — 协议扩展

| 协议 | 状态 |
|------|------|
| SMB | 枚举已定义 |
| FTP | 枚举已定义 |
| DLNA | 枚举已定义 |
| NFS | 枚举已定义 |

---

## 📄 开源协议

MIT License

---

**🐾 猫爪音乐 — 让音乐更可爱 🎵**
