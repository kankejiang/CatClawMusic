# CatClawMusic MAUI Android 后续开发方案

**项目**: CatClawMusic  
**目标端**: `CatClawMusic.Maui` 安卓客户端  
**文档类型**: 后续开发规划 / 分阶段实施方案  
**更新日期**: 2026-07-01  

---

## 1. 文档目的

本文档基于当前仓库中的现有实现，整理出 `CatClawMusic.Maui` 安卓客户端下一阶段的开发方向、模块优先级、任务拆解、验收标准与风险控制方案，用于后续持续迭代。

本方案重点解决以下问题：

- 当前 MAUI 安卓端已经具备基本页面和部分功能，但整体完成度仍不足。
- 页面职责存在混杂，尤其是“发现/探索”页和“设置”体系。
- 已开放到 UI 的部分菜单仍为占位页，影响用户认知和可用性。
- 导航、详情页联动、播放闭环、设置生效链路仍需系统化整理。
- 安卓目标已经可以作为主交付方向，应优先围绕安卓端做完整可用版本。

---

## 2. 当前代码现状

### 2.1 当前技术结构

仓库目前大体分为三层：

- `CatClawMusic.Core`
  - 核心接口、数据模型、播放队列、AI 服务抽象、插件抽象等。
- `CatClawMusic.Data`
  - 数据库、扫描、网络音乐、推荐数据、备份恢复、多种协议服务等。
- `CatClawMusic.Maui`
  - MAUI 页面、ViewModel、平台实现、主题服务、权限服务、音频服务等。

其中 MAUI 客户端已经具备以下基础：

- `Shell + TabBar` 主导航骨架
- `音乐库 / 发现 / 播放 / 设置` 四个主页面
- 播放服务与播放队列的基本接入
- 本地音乐扫描与数据库读取能力
- 推荐数据服务 `ExploreDataService`
- 主题服务 `ThemeService`
- AI 抽象服务 `IAgentService`
- 若干详情页、设置子页和平台服务

### 2.2 已具备基础能力的模块

以下模块属于“已经有基础实现，可以继续深化”的状态：

- 主导航
  - `CatClawMusic.Maui/AppShell.xaml`
  - `CatClawMusic.Maui/AppShell.xaml.cs`
- 发现页
  - `CatClawMusic.Maui/Pages/SearchPage.xaml`
  - `CatClawMusic.Maui/Pages/SearchPage.xaml.cs`
  - `CatClawMusic.Maui/ViewModels/SearchViewModel.cs`
- 音乐库
  - `CatClawMusic.Maui/Pages/LibraryPage.xaml`
  - `CatClawMusic.Maui/Pages/LibraryPage.xaml.cs`
  - `CatClawMusic.Maui/ViewModels/LibraryViewModel.cs`
- 播放页
  - `CatClawMusic.Maui/Pages/NowPlayingPage.xaml`
  - `CatClawMusic.Maui/ViewModels/AppViewModels.cs`
- 设置主页
  - `CatClawMusic.Maui/Pages/SettingsPage.xaml`
  - `CatClawMusic.Maui/Pages/SettingsPage.xaml.cs`
  - `CatClawMusic.Maui/ViewModels/SettingsViewModel.cs`
- 本地音乐设置
  - `CatClawMusic.Maui/Pages/LocalMusicSettingsPage.xaml`
  - `CatClawMusic.Maui/ViewModels/LocalMusicSettingsViewModel.cs`
- 外观设置
  - `CatClawMusic.Maui/Pages/AppearanceSettingsPage.xaml`
  - `CatClawMusic.Maui/ViewModels/AppearanceSettingsViewModel.cs`
- 通用设置
  - `CatClawMusic.Maui/Pages/GeneralSettingsPage.xaml`
  - `CatClawMusic.Maui/ViewModels/GeneralSettingsViewModel.cs`
- 详情页链路
  - `ArtistDetailPage`
  - `AlbumDetailPage`
  - `PlaylistPage / PlaylistDetailPage`

### 2.3 当前核心问题

#### 2.3.1 信息架构问题

- 当前“发现页”同时承担了首页、搜索、推荐、AI 助手入口等多个职责。
- 用户很难明确区分“浏览推荐”和“主动搜索”的边界。
- AI 助手与发现内容流耦合较强，影响页面聚焦。

#### 2.3.2 功能落地不一致

- 设置主页已经展示多个二级功能入口。
- 但多个子页仍然只是占位 UI，尚未实现真实业务能力。
- 造成“看起来有功能，但点进去不可用”的落差。

#### 2.3.3 导航与详情链路不完整

- 目前已修正设置子页路由与返回链路，但全局仍需统一导航规范。
- 详情页之间的参数传递和数据加载策略仍有待收敛。
- 未来如果继续增加页面，裸路由和层级路由混用会继续放大维护成本。

#### 2.3.4 UI 体系还未完全组件化

- 当前已形成一套毛玻璃/圆角卡片风格，但仍以页面内重复 XAML 为主。
- 缺少统一的公共组件层，例如：
  - 页面头部 Hero 组件
  - 毛玻璃列表项组件
  - 胶囊按钮组件
  - 设置菜单项组件
  - 迷你播放器组件

#### 2.3.5 平台交付策略尚未明确收口

- 安卓目标已可作为主交付端。
- Windows 目标仍有额外编译问题，不适合当前与安卓同步推进。
- 需要明确阶段性策略：先把安卓端做完整，再逐步回头治理多目标兼容。

---

## 3. 当前未完整绑定到 UI 的功能清单

以下为当前代码中“入口已存在但功能未真正落地”或“仅部分落地”的模块。

### 3.1 设置体系中的占位页

以下页面目前仍为占位态或开发中页面：

- `CatClawMusic.Maui/Pages/RemoteMusicSettingsPage.xaml`
- `CatClawMusic.Maui/Pages/AiSettingsPage.xaml`
- `CatClawMusic.Maui/Pages/PermissionManagementPage.xaml`
- `CatClawMusic.Maui/Pages/PluginManagementPage.xaml`
- `CatClawMusic.Maui/Pages/FolderBrowserPage.xaml`
- `CatClawMusic.Maui/Pages/DesktopLyricPage.xaml`
- `CatClawMusic.Maui/Pages/SongDetailPage.xaml`
- `CatClawMusic.Maui/Pages/P2PSettingsPage.xaml`
- `CatClawMusic.Maui/Pages/ServerSettingsPage.xaml`
- `CatClawMusic.Maui/Pages/SplashSettingsPage.xaml`
- `CatClawMusic.Maui/Pages/ModelManagerPage.xaml`
- `CatClawMusic.Maui/Pages/ModelEditPage.xaml`
- `CatClawMusic.Maui/Pages/ArtistMatchPage.xaml`
- `CatClawMusic.Maui/Pages/ArtistMatchDetailPage.xaml`

### 3.2 设置项已存在但尚未真正生效

#### 外观设置

`AppearanceSettingsViewModel` 中下列能力仍未完整接入应用行为：

- 启动页下拉选择
  - 当前仅有 UI 状态与选择值
  - 未接入启动页路由持久化与 App 启动逻辑
- 主题色切换
  - 已可部分切换主题
  - 但选项设计和现有主题映射需要统一

#### 通用设置

`GeneralSettingsViewModel` 中下列能力仍需补齐：

- 语言切换
  - 当前只有选择项和索引
  - 未接入本地化资源系统
  - 未接入设置持久化后的重新加载流程
- 恢复默认设置
  - 当前调用 `Preferences.Clear()`
  - 但没有对各模块做完整重建、重载、重绑定

### 3.3 发现页中的边界混乱点

发现页当前已比之前更清晰，但仍有进一步拆分空间：

- 搜索框承担“筛选发现内容”和“进入 AI 助手”的混合暗示
- AI 助手仍存在于发现页上下文中，而非完全独立模块
- 推荐歌曲、常听歌曲、新入库本质上属于首页内容流
- 主动搜索建议未来拆为单独搜索页面或独立搜索结果层

### 3.4 详情页相关缺口

- `AlbumDetailPage` 已补通标题参数链路，但未来仍应补齐基于专辑实体的更稳定导航方式
- `SongDetailPage` 仍为占位页
- `ArtistDetailPage -> AlbumDetailPage` 已可跳转，但详情页之间的复用数据结构仍需统一

---

## 4. 产品目标与版本方向

### 4.1 目标版本定位

建议将下一阶段 MAUI 安卓端目标定义为：

**“可持续使用的安卓主线版本”**

即满足以下标准：

- 主导航清晰
- 核心听歌流程闭环
- 设置项与显示状态尽量真实
- 至少主要菜单不再出现“点进去却是占位页”
- UI 风格统一，具备主流播放器的基本现代感
- 导航、详情页、播放状态联动稳定

### 4.2 不建议的目标

当前阶段不建议同时追求：

- 安卓和 Windows 双端同步完整交付
- 一次性完成所有设置子页
- 一次性做完所有高级功能，如桌面歌词、P2P、模型管理、服务端配置等

应先优先保障安卓主体验和主路径闭环。

---

## 5. 目标信息架构建议

### 5.1 建议的底部导航结构

基于当前代码现状，建议保留以下 4 个主导航：

1. `发现`
2. `音乐库`
3. `播放`
4. `设置`

原因：

- 已与当前 `Shell` 结构相符
- 用户认知成本低
- 便于后续继续扩展迷你播放器与详情流

### 5.2 各主页面职责定义

#### 发现

职责：

- 今日推荐
- 常听歌曲
- 最近入库
- 推荐艺人
- 推荐专辑
- AI 助手入口

不建议长期承担：

- 复杂搜索结果页
- 所有 AI 会话的主承载容器
- 过多筛选器与二级导航

#### 音乐库

职责：

- 本地音乐 / 远程音乐浏览
- 排序、筛选、搜索
- 曲目操作、加入歌单、收藏、详情

#### 播放

职责：

- 当前播放状态
- 歌词
- 播放控制
- 音量 / 模式 / 收藏
- 后续可增加队列与更多播放工具

#### 设置

职责：

- 应用配置中心
- 权限和服务接入
- UI 主题 / 缓存 / 备份等系统级管理

### 5.3 建议的二级页面分组

#### 设置中建议按组展示

**内容与服务**

- 外观与个性化
- 本地音乐
- 远程音乐服务
- AI 设置
- 插件管理

**系统与维护**

- 权限管理
- 通用设置
- 备份与恢复
- 关于

**暂缓公开入口**

以下页面在未实现前建议不要直接从主菜单暴露：

- FolderBrowser
- DesktopLyric
- SongDetail
- P2PSettings
- ServerSettings
- SplashSettings
- ModelManager
- ModelEdit
- ArtistMatch

---

## 6. 分阶段开发方案

## Phase 1: 设置体系真实化

### 6.1 目标

先把“已暴露给用户的设置入口”做成真实功能页，提升系统可信度和完成度。

### 6.2 优先交付页面

#### P1-A: AI 设置

建议实现：

- Agent 配置表单
  - Base URL
  - API Key
  - Model Name
  - 超时/是否启用
- 当前配置状态展示
- 测试连通性按钮
- 默认 Agent 切换

依赖代码：

- `CatClawMusic.Core/Interfaces/IAgentService.cs`
- `CatClawMusic.Maui/Services/AgentConfigStorage.cs`
- `CatClawMusic.Maui/ViewModels/SearchViewModel.cs`

验收标准：

- 用户可在 UI 中完成配置
- 保存后发现页 AI 助手状态同步变化
- 测试连接可反馈成功/失败

#### P1-B: 权限管理

建议实现：

- 当前权限状态列表
  - 通知权限
  - 存储权限
  - 管理所有文件权限
  - 悬浮窗权限
- “去授权”按钮
- 权限解释说明

依赖代码：

- `CatClawMusic.Core/Interfaces/IPermissionService.cs`
- `CatClawMusic.Maui/Services/PermissionService.cs`
- `CatClawMusic.Maui/ViewModels/SettingsViewModel.cs`

验收标准：

- 设置页权限状态与权限管理子页状态一致
- 能主动触发授权动作
- 返回后状态可刷新

#### P1-C: 远程音乐服务

建议最小版实现：

- 当前支持的服务类型列表
- 已缓存歌曲数量
- 连接配置入口
- 清理缓存按钮
- 同步/刷新入口

依赖代码：

- `CatClawMusic.Data/NetworkMusicService.cs`
- `CatClawMusic.Data/SubsonicService.cs`
- `CatClawMusic.Data/WebDavService.cs`
- `CatClawMusic.Data/SmbService.cs`

验收标准：

- 至少能展示真实状态而非“敬请期待”
- 至少有一种远程源接入流程可以走通

#### P1-D: 插件管理

建议最小版实现：

- 插件列表
- 启用/禁用开关
- 插件状态标签
- 插件基础信息展示

依赖代码：

- `CatClawMusic.Core/Interfaces/IPluginManager.cs`
- `CatClawMusic.Core/Models/PluginInfo.cs`

验收标准：

- 插件状态可和设置页统计信息联动
- 启用/禁用操作真实生效

### 6.3 Phase 1 交付结果

交付完成后，设置主页中的主入口不再出现明显占位页。

---

## Phase 2: 发现页与搜索体系重构

### 7.1 目标

把当前发现页从“功能聚合页”重构为真正的首页，同时把搜索与 AI 助手边界拆清楚。

### 7.2 推荐的页面职责拆分

#### 方案 A: 保守演进

- `发现页`
  - 首页推荐流
  - AI 助手入口
  - 轻量搜索框
- `单独搜索页`
  - 歌曲搜索
  - 艺人搜索
  - 专辑搜索

#### 方案 B: 中期方案

- `发现页`
  - 仅保留推荐和快捷入口
- `搜索页`
  - 独立成为主搜索体验
- `AI 助手页`
  - 独立页面或底部抽屉

建议优先采用 **方案 A**，改动成本更低。

### 7.3 发现页后续要补的能力

- 更多入口卡片
  - 随机播放
  - 查看全部最近播放
  - 查看全部新入库
- 艺人/专辑区支持“查看更多”
- 搜索时切换为“搜索结果模式”
- AI 助手入口样式独立化

### 7.4 搜索页建议实现内容

如果后续单独恢复 `搜索` 主页或二级页，建议支持：

- 输入联想
- 结果分组
  - 歌曲
  - 艺人
  - 专辑
  - 歌单
- 搜索历史
- 空状态

### 7.5 Phase 2 交付结果

交付完成后：

- 发现页只承担首页推荐职责
- 搜索路径更清晰
- AI 不再打断首页内容阅读

---

## Phase 3: 播放体验完善

### 8.1 目标

对标主流音乐播放器，完善全局播放存在感与控制体验。

### 8.2 核心任务

#### P3-A: 增加迷你播放器

建议位置：

- 底部导航栏上方

建议展示：

- 封面
- 歌曲名
- 艺术家
- 播放/暂停
- 点击进入播放页

依赖：

- `NowPlayingViewModel`
- `PlayQueue`
- `IAudioPlayerService`

#### P3-B: 播放队列面板

建议能力：

- 当前队列展示
- 当前播放项高亮
- 队列重排
- 删除项
- 清空队列

#### P3-C: 播放工具扩展

建议优先顺序：

1. 全屏歌词入口联动
2. 睡眠定时入口
3. 喜欢/收藏状态统一
4. 播放模式可视化
5. 音效/均衡器入口

### 8.3 Phase 3 交付结果

交付完成后，应用将具备更接近主流音乐播放器的持续播放体验。

---

## Phase 4: 详情页与内容体系统一

### 9.1 目标

统一内容页设计和交互模式，让艺人、专辑、歌曲、歌单都具备一致体验。

### 9.2 重点任务

#### P4-A: 统一头图与操作区

统一元素：

- 头图封面
- 标题/副标题
- 主操作按钮
- 次操作按钮
- 返回按钮样式

#### P4-B: 补齐 SongDetailPage

建议展示：

- 歌曲元数据
- 封面
- 艺人 / 专辑跳转
- 比特率 / 大小 / 路径 / 来源
- 收藏状态

#### P4-C: 歌单体系增强

建议补充：

- 新建歌单
- 编辑歌单
- 删除歌单
- 封面设置
- 歌单内排序

### 9.3 Phase 4 交付结果

交付完成后，内容层级会从“能跳转”提升到“结构完整、体验一致”。

---

## Phase 5: 设置生效链路与技术债治理

### 10.1 目标

把当前已有设置项真正接入应用行为，同时清理已经暴露出的工程问题。

### 10.2 重点任务

#### P5-A: 启动页设置生效

当前问题：

- `AppearanceSettingsViewModel` 有启动页选项
- 但未接入 `App` 启动后的默认导航逻辑

建议实现：

- 设置项持久化
- App 启动时根据设置选择默认 Shell 路由
- 异常回退到默认页

#### P5-B: 语言设置生效

当前问题：

- `GeneralSettingsViewModel` 只有选择项
- 未接入资源系统

建议实现：

- 建立基础本地化资源
- 切换语言后提示重启或自动刷新
- 保留系统默认语言模式

#### P5-C: 统一 Dialog 与导航服务

当前问题：

- 多页面仍直接使用 `DisplayAlert`、`DisplayActionSheet`
- 存在过时 API 警告

建议实现：

- 收口到统一 `DialogService`
- 逐步切换到异步版本 API
- 收口跳转到统一 `NavigationService`

#### P5-D: 平台构建治理

当前问题：

- 安卓目标可构建
- Windows 目标仍存在 `FFmpegService` 相关构建问题

建议策略：

- 短期：安卓端持续交付，不让 Windows 问题阻塞主线
- 中期：将 Windows 问题单独建债务任务处理

---

## 7. UI/设计系统建设建议

### 11.1 当前建议

继续沿用现在已经初步建立的风格方向：

- 深色渐变背景
- 半透明毛玻璃
- 圆角卡片
- 柔光氛围
- 高亮主色胶囊按钮

### 11.2 下一步应组件化的 UI 元素

建议抽为公共 XAML 资源或复用模板：

- Page Hero Header
- Glass List Item
- Glass Setting Item
- Glass Pill Button
- Glass Section Header
- Mini Player
- Empty State Card

### 11.3 设计规范建议

建议统一以下规范：

- 页面边距
  - 水平 `16`
  - 大卡片间距 `12-14`
- 圆角半径
  - 小元素 `16`
  - 卡片 `22-26`
- 标题层级
  - Page Title
  - Section Title
  - Primary Body
  - Secondary Body
  - Hint Text

---

## 8. 工程实施顺序建议

建议按以下顺序推进：

1. 设置体系真实化
2. 发现页职责拆分
3. 迷你播放器与播放增强
4. 详情页与歌单体系完善
5. 设置生效链路与技术债清理

原因：

- 第一阶段最直接影响用户信任感
- 第二阶段最直接影响首页可用性
- 第三阶段最能提升“播放器完成度”
- 后两阶段更适合作为产品打磨与工程收尾

---

## 9. 任务拆解建议

## Sprint A: 设置体系转正

- 实现 AI 设置页
- 实现权限管理页
- 实现远程音乐服务页最小版
- 实现插件管理页最小版
- 统一设置页二级页面返回体验

## Sprint B: 发现页与搜索

- 抽离 AI 助手承载方式
- 完善发现页推荐流模块
- 建立独立搜索结果结构
- 增加查看更多与搜索状态

## Sprint C: 播放器增强

- 增加迷你播放器
- 增加播放队列面板
- 完善收藏/模式/睡眠/歌词入口

## Sprint D: 内容页统一

- 完成 SongDetailPage
- 统一专辑/艺人/歌单详情页结构
- 歌单能力增强

## Sprint E: 生效链路与治理

- 启动页设置生效
- 语言设置生效
- Dialog/Navigation 收口
- Windows 构建债务整理

---

## 10. 验收标准

### 10.1 阶段验收通用标准

每个阶段至少满足：

- 安卓目标构建通过
- 主路径手工测试通过
- 页面无明显空白页、死链、回退异常
- 页面状态和设置状态真实联动

### 10.2 核心场景验收

#### 听歌主路径

- 导入音乐
- 进入发现页看到推荐
- 点击歌曲开始播放
- 打开播放页正常显示
- 播放记录写入
- 常听/最近内容更新

#### 设置主路径

- 进入设置
- 打开任一二级页
- 返回设置首页
- 系统返回不直接退桌面

#### 详情主路径

- 发现页进入艺人页
- 艺人页进入专辑页
- 专辑页播放歌曲
- 返回链路正常

---

## 11. 风险与应对

### 11.1 风险：功能页数量过多

问题：

- 设置子页较多，若全部并行推进容易分散注意力。

应对：

- 先只做用户已直接看到的主菜单页
- 隐藏或暂缓未实现子项

### 11.2 风险：发现页再次膨胀

问题：

- 如果继续在发现页塞入搜索、AI、推荐、歌单、分类，会再次失焦。

应对：

- 明确发现页只做“首页推荐”
- 搜索与 AI 独立承载

### 11.3 风险：导航复杂度增加

问题：

- 子页面增加后，若路由规范不统一，未来继续出现返回和参数问题。

应对：

- 统一采用 `tab/child` 风格路由
- 裸路由仅做兼容
- 后续新页面优先走层级路由

### 11.4 风险：多目标构建拖慢主线

问题：

- Windows 目标问题会干扰安卓迭代节奏。

应对：

- 以安卓构建与安卓验收为主
- Windows 问题单列技术债

---

## 12. 预计排期建议

### 方案一：紧凑排期（5 周）

**第 1 周**

- AI 设置
- 权限管理

**第 2 周**

- 远程音乐服务最小版
- 插件管理最小版

**第 3 周**

- 发现页与搜索拆分
- AI 助手承载重构

**第 4 周**

- 迷你播放器
- 播放队列
- 播放页增强

**第 5 周**

- SongDetail
- 设置生效链路
- 一轮技术债修复

### 方案二：稳妥排期（8 周）

更适合边开发边调试边视觉打磨的方式：

- 第 1-2 周：设置体系
- 第 3-4 周：发现页与搜索
- 第 5-6 周：播放器增强
- 第 7 周：详情页与歌单
- 第 8 周：治理与回归

---

## 13. 下一步建议

如果按当前仓库状况继续推进，建议立即执行以下 3 个动作：

1. 把设置页中的 4 个主占位页转正
2. 将发现页 AI 与搜索逻辑进一步拆分
3. 增加底部迷你播放器，建立全局播放存在感

这 3 件事完成后，MAUI 安卓端会从“已有基础页面”明显提升到“可持续交付的播放器版本”。

---

## 14. 附录：建议优先跟踪的关键文件

### 导航与壳层

- `CatClawMusic.Maui/AppShell.xaml`
- `CatClawMusic.Maui/AppShell.xaml.cs`

### 发现页

- `CatClawMusic.Maui/Pages/SearchPage.xaml`
- `CatClawMusic.Maui/Pages/SearchPage.xaml.cs`
- `CatClawMusic.Maui/ViewModels/SearchViewModel.cs`

### 音乐库

- `CatClawMusic.Maui/Pages/LibraryPage.xaml`
- `CatClawMusic.Maui/Pages/LibraryPage.xaml.cs`
- `CatClawMusic.Maui/ViewModels/LibraryViewModel.cs`

### 播放与队列

- `CatClawMusic.Maui/Pages/NowPlayingPage.xaml`
- `CatClawMusic.Maui/ViewModels/AppViewModels.cs`
- `CatClawMusic.Core/Services/PlayQueue.cs`

### 设置体系

- `CatClawMusic.Maui/Pages/SettingsPage.xaml`
- `CatClawMusic.Maui/Pages/SettingsPage.xaml.cs`
- `CatClawMusic.Maui/ViewModels/SettingsViewModel.cs`
- `CatClawMusic.Maui/ViewModels/AppearanceSettingsViewModel.cs`
- `CatClawMusic.Maui/ViewModels/GeneralSettingsViewModel.cs`
- `CatClawMusic.Maui/ViewModels/LocalMusicSettingsViewModel.cs`

### 详情页

- `CatClawMusic.Maui/Pages/ArtistDetailPage.xaml.cs`
- `CatClawMusic.Maui/Pages/AlbumDetailPage.xaml.cs`
- `CatClawMusic.Maui/ViewModels/ArtistDetailViewModel.cs`
- `CatClawMusic.Maui/ViewModels/AlbumDetailViewModel.cs`

### 数据与服务

- `CatClawMusic.Data/ExploreDataService.cs`
- `CatClawMusic.Data/MusicDatabase.cs`
- `CatClawMusic.Data/MusicLibraryService.cs`
- `CatClawMusic.Maui/Services/ThemeService.cs`
- `CatClawMusic.Maui/Services/PermissionService.cs`

---

## 15. 结论

当前 MAUI 安卓端已经不是“从零开始”的阶段，而是“基础能力已具备、需要系统收口和持续完善”的阶段。

后续开发不应继续以“补单个页面”为主，而应转向以下思路：

- 先清理已经开放但未落地的入口
- 再理顺页面职责与导航结构
- 再补足主流音乐播放器应有的全局播放体验
- 最后统一设置生效链路与技术债治理

按照本文方案推进，可以在不推翻现有代码的前提下，把 `CatClawMusic.Maui` 安卓客户端逐步推进到可持续交付状态。
