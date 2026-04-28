# 🐾 猫爪音乐 (CatClaw Music)

简洁可爱的 Android 音乐播放器，少女风设计，支持本地播放 + Navidrome 网络播放。

![平台](https://img.shields.io/badge/平台-Android-green)
![.NET](https://img.shields.io/badge/.NET-9.0-512bd4)
![语言](https://img.shields.io/badge/C%23-12.0-blue)
![协议](https://img.shields.io/badge/协议-MIT-orange)

---

## ✨ 功能特性

| 特性 | 状态 | 说明 |
|------|:--:|------|
| 🎵 本地音乐扫描 | ✅ | 自动扫描 Music/Download 目录，用 TagLibSharp 读取元数据，存入 SQLite |
| 🔐 运行时权限请求 | ✅ | Android 13+ READ_MEDIA_AUDIO / 旧版 READ_EXTERNAL_STORAGE |
| ▶️ 音频播放 | ✅ | Android MediaPlayer 封装，支持播放/暂停/Seek/音量 |
| 🎶 LRC 歌词 | ✅ | 读取嵌入歌词 + 同名 .lrc 文件，自动滚动+点击跳转 |
| 🔀 播放模式 | ✅ | 顺序 / 随机(Fisher-Yates) / 单曲循环 / 列表循环 |
| 📋 播放队列 | ✅ | 历史记录栈，支持上一曲/下一曲/插播 |
| 🔍 搜索 | ✅ | 本地数据库实时搜索，按标题/艺术家/专辑模糊匹配 |
| 📂 播放列表 | ✅ | 系统默认列表（最近播放/收藏） |
| ☁️ WebDAV | 🔧 | 配置页面 OK，协议实现待引入 NuGet |
| 🎵 Navidrome | ✅ | Subsonic API 客户端、ping/搜索/流媒体/封面/歌词 |
| 🖼️ 封面提取 | ✅ | 从音频文件内嵌封面提取 TagLib byte[] |
| 📊 数据库 | ✅ | 8 张表 + 索引，SQLite 持久化 |
| 🎨 浅色 UI | ✅ | 猫爪粉配色 #FF7BAC，暖粉白背景 #FFF5F7，站酷快乐体标题 |
| 🌙 深色模式 | 🚫 | 已锁定浅色模式 |
| 🔲 自定义图标启动 | ✅ | 原生 Android mipmap 图标 + drawable 全屏启动画面 |

---

## 📱 界面预览

| 音乐库 | 正在播放 | 播放列表 | 搜索 | 设置 |
|:---:|:---:|:---:|:---:|:---:|
| 本地/网络分页 | 封面+歌词+进度 | 系统默认列表 | 实时搜索 | 二级菜单网络配置 |

---

## 🏗 项目结构

```
CatClawMusic/
├── CatClawMusic.Core/                  # 核心库
│   ├── Models/
│   │   ├── Song.cs                     # 歌曲模型 (Id/Title/Artist/Album/Duration/Source...)
│   │   ├── Album.cs                    # 专辑模型
│   │   ├── Playlist.cs                 # 播放列表模型
│   │   ├── Lyrics.cs                   # LRC 歌词模型 (Metadata/Lines/TimeSpan)
│   │   └── ConnectionProfile.cs        # 连接配置 (ProtocolType 枚举：WebDAV/Navidrome/SMB/DLNA/FTP/NFS)
│   ├── Interfaces/
│   │   ├── IAudioPlayerService.cs      # 音频播放接口 (Play/Pause/Seek/Volume/Events)
│   │   ├── IMusicLibraryService.cs     # 音乐库管理接口 (Scan/Search/AlbumCover)
│   │   ├── ILyricsService.cs           # 歌词服务接口 (LRC 解析/多源查找)
│   │   ├── INetworkFileService.cs      # 网络文件服务接口 (WebDAV 文件系统)
│   │   ├── ISubsonicService.cs         # Subsonic/Navidrome API 接口
│   │   ├── INetworkMusicService.cs     # 网络音乐工厂接口 (协议分发)
│   │   ├── IPermissionService.cs       # 权限服务接口
│   │   └── IPlugin.cs                  # 插件体系 (预留)
│   └── Services/
│       ├── PlayQueue.cs                # 播放队列 (4 种模式 + Fisher-Yates 洗牌)
│       ├── LyricsService.cs            # LRC 解析 + 嵌入歌词 + 同名文件查找
│       ├── MusicUtility.cs             # 工具类 (秒→时间/字符串截取/目录扫描)
│       └── TagReader.cs                # TagLibSharp 封装 (标签/封面/歌词/批量扫描)
│
├── CatClawMusic.Data/                  # 数据层
│   ├── MusicDatabase.cs                # SQLite 8 表 + 索引 + CRUD
│   ├── MusicLibraryService.cs          # IMusicLibraryService 实现 (扫描+DB 存储)
│   ├── WebDavService.cs                # WebDAV 协议 (TODO: 引入 NuGet)
│   ├── SubsonicService.cs              # Navidrome 客户端 (MD5 token/JSON 解析)
│   └── NetworkMusicService.cs          # 协议路由器 (ProtocolType switch)
│
├── CatClawMusic.UI/                    # UI 层
│   ├── Pages/
│   │   ├── LibraryPage.xaml{.cs}       # 音乐库页 (权限请求卡片 + 歌曲列表)
│   │   ├── NowPlayingPage.xaml{.cs}    # 播放页 (封面+歌词+进度+音量+收藏)
│   │   ├── PlaylistPage.xaml{.cs}      # 播放列表页
│   │   ├── SearchPage.xaml{.cs}        # 搜索页 (实时搜索+结果播放)
│   │   ├── SettingsPage.xaml{.cs}      # 设置主页 (菜单→子页)
│   │   ├── WebDavSettingsPage.xaml{.cs}# WebDAV 二级配置页
│   │   └── NavidromeSettingsPage.xaml{.cs}# Navidrome 二级配置页
│   ├── ViewModels/
│   │   ├── LibraryViewModel.cs         # 权限检查→扫描→加载
│   │   ├── NowPlayingViewModel.cs     # 播放控制+歌词同步+音量+收藏
│   │   ├── SettingsViewModel.cs        # 缓存/播放设置
│   │   ├── SearchViewModel.cs          # 搜索逻辑
│   │   ├── PlaylistViewModel.cs        # 播放列表管理
│   │   ├── WebDavSettingsViewModel.cs  # WebDAV 测试/保存
│   │   └── NavidromeSettingsViewModel.cs# Navidrome 测试/保存
│   ├── Platforms/Android/
│   │   ├── AudioPlayerService.cs       # MediaPlayer 封装 (IDisposable/Volume)
│   │   ├── PermissionService.cs        # MAUI Permissions API 封装
│   │   ├── MainActivity.cs             # Activity 入口
│   │   ├── MainApplication.cs
│   │   ├── AndroidManifest.xml         # 权限声明+图标+主题
│   │   └── Resources/                  # 原生图标/启动画面资源
│   ├── Resources/
│   │   ├── app_icon.png / splash.png   # 图标和启动画面
│   │   └── 站酷快乐体2016修订版.ttf / 298-CAI978.ttf
│   ├── App.xaml                        # 全局样式 (浅色主题/卡片/按钮/字体)
│   ├── AppShell.xaml                   # Tab + Route 导航
│   └── MauiProgram.cs                  # DI 注册 (Singleton + Transient)
│
└── CatClawMusic.sln
```

---

## 🔧 技术栈

| 层 | 技术 | 版本 |
|---|---|---|
| 框架 | .NET MAUI | 9.0 |
| 语言 | C# | 12.0 |
| 数据库 | SQLite (sqlite-net-pcl) | 1.9.172 |
| MVVM | CommunityToolkit.Mvvm | 8.2.2 |
| 标签 | TagLibSharp | 2.3.0 |
| 播放器 | Android.Media.MediaPlayer | Android 35 |
| 权限 | MAUI Permissions API | — |

---

## 📝 开发进度

| 阶段 | 内容 | 状态 |
|------|------|:--:|
| M1 | 项目搭建 + 基础架构 | ✅ |
| M2 | 本地音乐扫描 + 权限 + 基础播放 | ✅ |
| M3 | 播放界面 + 歌词 + 播放列表 + 搜索 | ✅ |
| M4 | UI 主题重设计（猫爪少女风） | ✅ |
| M5 | Navidrome/Subsonic 协议适配 | ✅ |
| M6 | WebDAV 协议实现 | 🔧 |
| M7 | 设置页重构（二级菜单） | ✅ |
| M8 | 图标 + 启动画面 | ✅ |
| M9 | SMB / DLNA / FTP / NFS 协议 | ⏳ |
| M10 | 测试 + Bug 修复 + 发布 | ⏳ |

---

## 🖥 架构

```
┌─────────────────────────────────────────┐
│  CatClawMusic.UI (.NET 9 Android)       │
│  Pages ← ViewModels ← DI                │
│  Platforms/Android/*                     │
├─────────────────────────────────────────┤
│  CatClawMusic.Data (.NET 9)             │
│  MusicDatabase | SubsonicService         │
│  WebDavService | NetworkMusicService    │
├─────────────────────────────────────────┤
│  CatClawMusic.Core (.NET 9)             │
│  Models | Interfaces | Services          │
└─────────────────────────────────────────┘
```

**DI 注册树**：
```
Singleton: MusicDatabase, ISubsonicService, INetworkFileService,
           INetworkMusicService, IAudioPlayerService, ILyricsService,
           IMusicLibraryService, IPermissionService, PlayQueue

Transient: LibraryVM, NowPlayingVM, SettingsVM, SearchVM,
           PlaylistVM, WebDavSettingsVM, NavidromeSettingsVM
           + 7 Pages
```

---

## 🚀 编译运行

```bash
# 环境要求
- Visual Studio 2022 17.8+ / .NET 9 SDK
- Android SDK API 35+

# 克隆
git clone https://github.com/yourusername/CatClawMusic.git
cd CatClawMusic

# 编译
dotnet build CatClawMusic.UI/CatClawMusic.UI.csproj

# 或直接打开 .sln 在 Visual Studio 运行
```

---

## 📄 开源协议

MIT License © 2026 CatClaw Music

---

## 💬 交流群

> ₍˄·͈༝·͈˄*₎◞ ̑̑

加入猫爪音乐交流群一起讨论喵~

**QQ 群：855383639**

---

**🐾 猫爪音乐 — 让音乐更可爱 🎵**
