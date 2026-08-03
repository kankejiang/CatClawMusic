# CatClaw Music v1.7.8 更新日志

## 🎯 横竖屏体验 & 布局优化

- **横屏页面重构**：歌曲/艺术家/专辑页统一为 4 列网格，筛选排序改用 FlexLayout 两列排列，充分利用横屏宽度
- **专辑列表优化**：A-Z 字母索引替换年代分组，统一排序逻辑（英文优先、中文按 Unicode、符号最后）
- **NowPlayingPage 横屏适配**：SafeArea 适配车机 dock 栏，封面按三边定正方形，横屏歌词改用 PushAsync FullLyricsPage 复用竖屏滚动逻辑
- **歌词对齐**：跟随用户设置，默认对齐从居中迁移为居左
- **PlaylistDetailPage**：重构布局与交互，新增横屏适配
- **DesktopAlbumsPage / DesktopAllSongsPage / DesktopArtistsPage**：横屏布局适配，同步 LetterRail 索引和列表项样式
- **MainPage / DesktopMainPage**：改为 Singleton 避免页面重建，改用 HandlerChanged 管理事件订阅生命周期

## 📱 底部安全区 & 全面屏适配

- **Android 三键导航栏修复**：TabBar 动态计算高度 `56 + BottomInset`，将图标行提升到导航栏上方，避免图标被遮挡（手势导航设备无影响，行为不变）
- **车机屏幕适配**：EdgeToEdge inset 增加 `NavigationBars`/`CaptionBar` 类型，正确读取车机底部 dock 栏高度
- **嵌入子页面底部安全区**：全部歌曲/艺术家/专辑页嵌入时保留底部 inset，避免被车机 dock/导航栏遮挡
- **DesktopLyricPage 底部安全区**：预留系统栏 inset
- **AI 聊天模式**：隐藏 TabBar 后为页面区预留底部 inset，防止输入栏被导航键遮挡

## 🪟 Windows 平台修复

- **任务栏高度测量**：通过系统 API（MonitorFromWindow/GetMonitorInfo/GetDpiForWindow）测量任务栏高度，写入 SafeAreaHelper.BottomInset
- **全屏页面底部安全区**：NowPlayingPage/FullLyricsPage 在 Windows 下预留底部 inset，防止控件被任务栏遮挡
- **桌面主页面底部安全区**：DesktopMainPage RootGrid 在 Windows 也应用底部 inset
- **XAML 编译器修复**：修复 Windows App SDK 1.7 XAML 编译器退出码问题（包装脚本 + output 修复 target）
- **启动崩溃日志**：新增 Windows 启动崩溃日志

## 🎵 歌词体验修复

- **横竖屏切换后歌词不再滚动**：修复根切换销毁 ViewPager2 时 BeginInteraction 令牌泄漏，导致 IsUserInteracting 永久卡 true（双保险：MainPage Unloaded 释放令牌 + InteractionStateService 看门狗强制自愈）
- **歌词滚动定位**：ScrollToLine 不再依赖歌词行原生视图，改用纯 MAUI 坐标 + ComputeVerticalScrollOffset 计算位移，彻底解决"RecyclerView does not support scrolling to an absolute position"问题
- **横竖屏歌词坐标**：改用原生 GetLocationOnScreen 获取精确坐标
- **歌词显示强制同步**：解决新页面歌词/进度不同步问题，横竖屏切换后显式复位交互状态

## ⚡ 性能 & 内存优化

- **启动优化**：延迟非关键 I/O，MainPage 仅预加载目标 tab，减少无效 I/O，提升首屏加载速度
- **Android Bitmap 缓存**：降至 32MB，支持渐进式 LRU 驱逐，避免解码风暴
- **InteractionStateService 看门狗升级**：全计数器兜底（触摸/滚动/令牌全清），结束事件刷新活动时间，彻底修复交互状态泄漏

## 🎨 图标 & 主题适配

- **ThemedIconExtension**：新增主题图标扩展，XAML 中的 AppThemeBinding 图标源通过 ImageSourceHelper 路由，修复 WinUI 下 SVG 图标不可见问题
- **浅色模式图标**：新增 `ic_arrow_left_light`、`ic_arrow_up`、`ic_arrow_up_light`、`ic_back_light`、`ic_share`、`ic_share_light`
- **纯黑 SVG 修复**：ic_album、ic_arrow_forward、ic_arrow_left、ic_check 改为白色，确保在深色/渐变背景上可见
- **LibraryCardItem.IconSource**：从 string 改为 ImageSource，通过 helper 解析

## 🔧 其他修复

- **Debug 构建**：关闭 FastDeploy 避免物理机部署 NO_CERTIFICATES 错误
- **global.json**：锁定 .NET 10 SDK（latestFeature）
- **构建脚本**：新增 `XamlCompilerWrapper.bat` 和 `build-release.ps1`
- **AlbumsPage/ArtistsPage**：修复 x:DataType 命名空间，补充编译绑定与 EmptyView
- **AlbumMorePopup**：新增弹窗操作

---

**67 个文件变更，+3040 / -1204 行**

**完整 Commit 列表**：`v1.7.6...v1.7.8`