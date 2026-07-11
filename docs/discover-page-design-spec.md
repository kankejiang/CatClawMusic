# CatClawMusic · 发现页 UI 设计规格

> 配套高保真原型：`discover-page-prototype.html`（浏览器直接打开，支持深/浅色切换、Hero 轮播、横向 scroll-snap、TabBar）。
> 设计 Token 直接取自 `CatClawMusic.Maui/Resources/Styles/Colors.xaml`，可 1:1 落地为 MAUI XAML。

## 1. 页面定位
「发现」是 App 的底部一级 Tab（与 播放 / 歌单 / 音乐库 平级），承载**被动推荐流**：用户无需搜索，即可浏览「每日推荐 → 推荐艺人 → 推荐专辑 → 排行榜 → 我的最爱」。心智模型：**发现 → 浏览 → 播放**。

## 2. 设计语言（与现有 App 一致）
- **玻璃态（Glassmorphism）**：卡片 = 半透明 `CardBackgroundStrongColor`(rgba) + `backdrop-filter: blur` + 1px `GlassStroke` 描边 + 顶部 ~13% 白色高光层（`GlassHighlightBrush`）。
- **深色基底 + 可见层次**：页面背景 `PageBackgroundBrush` 四段渐变（`#0B0D20→#15132E→#0A0C1C→#080914`），叠加主色/强调色径向光晕（`PrimaryGlowBrush`/`AccentGlowBrush`）。
- **一致圆角半径体系**：`xs 12 / sm 16 / md 20 / lg 24 / xl 26 / pill 999`，全页统一节奏。
- **主题**：通过 `DynamicResource` 引用，支持 10 套主题色 + 深/浅/跟随系统（已有 `ThemeService`）。

## 3. 页面结构（顶部分段 Tab + 四面板）
发现页顶部用**分段控件**（推荐 / 排行榜 / 歌手 / 推荐专辑）切换主内容区，单屏只显示一类内容，避免长滚拥挤。底栏「发现」Tab 为一级导航，与分段控件层级区分（底栏图标+文字、分段纯文字胶囊）。

| 分段 | 内容 | 数据源（ExploreDataService） | 组件 | 交互 |
|---|---|---|---|---|
| 推荐（默认） | Hero 轮播 | 本地曲库：每日随机 20 首 / 我的最爱 / 最常播放（+ AI 开时插入「智能推荐」共 4 张） | 全宽渐变卡 + 播放 FAB + 指示点 | 自动轮播 4.5s、点击切换、播放对应列表 |
| 推荐 | 每日推荐 | `GetDailyRecommendAsync()` | 横向方形卡 (132²) ×8 | 换一批(重洗)、点击播放 |
| 推荐 | 推荐艺人 | `GetArtistsWithSongCountAsync()` | 横向圆形卡 (84Ø) | 点击 → 艺人详情 |
| 排行榜 | 最多播放 | `GetTopPlayedSongsAsync()` | 纵向列表 + 名次(前三高亮) | 点击/播放按钮 |
| 排行榜 | 我的最爱 ★ | `GetFavoriteSongsAsync()` | 纵向列表(★ 标星、已收藏标记) | 点击/播放按钮 |
| 歌手 | 推荐歌手 | `GetArtistsWithSongCountAsync()` | 3 列网格圆形卡 | 点击 → 艺人详情 |
| 推荐专辑 | 从曲库随机挑出的专辑 | `GetAlbumsWithSongCountAsync()` | 2 列网格方卡（封面+标题+艺人） | 点击 → 专辑详情 |

> **本地播放器语义（重要）**：本页所有内容均来自**用户自己的曲库 + 已连接服务（Subsonic/WebDAV）**，去重合并后随机/统计生成。**不存在**流式平台的编辑运营榜、飙升榜、算法个性化推荐。具体映射：
> - 每日随机歌单 = `GetDailyRecommendAsync()` 随机 20 首 + 当日磁盘缓存（隔天重洗）。
> - 最多播放 = `GetTopPlayedSongsAsync()`，来自本地 `PlayHistory.PlayCount`（真实本地榜）。
> - 我的最爱 ★ = `GetFavoriteSongsAsync()`，来自本地收藏表（用户手动标星的歌）。
> - 推荐歌手 / 推荐专辑 = 从你库里随机挑出，语义是「重新发现自己的库」，而非「新内容」。曲库数月不更新也照样有内容（不依赖入库时间新鲜度）。

## 4. 复用资产
- 样式：`GlassCardStyle`(R24) / `GlassCardStrongStyle`(R26) / `GlassIconContainerStyle`(44² R16) / `GlassBadgeStyle`(pill) / `SectionTitleStyle`(18 bold) / `PageTitleStyle`(30 bold)。
- 模板：`Templates.xaml` 的 `AlbumCardTemplate`（160 高封面）、`ArtistCardTemplate`（84Ø）、`SongItemTemplate`（列表行）。
- 图标：`ic_*` MauiImage + `AppThemeBinding` 深/浅变体；封面走 `CoverHelper`/`CoverSource`，缺省 `ic_music_note`。

## 5. 间距与节奏
- 外边距 `18px`，区块间距 `14px`，卡片间距 `12px`，列表行间距 `10px`。
- 每屏唯一主行动 = Hero 播放；横向区用 `scroll-snap` 分段浏览；纵向列表收敛详情。

## 6. 落地到 MAUI 的映射
- 页面：`DiscoverPage.xaml`(ContentPage) + `DiscoverViewModel`(`CommunityToolkit.Mvvm` `[ObservableProperty]`)。
- 顶部分段：`<Microsoft.Maui.Controls.SegmentedControl>`（或 `Grid`+`RadioButton` 胶囊）绑定 `CurrentCategory`，四个面板用 `IsVisible` 切换。
- 横向区（推荐页）：`<CollectionView ItemsLayout="HorizontalList">` + 复用 `AlbumCardTemplate`/`ArtistCardTemplate`。
- Hero：`<CarouselView>` 绑定精选歌单，IndicatorView 指示点。
- 网格区（歌手/推荐专辑）：`<CollectionView ItemsLayout="VerticalGrid">` 或 `FlexLayout`，复用卡片模板。
- 接入：`MauiProgram` 注册 `AddTransient<DiscoverPage>()`；手机端 `MainPage` ViewPager 第 2 位 Tab、桌面端 `DesktopMainPage` 侧栏 `NavDiscover`。
- 数据：`ExploreDataService` 已就绪，ViewModel 直接注入调用（支持 `SetSourceFilter("all|local|network")`）。

## 7. 参考（IMA 知识库）
- 《什么是玻璃态设计风格？》— 玻璃态技法与最佳实践。
- 《卡片设计成这样能落地吗？》— 卡片精致度「多层次空间感」公式（背景/中间/内容三层）。
- 《在用户界面中构建一致的圆角半径体系》— 圆角尺度规范。

## 8. 智能推荐（AI 情境化，可配置，可选）
发现页「推荐」面板顶部提供「智能推荐 AI」开关。关闭时退回现有**随机每日歌单**；开启后由可配置的 LLM 按情境生成歌单。

- **上下文信号（演示态）**：天气 / 周六 / 深夜 / 万圣节前夜 —— 实际实现中日期·时间·周末·节日本地计算（零联网），天气可选（Open-Meteo 免 key），听歌画像来自 `PlayHistory`/`PlayCount`。
- **Hero 轮播在 AI 模式下由 3 张变 4 张**：AI 关 = 今日随机歌单 / 我的最爱 / 循环最多次（3 张）；AI 开 = 在最前插入「AI 为你推荐 · 雨天·周六深夜」智能卡，共 4 张，**随机推荐卡始终保留**。每张卡对应独立播放列表，指示点数量随卡数变化。
- **「每日推荐」区在 AI 模式下改写**：标题变为「AI 为你推荐」，显示上下文 chips（天气/周六/深夜/万圣节前夜），「换一批」变为「重新生成」。
- **防幻觉**：仅从用户曲库候选列表（最常播+我的最爱 Top N）中挑选/排序，返回 `songIds`，不生成库外歌曲。
- **隐私与降级**：只发聚合信号（曲风/艺人/次数），不发原始历史；可接本地 Ollama 模型；AI 未配置/失败/离线 → 回退随机每日歌单。每日结果写磁盘缓存（复用 `daily_recommend.json` 思路）。
- **落地**：新增 `IRecommendationService`（Random / AI 两实现）；`ExploreDataService.SetRecommendMode("random"|"ai")`；设置页加「智能推荐」开关 + 模型配置（endpoint/key/model）+ 上下文信号开关。

## 9. PC 桌面端布局映射（Windows / DesktopMainPage 外壳）
配套原型：`discover-page-pc-prototype.html`（1200×800 窗口，贴合真实 `DesktopMainPage.xaml` 栅格）。

- **窗口栅格**（直接对应 `DesktopMainPage.xaml`）：`Columns=220,*`；`Rows=Auto(标题栏),52(顶栏),*,96(播放条)`。
- **左侧栏**（220px，`CardBackgroundColor` + 右侧 1px `DividerColor`）：Logo（猫爪 + 猫爪音乐/CatClaw Music）+ 导航（🔍 发现[激活] / 🎵 我的音乐 / 📋 歌单）+「我的歌单」可滚动列表 + 底部 ⚙️ 设置。导航项激活态用 `HeroBrush` 渐变 + `GlassStroke` 描边，对应 XAML 的 `NavDiscover` 高亮。
- **顶栏**：左侧玻璃态搜索框（内嵌放大镜 SVG + 占位「搜索歌曲、歌手、专辑…」+ 右侧 ⌘K 快捷键提示，聚焦时描边发光），右侧仅保留主题切换按钮（🌙/☀️）；与手机端顶栏（搜索 + 主题）保持一致，去掉歌词/通知/头像装饰。对应 `SearchBox` 与 `ic_theme` 控件。
- **内容区**：`PageBackgroundBrush` 渐变 + 玻璃态内容。头部「发现」大标题 + 其下随时间段变化的问候副标题（凌晨好/早上好/下午好/晚上好，与手机端一致，`#greet` 由 `updateGreet()` 每 30s 刷新）+ 右侧「智能推荐 AI」开关 + 深/浅主题键（对应 `ThemeService`）；下方分段控件（推荐/排行榜/歌手/推荐专辑，sticky 吸顶）。
- **分段内容（利用宽屏）**：
  - 推荐：**Hero 横向多卡轮播**（类似网易云精选活动）：宽矮横卡并排，PC 宽屏一次显示 2~3 张，左右箭头切换 + 指示点 + 自动轮播 5s；每张卡含渐变背景 + 标签(带圆点) + 大标题 + 描述 + 右下播放钮。AI 关时 3 张 / AI 开时 4 张「AI 推荐」卡、随机推荐卡始终保留；下方每日推荐横向滚行 + 推荐艺人横向滚行。
  - 排行榜：**双列网格**（左「最多播放」list / 右「我的最爱 ★」list），对应手机端的上下两块。
  - 歌手：`VerticalGrid` 8 列圆形卡（手机端为 3 列）。
  - 推荐专辑：`VerticalGrid` 6 列方卡（手机端为 2 列）。
- **底部播放条**（仅内容列宽，96px，`HeroBrush` 渐变背景 + 顶部 1px 分隔线）：左=封面+标题/艺人（240px），中=播控（循环/上一首/播放暂停/下一首/收藏）+ 进度条，右=音量条。对应 `PlayerBarBorder` 的三列 `Auto,*,Auto`。
- **交互一致**：AI 开关、Hero 3/4 张、主题切换、Tab 切换逻辑与手机端原型完全相同，仅布局密度按桌面放宽。

