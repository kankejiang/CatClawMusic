# 更新日志 (CHANGELOG)

## [1.0.13] - 2026-05-26

### ✨ 流光效果优化

- **柔和流光背景**：从封面主色取色，高亮度低饱和柔和色块缓慢漂移叠加
- **呼吸缩放**：流光支持呼吸式缩放动画
- **暂停跟随**：音乐暂停时流光效果跟随当前状态

### 🎵 播放体验优化

- **重启恢复精确播放列表**：重启后恢复退出时的播放队列，而非加载全部歌曲
- **频谱峰值下落速度降低**：峰值指示器缓缓飘落，视觉效果更舒适
- **频谱/进度条白色硬编码**：避免与背景色融为一体

### ⚡ 性能优化

- **Activity 级禁用 Autofill**：消除切换 Tab 时的 AutofillManager spam 卡顿
- **AudioVisualizerView 零信号快速 idle**：频谱数据全零时停止无意义重绘
- **TransitionToColors 不再每帧重建位图**：动画帧只更新颜色数组，消除 GC 压力
- **VisualizerHelper 预分配缓冲区**：消除每次 FFT 回调的数组分配
- **Paint.Color 使用 struct 赋值**：修复 SetColor(Int64) 的 IllegalArgumentException

### 🏗️ 原生模块

- 新增 C++ 原生库 `libcatclaw_native.so`：FFT 频谱分析、LRC 歌词解析、音频标签读取、颜色提取、音频处理

---

## [1.0.10] - 2026-05-22

### 🎨 弹窗系统重构

- **GlassDialog 统一弹窗类**：创建 `Helpers/GlassDialog.cs`，所有弹窗统一使用毛玻璃圆角卡片样式
- **毛玻璃模糊力度加强**：`SetBackgroundBlurRadius` 从 80→300，模糊效果显著增强（Android 12+）
- **Android 12 以下模糊后备**：新增 `ApplyPreSBlur()` 方法，使用 RenderScript `ScriptIntrinsicBlur` 对 DecorView 截图高斯模糊（15px 模糊半径）
- **背景不透明度提升**：卡片背景从 `#33000000`（20%黑）→ `#CC000000`（80%黑），文字对比度大幅提升
- **背景变暗加强**：`SetDimAmount` 从 0.4→0.55，突出弹窗层次感
- **点击外部关闭**：`SetCanceledOnTouchOutside(true)`，轻触弹窗外区域自动关闭
- **主题色集成**：弹窗按钮、CheckBox、Switch 高亮色均使用当前主题 `ColorPrimary`

### 🎵 歌词系统优化

#### 全屏歌词页 (FullLyricsFragment)
- **高亮只保留当前行**：已唱过和未唱的行统一显示 `#38FFFFFF`（22% 白色），只有当前行纯白高亮 + 放大 4sp
- **歌词字体加粗**：所有歌词行 `SetTypeface(null, TypefaceStyle.Bold)`
- **Span 残留修复**：`HighlightCurrentLine` 中先用 `SetText(plain, BufferType.Normal)` 清除 ForegroundColorSpan，再设置颜色，解决行切换后逐字高亮残留
- **逐字歌词行尾优化**：接近行尾 <200ms 时将当前行所有词完整高亮，避免最后一个词没亮就切行

#### 播放页歌词 (NowPlayingFragment)
- **已唱行淡化**：`_lyricPrev` Alpha 1.0→0.45，`_lyricPrev2` Alpha 0.6→0.35，播放过的行明显变暗，消除高亮残留感
- **播放位置更新频率**：位置定时器从 200ms→100ms，逐字高亮更实时

### ⏱️ 睡眠定时修复

- **「播完整首歌再停止」修复**：原方案监听 `PlaybackState.Stopped` 事件与 `NowPlayingViewModel.Next()` 冲突导致继续播下一首
  - 新方案：在 `NowPlayingViewModel` 新增 `StopAfterCurrentSong` 标志位
  - `OnPlaybackStateChanged` 收到 `Stopped` 时先检查该标志，若为 true 则暂停而非播下一首
  - `ExecuteSleepStop` 简化为设置标志位，移除事件监听

### 📋 音乐库持久化

- **重启自动加载**：`LibraryFragment.OnViewCreated` 末尾新增自动加载逻辑——若当前 Tab 歌曲列表为空，自动调用 `LoadLocalAsync`/`LoadNetworkAsync`
- **权限过期保护**：文件夹权限过期时先加载数据库缓存数据显示，再提示权限过期（而非直接清空）
- **首次启动自动扫描**：有文件夹 URI 但数据库无缓存时，自动触发 `BackgroundScanAsync` 扫描（而非显示"下拉刷新"提示）

### 🔌 插件管理

- **插件卡片布局重构**：`item_plugin_card.xml` 改为上下分层布局
  - 上层：图标 + 插件名/版本/来源标签 + 描述
  - 下层：右对齐「启用」Switch + 卸载按钮
- **卡片高度大幅压缩**：padding 16→8dp，字号整体缩小（图标 24→18sp，名称 15→13sp，描述 12→11sp），高度缩减约 40~50%
- **GitHub 安装 .ccp 格式支持**：错误提示统一更新为「.dll 或 .ccp」

### ✨ CheckBox/RadioButton 主题色

- 所有弹窗中 CheckBox 和 RadioButton 勾选颜色从系统默认（部分设备红色）改为应用当前主题色 `ButtonTintList`
- 涉及：`FullLyricsFragment`（歌词设置）、`CatClawTagMenuContributor`（标签匹配）

### 🔧 Bug 修复汇总

| 问题 | 修复方案 |
|------|---------|
| 睡眠定时「播完整首歌再停止」播完继续下一首 | 新增 `StopAfterCurrentSong` 标志位 |
| 全屏歌词行切换后 Span 残留不刷新 | `SetText(plain, Normal)` 清除 Span |
| 逐字歌词最后一两个字未高亮就切行 | 行尾 200ms 内完整高亮 |
| 播放页已唱行高亮残留 | prev 行 Alpha 降至 0.45/0.35 |
| 弹窗太通透看不清文字 | 背景 `#33`→`#CC`，DimAmount 0.4→0.55 |
| WebDAV 测试连接误报失败 | 使用独立临时 HttpClient，手动处理 301/302 重定向，目录 URL 保留末尾 `/` |
| SMB 浏览显示空文件夹 | 改用 `FileDirectoryInformation`，修正 `ShareAccess`，根目录回退用 `String.Empty` |
| SMB 设置页 DI 解析到 WebDavService | 改用 `GetServices` + `is SmbService` 精确解析 |

### 📦 构建优化

- 清理 `_sleepStateHandler` 残留引用，消除编译警告

---

## [1.0.9] - 2026-05-21

### ⏱️ 睡眠定时（全新功能）

播放页面新增睡眠定时功能，支持预设和自定义时间倒计时。

- **预设时间选择**：10 / 20 / 30 / 45 / 60 / 90 分钟一键设定
- **自定义时间**：手动输入任意分钟数
- **播完再停**：可选「播完整首歌再停止播放」，当前歌曲播放完毕后再暂停
- **倒计时显示**：定时启动后按钮变色，右侧显示实时倒计时
- **定时中点击取消**：再次点击定时按钮可随时取消倒计时

### 🎵 频谱图全面优化

参考 QQ 音乐 / 网易云 / 酷狗等主流播放器，对频谱图进行大幅优化：

#### 核心算法
- **频段分布重构**：从 32 频段扩展到 64 频段，范围 0~18kHz，低频区线性细分 + 中高频对数分布
- **汉宁窗平滑**：三点加权卷积平滑（0.25 / 0.5 / 0.25），消除频谱泄漏导致的低频虚高
- **RMS 能量计算**：改用均方根替代峰值计算，能量响应更平滑自然
- **增益下调**：降低整体增益系数，避免音乐未开始柱条提前跳动
- **动态范围扩展**：低音区独立分配更多柱条，高音区合理压缩

#### 视觉效果
- **峰值指示器下落减速**：降低峰值小横线的下落速度，视觉效果更流畅
- **低能量态透明底色**：无音乐时柱条半透明，有能量时渐变为主题色

#### UI 改进
- **频谱图开关**：控制区新增频谱图开关按钮，可随时开启/关闭频谱显示
- **控制区控件高亮**：所有控制按钮改为高亮度白色，提升可见性

### 📋 播放列表弹窗样式优化

- 弹窗背景从紫色改为毛玻璃半透明白色，最终定为 **黑色 20% 透明度**
- 统一弹窗风格：MaterialCardView + 圆角 24dp + 半透明背景 + 白色描边

### 🗄️ 播放计数与数据库优化

- **单曲播放计数**：数据库新增 `PlayCount` 字段，每播放一次自动 +1
- **移除最近播放数量限制**：不再限制为 20 条，保留全部播放历史
- **增量刷新机制**：刷新时不再清空数据库，只处理新增/删除的歌曲，保留计数和收藏数据
- 搜索页面改名「**探索**」页面

### ✨ UI/UX 改进

- **字体规范化**：全局移除「站酷快乐体2016修订版」，统一使用 `sans-serif` / `sans-serif-medium` 规范字体
- **CheckBox 自定义样式**：创建独立 drawable 资源，修复黑色背景上的白色方块问题
- **自定义时间弹窗优化**：取消返回上一级时间选择界面，确认后才关闭全部弹窗

### 🔧 Bug 修复

| 问题 | 修复方案 |
|------|---------|
| 睡眠定时倒计时结束不停止播放 | 监听 `IAudioPlayerService.StateChanged` 事件，播完当前歌曲后暂停 |
| 弹窗中 CheckBox 显示白色方块 | 创建自定义 cb_check_box drawable（透明边框/白色填充+黑色对勾） |
| 自定义定时弹窗关闭全部弹窗 | 改为返回上一级时间选择界面，确认后才关闭所有弹窗 |

### 🎨 播放模式按钮颜色

- 播放模式（列表循环/单曲循环/随机播放）按钮颜色随封面 Material You 调色板动态变化

---

## [1.0.8] - 2025-05-21

### 🎵 音频频谱可视化（全新功能）

播放界面新增实时音频频谱可视化，32 频段条形图跟随音乐节拍跳动。

#### 核心实现
- 采用 Android 原生 `Visualizer` API，通过 `AudioSessionId` 绑定 ExoPlayer 音频输出
- FFT 频谱数据实时捕获，32 频段映射到人耳可听范围（20Hz~20kHz）
- 混合频段分布：低频线性 + 中高频对数，确保每根柱条拥有独立数据源
- 首次使用自动请求 `RECORD_AUDIO` 权限（Android Visualizer API 硬性要求）

#### 视觉效果
- **峰值指示器（Peak Hold）**：每根柱条顶部悬浮小横线，快速上升、重力缓降
- **快攻慢放动画**：柱条上升速度 0.9（近即时），下落速度 0.45（自然回落）
- **颜色跟随主题**：频谱颜色与进度条滑块同步（Material You 模式取 `palette.Primary`，默认模式取白色）
- 低能量态显示半透明底色，高能量态渐变至主题色

### 🎨 播放界面布局优化

| 调整项 | 修改前 | 修改后 |
|--------|--------|--------|
| 歌名/艺术家位置 | 封面下方独立区域 | 移入封面底部雾化渐变区域，白色文字+阴影 |
| 雾化渐变高度 | 60dp | 90dp（容纳歌名文字） |
| 歌词区域 | 权重 1 | 权重 1.2（吸收原歌名区域空间） |
| 播放控件底部间距 | 30dp | 8dp（贴近屏幕底部） |
| 频谱与控件间距 | -4dp（重叠） | 0dp（自然衔接） |

### 📝 歌词加载优先级调整

歌词获取优先级从「内嵌歌词 > 外置 .lrc」改为「外置同名 .lrc > 内嵌歌词」，优先使用更完整的外置歌词文件。

### 🔧 Bug 修复

| 问题 | 修复方案 |
|------|---------|
| 频谱完全不跳动 | 从 TeeAudioProcessor（反射注入 ExoPlayer 管道）切换到 Android 原生 Visualizer API |
| 频谱只有1根条跳动 | FFT 数据格式错误：Android 返回 `[R0,I0,R1,I1,...]` 复数对，需计算 `sqrt(R²+I²)` |
| 频谱全部为0 | `samplingRate` 单位是 milliHertz 而非 Hz，需除以 1000 |
| 低频段柱条不跳动 | 纯对数分布下低频段映射到同一 FFT bin，改为混合线性+对数分布 |
| 切歌后频谱停跳 | 监听 `PlayPauseIcon` 变化重新绑定 Visualizer 到新 `AudioSessionId` |
| 频谱启动延迟 | 播放状态变化时立即触发 `TryStartVisualizer` |
| `LinearGradient` 崩溃 | Xamarin 绑定把 ARGB int 当成资源 ID，删除未使用的 LinearGradient |
| 浅色背景频谱不可见 | 颜色改为与进度条同色（`palette.Primary` / 白色） |

### 🏗️ 架构变更

- **移除** `TeeAudioProcessor` 及其反射注入逻辑（不可靠的 ExoPlayer 管道注入方案）
- **新增** `VisualizerHelper`：封装 Android Visualizer API 生命周期管理
- **新增** `AudioVisualizerView`：自定义 View，支持峰值保持 + 重力下落 + 快攻慢放动画
- **实现** `AudioSessionId`：通过反射 `getAudioSessionId()` 从 ExoPlayer 获取真实音频会话 ID

---

## [1.0.7] - 2025-05-17

### 🎉 猫爪标签 (CatClawTag) 插件（重大更新）

#### ✨ 匹配元数据功能
- 新增"匹配元数据"搜索弹窗，支持**多源搜索 + 多结果选择**
  - 搜索源下拉框：LRCLIB / 网易云音乐 / QQ音乐 / 酷狗 / 酷我
  - 关键字输入框：标题/艺术家/专辑
  - 字段复选框：独立勾选需要匹配的字段（标题、艺术家、专辑、年份、音轨号、流派）
  - 多结果列表：展示所有搜索结果，用户可选择任意一条写入元数据

#### 🎵 歌词搜索增强
- **6 大歌词数据源**：LRCLIB、网易云音乐、QQ音乐、酷狗、酷我、嵌入歌词
- 搜索结果支持**多版本选择**，可预览并挑选最匹配的版本
- 自动根据歌曲信息（标题+艺术家）智能匹配歌词

#### 🖼️ 封面搜索增强
- **7 大封面数据源**：iTunes API、Deezer API、QQ音乐、酷狗、酷我、网易云音乐、Last.fm
- 封面选择弹窗支持**图片预览**，异步下载显示缩略图
- 搜索结果支持**多版本选择**

#### 💾 保存模式升级
- 歌词和封面新增**保存模式下拉框**：
  - **保存到标签** — 写入音频文件内嵌标签
  - **保存到文件** — 同目录生成 .lrc / .jpg 文件
  - **标签和文件** — 同时写入两者
- 本地文件采用**删除+重命名策略**，解决 Android 文件锁定问题
- content:// URI 支持**三级写回策略**：OpenOutputStream → tree URI → DocumentFile
- WebDAV 远程文件通过 **PUT 方法**写回

### 🔧 Bug 修复

| 问题 | 修复方案 |
|------|---------|
| Shuffle 模式切换导致元数据与播放歌曲不匹配 | 双列表索引映射重构，顺序/随机双向切换保持当前歌曲不变 |
| content:// URI 本地歌曲无法保存元数据 | 三级写回策略 + PersistedUriPermissions |
| 封面选择弹窗不显示图片 | 改为水平布局 + 异步下载 + Handler fallback |
| 提示保存成功但实际未保存 | WriteBackContentUriAsync 返回 bool，SaveSongMetadataAsync 根据返回值判断 |
| debug.log 无插件日志 | 新增 ILogService 接口及 LogService 实现，统一日志输出到 `debug.log` |
| 封面搜索全部失败 | Task.WhenAll 改为每个任务独立 try-catch，单源失败不影响其他源 |
| 音乐库封面图标修改后不更新 | 新增 InvalidateSongCache 方法，保存成功后清除封面缓存 |
| MediaStore 刷新不生效 | 使用 MediaScannerConnection + 自定义广播通知系统重新扫描 |

### 🎨 UI/UX 改进

- 长按音乐库歌曲弹出**毛玻璃圆角卡片式上下文菜单**（半透明背景 + 圆角阴影）
- 移除批量自动匹配/搜索匹配菜单项，统一归入"匹配元数据"
- 匹配元数据入口移至**音乐库长按菜单**（不再在歌单中操作）

### 📁 数据库规范化
- 数据库路径从 `CacheDir` 迁移至 `ExternalFilesDir`（Android/data 目录），符合 Android 规范
- 新增 `RescanLibraryReceiver` 广播接收器，插件保存后可触发音乐库刷新

### 🔊 音频格式全面扩展

扫描入库支持的音频格式从 **6 种扩展至 26 种主流格式**：

| 类别 | 格式 |
|------|------|
| 有损压缩 | .mp3, .m4a, .aac, .ogg, .oga, .opus, .wma, .amr, .spx |
| 无损压缩 | .flac, .ape, .wv, .tta |
| 未压缩/高解析度 | .wav, .aiff, .aifc, .dsf (DSD), .dff (DSDFF) |
| 容器格式 | .mp4, .mkv, .mka, .webm, .3gp |
| MIDI | .mid, .midi, .rmi |

> ExoPlayer 引擎本身无格式限制，上述扩展仅影响文件扫描入库范围。

---

## [1.0.6] - 历史版本

### 基础功能
- SAF 文件夹选择与多文件夹管理
- MediaStore 音频扫描
- Navidrome/Subsonic 网络音乐支持
- WebDAV 远程文件协议
- ExoPlayer 音频播放引擎
- LRC 歌词同步滚动 + 全屏歌词页
- 桌面悬浮歌词（拖拽/锁定/双行KTV）
- 收藏与播放历史
- 通知栏媒体控制 + MediaSession
- 5 色主题 + 深色模式
- 实时搜索 + SQL JOIN
- 插件体系架构预留
