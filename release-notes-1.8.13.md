# CatClaw Music 1.8.13 更新日志

## 🚀 平台升级：.NET 11 RC1

- 项目全面迁移到 **.NET 11 RC1**（SDK `11.0.100-rc.1.26425.128`），C# 15 语言版本
- 所有项目的 SDK 版本、目标框架、语言版本与 NuGet 包版本统一对齐
- `CatClawMusicServer` 从 net8.0 跨版本升级到 net11.0，EF Core / JwtBearer 同步升级
- 桌面壳层导航架构重构（见下）

## ✨ 启动流程重构

- **冷启动改用应用内启动加载页**，系统启动画面提前淡出，消除启动过程中的画面跳变
- 系统启动画面 keep-on-screen 到主界面就绪，**全程仅一个启动画面**
- 原生占位替代 MAUI 托管启动页，启动门控缩短至核心就绪，冷启动更快
- 启动画面美化：旋转黑胶动画图标 + 底部品牌字样 + 温柔淡出
- 修复启动画面淡出时闪现旧图标；启动图标回退应用图标本体

## 🖥️ 桌面端子页面内嵌

- **插件等外部模块的页面改为内嵌主内容区**，不再走整窗模态推页（模态会盖住侧栏与底部播放条）
- 新增 `ISubPageHost` / `SubPageHost` 宿主抽象，宿主未注册时自动回退原有 Shell/模态方式
- 配套调整桌面导航与过渡动画、毛玻璃背景、主题服务、应用重启与 WebView 登录页
- 新增 `NavDiagnostics` 导航诊断能力

## 🎨 Windows 视觉打磨

### 主题
- 深浅主题统一：深色背景不再额外加黑，歌词统一灰 + 白高亮
- 浅色模式背景改为封面原色柔和调和，歌词颜色随主题切换
- 修复深色模式侧栏与底部播放条发灰（定位到真正的壳层 `DesktopBlankPage`）

### 背景
- 修复切歌后动态背景不实时更新的真正根因：`CurrentCoverPath` 处理在 WINDOWS 分支不可达
- 修复 binding struct 更新链不可靠导致的背景不刷新
- 动态背景去遮罩：解决浅色太亮 / 深色太暗

### 歌词
- **修复歌词滚动/放大/行距/模糊过渡无动画**：MAUI 11 WinUI Ticker 不逐帧驱动
- 歌词 tween 帧率改为跟随显示器刷新率（`DispatcherTimer` 换 `CompositionTarget.Rendering`）

## 🔌 插件系统

- **JS 运行时（Jint / Acornima）上收宿主统一持有**，插件不再各自加载运行时，降低内存占用
- 插件市场安装成功后补上统一重启提示
- 移除 AI 歌单与 AI 每日推荐功能（相关页面、ViewModel、服务一并清理）

## 🐛 稳定性修复

- 修复音乐库-首页切换闪退：`DonutDrawable` 近零尺寸传给 Win2D 导致崩溃
- 修复歌词署名行参与原文/译文配对，导致正文首句被吞成译文
- 修复同文种同刻行被错误配对为译文，快节奏堆叠句恢复独立行
- 修复 WebDAV 播放后元数据不回填（显示"未知艺术家"）
- 修复 WebDAV / SMB 封面与歌词获取，网络扫描提速

## ⚡ 构建与工程

- 移除 GitHub Actions 工作流，改为本地 `build-release.ps1` / `build-win-release.ps1` 出包
- 补全 AI 工具产物忽略规则，`.workbuddy/` 整目录忽略

---

**下载**

| 平台 | 文件 | 说明 |
|------|------|------|
| Android | com.catclaw.music-Signed.apk | 直接安装（arm64） |
| Windows | catclaw.music-1.8.13-Setup.exe | 安装程序（x64，self-contained） |

- 版本号提升至 1.8.13（Android versionCode 70）
