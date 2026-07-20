# 🐾 猫爪音乐 (CatClaw Music)

> 萌系 Android 音乐播放器 · .NET 10 + C# 13 + MAUI 原生开发

<div align="center">

![平台](https://img.shields.io/badge/平台-Android-green)
![.NET](https://img.shields.io/badge/.NET-10.0-512bd4)
![语言](https://img.shields.io/badge/C%23-13.0-blue)
![版本](https://img.shields.io/badge/版本-1.6.6-ff69b4)
![最低版本](https://img.shields.io/badge/最低版本-Android%2012%20(API%2031)-orange)
![协议](https://img.shields.io/badge/协议-MIT-yellow)

[![QQ交流群](https://img.shields.io/badge/QQ交流群-855383639-7B68EE?style=for-the-badge&logo=tencentqq&logoColor=white)](https://qm.qq.com/q/Fhu3IEzqa4)

</div>

---

## ✨ 核心亮点

- **5 页 Tab 架构**：Android 原生 ViewPager2 水平滑动，GPU 合成零卡顿
- **ExoPlayer 播放引擎**：FFmpeg 软解兜底，FLAC/APE/DSD 等 26 种格式全支持
- **逐字 KTV 歌词**：Canvas ClipRect 像素级渐变高亮，TTML/AMLL/LRC 多格式兼容
- **桌面悬浮歌词**：可拖拽锁定，单行跑马灯 / 双行 KTV 切换
- **AI 对话式搜索**：18 个 Agent 工具，8 个内置 LLM 供应商，猫娘人格
- **Navidrome / WebDAV / SMB**：三种远程协议，增量扫描 + 流媒体播放
- **音效系统**：5 频段均衡器 + 低音增强 + 环绕声 + 混响，12 种预设
- **插件体系**：歌词/封面/协议/音频增强/菜单 5 种插件接口，GitHub 安装
- **动态主题**：封面取色 + 流光背景 + 5 色主题无重启切换
- **备份恢复**：6 类数据 ZIP 打包，跨设备智能匹配

---

## 📱 截图

<div align="center">

| 播放页 | 歌词页 | 发现页 |
|:---:|:---:|:---:|
| ![](images/213D14E63AAD1FD2FB2431EBDE73589C.jpg) | ![](images/e4fc2f068444f8a1e1b82338bf5fa380.jpg) | ![](images/a62d0758c743118ed18ef74234f1f7b3.jpg) |

| 歌单 | 音乐库 | 艺术家 |
|:---:|:---:|:---:|
| ![](images/714ab1a33c755ee2066e232b640ec131.jpg) | ![](images/c618969189f5baec852f3186c4852e3b.jpg) | ![](images/a9bcd724c3a3ba7668b0c29e473b2151.jpg) |

</div>

---

## 🏗️ 项目结构

```
CatClawMusic/
├── CatClawMusic.Core/         # 核心层：接口、模型、服务、AI Agent
├── CatClawMusic.Data/         # 数据层：SQLite、Navidrome、WebDAV、SMB、爬虫
└── CatClawMusic.Maui/         # UI 层：MAUI 页面、ViewModel、Android 平台代码
```

**技术栈**：.NET 10 · C# 13 · MAUI 10 · ExoPlayer 1.10 · CommunityToolkit.Mvvm · TagLibSharp · SQLite · SMBLibrary · NativeAOT

---

## 🎵 功能总览

### 本地音乐
SAF 文件夹选择 · MediaStore 扫描 · 三路径策略 · 递归扫描 26 种音频格式 · TagLibSharp 元数据 · 增量入库 · LruCache 封面极速加载

### 音频播放
ExoPlayer + FFmpeg 转码 · 流媒体 · Basic Auth · WakeLock 保活 · 音频焦点管理 · 频谱可视化 · 睡眠定时 · 播放状态持久化

### 音效
5 频段 Equalizer · BassBoost · Virtualizer · PresetReverb · 12 种预设一键切换

### 播放队列
顺序/循环/单曲/随机 · Fisher-Yates 洗牌 · 历史栈回退 · O(1) 歌曲查找

### 歌词
LRC/TTML/AMLL 解析 · 多源三级回退 · 编码自适应 · 逐字渐变高亮 · 全屏毛玻璃 · 拖拽定位 · 双语歌词 · 横屏模式

### 桌面歌词
悬浮窗拖拽锁定 · 单行跑马灯 · 双行 KTV · 字体颜色透明度可配 · 通知栏快捷控制

### 通知栏 / MediaSession
HyperOS 5 按钮通知 · MediaStyle 大封面 · 蓝牙/车载/穿戴设备控制 · foregroundService 保活

### 主题
5 色无重启切换 · 深色/浅色/跟随系统 · 封面取色 MaterialYouPalette · 流光背景 ValueAnimator · 毛玻璃卡片

### 远程协议
| 协议 | 能力 |
|------|------|
| Navidrome | 扫描 · 封面 · 歌词 · 收藏同步 · 流媒体 · Token 认证 |
| WebDAV | PROPFIND · 递归扫描 · GET 流播放 · Basic 认证 · SSL 跳过 |
| SMB/CIFS | 目录浏览 · 递归扫描 · 域/NTLM 认证 · 流播放 |

### AI 探索
对话式布局 · 8 个 LLM 供应商 · 18 个 Agent 工具 · 猫娘人格 · 流式文本 · 多配置故障转移 · 向导式添加

### 插件
5 种接口（歌词/封面/协议/音频增强/菜单）· 本地 .dll/.ccp 安装 · GitHub Release 安装 · 反射兼容适配 · 子插件支持

### 备份恢复
ZIP 打包 6 类数据 · 分类独立恢复 · 进度实时上报 · 跨设备歌曲匹配 · 旧版 .json 兼容

### 其他
艺术家元数据爬虫（网易云/AI/QQ音乐）· 每日推荐引擎 · 权限统一管理 · 自定义启动页（API/本地图片）

---

## 🗄️ 数据库

SQLite + WAL · 11 表 · v5 迁移 · 多艺术家多对多关联 · 后台自动修复

---

## 📜 License

MIT
