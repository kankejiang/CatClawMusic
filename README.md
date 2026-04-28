# 猫爪音乐 (CatClaw Music)

简洁可爱的跨平台音乐播放器，支持本地播放和 NAS 网络播放。

![猫爪音乐](https://img.shields.io/badge/平台-Android-green)
![.NET 9 MAUI](https://img.shields.io/badge/.NET-9.0-512bd4)
![MIT 协议](https://img.shields.io/badge/协议-MIT-blue)

---

## ✨ 功能特性

- 📱 **本地音乐库管理**：扫描、索引、元数据读取
- 🌐 **WebDAV 网络播放**：支持 NAS 网络音乐播放
- 🎵 **多种音频格式**：MP3、FLAC、AAC、WAV、OGG、M4A
- 📋 **播放列表管理**：本地 + 网络
- 🔍 **搜索与过滤**：本地 + 网络
- 🎶 **LRC 歌词支持**：本地歌词文件，网络匹配预留插件接口
- 📊 **播放统计**：最近播放、播放次数、播放时长统计
- 💾 **WebDAV 缓存**：边下边播，1GB-15GB 可配置
- 🎲 **随机播放**：Fisher-Yates 洗牌算法，不重复播放
- 🎨 **简洁可爱 UI**：自定义设计语言，柔和配色

---

## 📷 截图

（待添加）

---

## 🚀 快速开始

### 环境要求
- Visual Studio 2022 17.8+
- .NET 9 SDK
- Android SDK（Android 10+）

### 编译运行
1. 克隆仓库：
   ```bash
   git clone https://github.com/yourusername/CatClawMusic.git
   ```

2. 打开 `CatClawMusic.sln` 解决方案

3. 设置 `CatClawMusic.UI` 为启动项目

4. 选择 Android 模拟器或真机，点击运行

---

## 📁 项目结构

```
CatClawMusic/
├── CatClawMusic.Core/             # 核心库（模型、接口、服务）
│   ├── Models/                    # 数据模型（Song、Album、Playlist、Lyrics）
│   ├── Interfaces/               # 服务接口（IAudioPlayerService、IMusicLibraryService 等）
│   └── Services/                 # 核心服务（PlayQueue、LyricsService）
│
├── CatClawMusic.Data/             # 数据访问层
│   ├── MusicDatabase.cs          # SQLite 数据库上下文
│   └── WebDavService.cs         # WebDAV 服务实现
│
├── CatClawMusic.UI/               # UI 层（MAUI）
│   ├── Pages/                     # 页面
│   │   ├── LibraryPage.xaml      # 音乐库页
│   │   ├── NowPlayingPage.xaml  # 当前播放页
│   │   ├── PlaylistPage.xaml    # 播放列表页
│   │   ├── SearchPage.xaml      # 搜索页
│   │   └── SettingsPage.xaml    # 设置页
│   ├── Controls/                  # 自定义控件
│   ├── ViewModels/                # ViewModels
│   ├── Platforms/                # 平台特定实现
│   │   └── Android/             # Android 平台
│   ├── App.xaml                 # 应用资源
│   ├── AppShell.xaml            # 导航框架
│   └── MauiProgram.cs          # 依赖注入配置
│
└── docs/                         # 文档
    └── 技术方案.md               # 技术方案文档
```

---

## 🔧 技术栈

| 技术 | 用途 |
|------|------|
| **.NET 9 MAUI** | 跨平台 UI 框架 |
| **C# 12** | 开发语言 |
| **SQLite** | 本地数据库 |
| **TagLib#** | 音频元数据读取 |
| **ExoPlayer** | Android 音频播放引擎 |
| **WebDAV.Client** | WebDAV 协议支持 |
| **CommunityToolkit.MVVM** | MVVM 框架 |

---

## 📝 开发计划

| 阶段 | 内容 | 状态 |
|------|------|------|
| M1 | 项目搭建 + 本地音乐扫描 + 基础播放 | 🔄 进行中 |
| M2 | 播放界面 + 歌词显示 + 播放列表 | ⏳ 待开始 |
| M3 | WebDAV 协议支持 + 网络播放 + 缓存 | ⏳ 待开始 |
| M4 | 搜索优化 + 专辑封面缓存 + 性能优化 | ⏳ 待开始 |
| M5 | 测试 + Bug 修复 + 发布准备 | ⏳ 待开始 |

---

## 🤝 贡献

欢迎提交 Issue 和 Pull Request！

---

## 📄 开源协议

本项目采用 MIT 协议开源。

```
MIT License

Copyright (c) 2026 CatClaw Music Contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## 📧 联系方式

- 项目主页：[GitHub](https://github.com/yourusername/CatClawMusic)
- 问题反馈：[Issues](https://github.com/yourusername/CatClawMusic/issues)

---

**猫爪音乐** - 让音乐更可爱 🐾🎵
