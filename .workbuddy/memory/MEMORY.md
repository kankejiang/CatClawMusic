# CatClawMusic 项目记忆

## 构建与发布
- 主线入口: `CatClawMusic.Maui/CatClawMusic.Maui.csproj` (v1.6.4 / build 41, UseMaui, 单项目)。旧 `CatClawMusic.UI.csproj`(Xamarin) 已非主线。
- TargetFramework: `net11.0-android` (需 API 37；本机另装 35/36)。
- 默认出 **Release 签名包**(2026-07-08 起): `dotnet publish CatClawMusic.Maui/CatClawMusic.Maui.csproj -c Release -f net11.0-android -p:AndroidSdkDirectory="C:/Users/Administrator/AppData/Local/Android/Sdk" -p:JavaSdkDirectory="C:/Program Files/Microsoft/jdk-21.0.11.10-hotspot/" -p:CatClawStorePass=catclaw123 -p:CatClawKeyPass=catclaw123`
- Debug 仅快速验证编译: 同命令 `-c Debug` (去签名参数)。
- 产出: `CatClawMusic.Maui/bin/Release/net11.0-android/publish/*-Signed.apk` (csproj 无自动复制 target)。
- keystore: alias=catclaw, storepass=keypass=catclaw123, keystore=../catclaw.keystore。

## MAUI 已知坑(高频)
- **Android `LayerType` 命名遮蔽**: 嵌套枚举 vs 同名实例属性冲突，唯一正确是在 `Android.Views.View` 子类体内用简单名 `LayerType`(见 `Platforms/Android/HardwareLayerExtensions.cs` 的 `LayerTypeProbe`)。`Platforms/Android` 下 .cs 必须 `using View = Android.Views.View;` 消解与 MAUI `Controls.View` 歧义。
- **权限元组**: `RequiredPermissions` 元素名 `(string androidPermission, bool isRuntime)` (非旧文档 `showRationale`)。
- **WinUI 图标按钮**: 纯图标无文字按钮用 `<ImageButton>` 而非 `<Button ImageSource>`(后者 Windows 不渲染图标)。XAML 字面量 `Image Source` Windows 也可能不渲染，需后台 `ImageSourceHelper.FromNameOriginal(...)` 显式赋。
- **未打包 Windows `Preferences` 静默失效**: `WindowsPackageType=None` 下 `ApplicationData.Current` 为 null, `Preferences` Set/Get 无操作。自定义文件夹已迁 `FileSystem.AppDataDirectory/custom_music_folders.json`(`CustomFolderStore`)。其他设置(ffmpeg/media_store/saf/主题)若反馈不保存亦需迁移文件存储。
- **Windows 命名空间**: `Platforms/Windows` 下 `Windows.Storage.*` 加 `global::` 前缀;类名勿用 `FolderPicker`(与 MAUI 冲突)。
- **键盘快捷键**: `ContentPage`/`Microsoft.UI.Xaml.Window.KeyDown` 都拿不到;在 `OnAppearing` 对 `Handler.PlatformView as Microsoft.UI.Xaml.UIElement` 订阅 `KeyDown`,按键用 `Windows.System.VirtualKey`, 用完全限定名避免 `using Microsoft.UI.Xaml;` 冲突。
- **顶部导航栏**: `AppShell.xaml` 全局样式对所有 `ContentPage` 设 `Shell.NavBarIsVisible="False"`;底部 TabBar 不受影响。
- **桌面根页**: `#if WINDOWS` 下 `App.xaml.cs CreateWindow` 用 `DesktopMainPage`(1200×800, Min 900×600) 替代手机 `MainPage`+TabBar。

## 歌词
- LRC(标准) + TTML(W3C, v1.5.1+, 逐字时间戳, 支持 .lrc/.ttml/.xml 自动检测)。解析 `LyricsService.cs` / 文件查找 `MusicUtility.cs` / 接口 `ILyricsService.cs`。匹配: 精确 `歌曲名.lrc` → 模糊 `歌曲名*.lrc`。

## 艺术家元数据(MAUI 主线缺口)
- 抓取设施已建好但未接通: `IArtistMetadataScraper` 5 实现(网易云/AI/多源照片已注册 DI;百度百科/豆瓣为孤儿实现未调用)。全仓 `SearchArtistsAsync` 无调用方, `ArtistMatchPage` 为空壳。
- 本地 Artist 表仅存 Name(扫描拆名入库), Gender/Birthday/Region/Description/Cover 不填。`ArtistDetailPage` 现仅 Name+歌曲封面头像。
- 要做资料卡需: ①接通抓取 ②持久化(扩列或 JSON 缓存) ③把 `BaiduBaikeScraper` 重新注册(厚字段最全来源)。

## 猫爪圈跨网 P2P(CatClawMusic.Core/ClawCircle)
- 端口: UDP 发现 37821 / TCP 直传 37822 / HTTP 媒体中心 37823 / STUN 37824。
- 阶段: ①NAS 媒体中心(Navidrome 兼容) ②WS tracker `/ws/clawcircle`(须 `app.UseWebSockets()` 在前) ③UDP NAT 打洞+分块直传。
- 坑: relay 反序列化须 `PropertyNameCaseInsensitive=true`; UDP 片 < 65507(默认 16KB); `SemaphoreSlim(0)` 勿多次 Release。验证 Harness 双节点真实服务端跑通。

## README 约定(公开仓库)
- `README.md` 必须 GitHub 友好纯 markdown: 禁 `<style>`/`<script>`;截图 `<details>` 默认收起;徽章 shields.io 图片;版本以 csproj 为准(v1.6.4)。本地花哨版留 `README.html` 勿覆盖。
- **不要显示更新日志**: 用户要求删除且以后不加回。

## 设计流程约定
- UI 改动一律「先出 HTML 原型确认视觉, 再落地 MAUI 代码」。历史原型在 `docs/`(listening-stats / song-detail / music-library-detail / music-library-overview / artist-detail / artist-list / album-list / now-playing / log-page 等)。品牌: 深空蓝玻璃拟态, 紫蓝渐变 #8C7BFF/#55D6FF/#080B1A, 字体 Space Grotesk + Noto Sans SC。
- 用户偏好: 不要播放控制假组件、不要假数据(本地播放器无法区分性别/地区)、GitHub 友好 markdown。
