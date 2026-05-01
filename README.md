# 🐾 猫爪音乐 (CatClaw Music)

萌系 Android 本地音乐播放器，纯原生 Android 开发，支持 Flac/WAV/MP3 播放、频谱可视化、LRC 歌词滚动。

![平台](https://img.shields.io/badge/平台-Android-green)
![.NET](https://img.shields.io/badge/.NET-9.0-512bd4)
![语言](https://img.shields.io/badge/C%23-12.0-blue)

---

## ✨ 功能特性

| 特性 | 状态 | 说明 |
|------|:--:|------|
| 🎵 音乐扫描 | ✅ | SAF 多文件夹 + MediaStore 双模式，存入 SQLite |
| ▶️ 音频播放 | ✅ | Android MediaPlayer，FLAC/MP3/WAV |
| 🎹 频谱可视化 | ✅ | 512 点 FFT + A加权 + 对数频段 + 包络跟随 + 峰值保持 |
| 🎶 LRC 歌词 | ✅ | 嵌入歌词 + SAF content URI .lrc 文件读取，三行滚动 |
| 🔀 播放模式 | ✅ | 顺序 / 随机 / 单曲循环 / 列表循环 |
| 📋 播放队列 | ✅ | 全量队列 + 播放历史持久化（重启恢复） |
| 🔍 搜索 | ✅ | 本地数据库实时搜索（标题/艺术家/专辑） |
| 🖼️ 封面 | ✅ | TagLibSharp 提取 + 缓存 + 默认封面兜底 |
| 📊 播放统计 | ✅ | 播放次数 + 收藏 + 最近播放持久化 |
| 🎨 UI 风格 | ✅ | 扁平毛玻璃卡片 + 紫粉渐变主题 |
| 🌙 深色模式 | ❌ | 锁定浅色 |
| ☁️ 网络播放 | 🔧 | Subsonic/WebDAV 协议层预留 |

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
| **来源** | 嵌入歌词 → 同名 .lrc（SAF content URI） |
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

## 🚀 编译

```bash
dotnet build CatClawMusic.UI/CatClawMusic.UI.csproj
```

要求：Visual Studio 2022 17.8+ / .NET 9 SDK / Android SDK API 35+

---

## 📄 开源协议

MIT License © 2026 CatClaw Music

---

**🐾 猫爪音乐 — 让音乐更可爱 🎵**
