<div align="center">

# 猫爪音乐 · CatClawMusic

_Android 原生音乐播放器 · Kotlin + Jetpack Compose_

> 本地库、NAS 远程库、网易云在线音乐与私人漫游，都在一个玻璃质感的界面里 🐾

[![Release](https://img.shields.io/github/v/release/kankejiang/CatClawMusic)](https://github.com/kankejiang/CatClawMusic/releases/latest)
[![License](https://img.shields.io/github/license/kankejiang/CatClawMusic)](./LICENSE.txt)
[![Platform](https://img.shields.io/badge/Android-12%2B%20(arm64)-3DDC84)](https://github.com/kankejiang/CatClawMusic/releases/latest)
[![QQ 交流群](https://img.shields.io/badge/QQ%E7%BE%A4-855383639-blue)](https://qm.qq.com/q/Fhu3IEzqa4)

</div>

---

## 这是什么

猫爪音乐是一个**只做 Android 的原生音乐播放器**，包名 `com.catclaw.music.kt`，最低 Android 12（API 31），发布 arm64 单 ABI 安装包。

2026 年 11 月起，仓库已**完全移除 .NET MAUI 实现**（Windows 桌面端与 MAUI Android 目标），只保留原生 Kotlin / Compose 工程 `CatClawMusic.Android`。历史版本仍可在 GitHub Releases 里取到，但不再维护。

## 功能

### 音乐库
- **本地扫描**：MediaStore 与 SAF 自选文件夹双通道，流式读标签 + 并发解析（613 首约 2.6 秒）
- **标签兜底**：缺歌手时从「歌手 - 歌名」文件名里拆；识别侧车 `.lrc`
- **整理**：歌曲 / 专辑 / 艺术家 / 收藏 / 最近播放，二级页统一大封面头
- **清空音乐库**：只删扫描记录，不动手机里的音频文件，保留歌单与收藏

### 远程音乐
- **WebDAV / Navidrome / SMB** 三类源，连接管理支持编辑、删除与连通性测试
- 自签证书可用（NAS 常见）；`Depth: infinity` 一次列全库，被拒时自动回退逐层遍历
- **播放时自动补全元数据**：播到缺时长的远程曲，后台按 Range 取文件头补全，不用手动点
- 远程曲流式播放 + 磁盘缓存，支持离线下载与断点续传；缓存占用可在设置里查看并清理

### 在线音乐（网易云）
- eapi 通道：歌单浏览、搜索（歌曲 / 歌手 / 专辑，分页）、在线播放
- 登录：手机号验证码与账号密码，会话自动续期、听歌打卡
- 音质档位：无损 / 320k / 128k，取链失败自动降档；会员曲不再只给 30 秒
- **私人漫游（FM）**：循环凑批、3 档推荐模式、42 个官方场景码、漫游态持久化、红心与垃圾桶
- 红心双向同步（播放页、列表、通知栏），只写「我喜欢的音乐」

### 播放与音效
- Media3 ExoPlayer 1.11.1：渐进式缓存、下一首预加载、CacheDataSource 边下边播
- **FFmpeg 软解兜底**（NextLib）：平台解不了的格式（ALAC / DTS / TrueHD 等）交给软解，平台能解的继续走硬解
- 音效：5 段均衡器（竖向推子 + 实时响应曲线 + 预设）、低音增强、响度增益、虚拟环绕声、淡入淡出
- 通知栏 / 锁屏：媒体条封面、红心与播放页同源
- 横屏：左侧玻璃侧栏 + 播放页双栏布局

### 界面
- 玻璃体系：`backdrop` 录制层 + `miuix-blur` 液态玻璃 + `haze`，底栏与迷你条是独立消费者
- 背景三选一：自定义图 / 雾面流光（常驻单实例，换页不重建）/ 莫奈取色
- 全屏歌词：逐字高亮、自动钉行、点击跳转，字号 / 对齐 / 翻译 / 罗马音可调
- 歌词源插件（lyrico）：订阅源匹配、候选筛选、写入本地歌词文件

## 技术栈

| 层 | 选型 |
| --- | --- |
| 语言 / UI | Kotlin 2.4.20 · Jetpack Compose（BOM 2026.09.00）· Material3 |
| 构建 | AGP 9.4.1 · Gradle 9.8 · JDK 21 · compileSdk 37 · minSdk 31 |
| DI | Hilt 2.60.1 |
| 存储 | Room 2.8.5（17 张表，schema 与历史线上库对齐） |
| 播放 | media3 1.11.1（ExoPlayer / Session）· nextlib-media3ext 1.11.1-0.18.0（FFmpeg） |
| 网络 | OkHttp 5.5.0 · SMBJ 0.14.0（SMB）· jcifs-ng（仅列共享） |
| 玻璃 | com.github.kyant0:backdrop · miuix-blur 0.9.4 · haze 1.7.3 |

## 工程结构

```
CatClawMusic.Android/
├── core/           纯 Kotlin：播放队列、歌词引擎、主题算法、插件协议、AI Agent
├── data/           Android 实现：Room、ExoPlayer 服务、WebDAV/Subsonic/SMB 导入、标签解析、下载、备份
├── app/            Compose UI：主壳、播放页、歌词页、发现、歌单、音乐库、设置
├── lyrico/         歌词源插件宿主与匹配逻辑
├── lyrico-audiotag/音频标签 / 歌词读写（NDK）
├── docs-schema/    与历史库逐表逐列的 schema 对照
└── tools/          开发期辅助脚本
```

依赖方向严格自上而下：`app → data → core`。每个页面一个 ViewModel，UI 只读 `StateFlow<XxxUiState>` 并调意图方法，不直接持有 DAO / PlayerController / Settings。规范见 [`docs/Android-MVVM-Architecture.md`](docs/Android-MVVM-Architecture.md)。

## 构建

```bash
cd CatClawMusic.Android

# 调试包（debug 包名多一个 .debug 后缀，可与正式包共存）
./gradlew assembleDebug
# 产物：app/build/outputs/apk/debug/app-debug.apk
```

依赖 JDK 21 与 Android SDK（compileSdk 37）。首次构建会下载 NextLib 的 FFmpeg 原生库（arm64 约 5.7 MB）。

## 发布

```powershell
# 按 build.gradle.kts 里的版本号走完整流程：读版本 → 校验前置条件 → 构建 arm64 签名包
#   → apksigner / aapt2 校验 → 提交版本号与 release notes → 打 tag → 推 origin + gitee
#   → gh release create 上传 APK
.\release-android.ps1

.\release-android.ps1 -DryRun      # 只构建，不上传
.\release-android.ps1 -SkipBuild   # 复用已有 APK
```

签名使用根目录 `catclaw.keystore`（alias `catclaw`），说明见 [`SIGNING.md`](SIGNING.md)。发布资产命名 `catclaw.music-<版本>-arm64.apk`，更新日志写在 `release-notes-<版本>.md`。

## 文档

| 文件 | 内容 |
| --- | --- |
| [`docs/Android-MVVM-Architecture.md`](docs/Android-MVVM-Architecture.md) | MVVM 分层与闸门测试约定 |
| [`docs/网易云音乐移植计划.md`](docs/网易云音乐移植计划.md) | 网易云接口与实现进度 |
| [`docs/lyrico-验收清单.md`](docs/lyrico-验收清单.md) | 歌词源插件验收项 |
| `release-notes-*.md` | 各版本更新日志 |

## 许可证

[MIT](./LICENSE.txt)
