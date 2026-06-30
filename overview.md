# CatClawMusic MAUI Android — Phase 1: 设置体系占位页转正

**日期**: 2026-07-01
**目标端**: `CatClawMusic.Maui` 安卓客户端 (v1.5.3)
**依据文档**: `docs/maui-android-follow-up-development-plan.md` Phase 1 + 第 13 节"下一步建议"第 1 项

---

## 完成内容

按照开发方案文档 **Phase 1: 设置体系真实化**，将设置主页中 4 个占位页转为真实功能页，消除"点进去却是占位页"的落差。

### P1-A: AI 设置页 ✅

**文件**:
- `CatClawMusic.Maui/ViewModels/AiSettingsViewModel.cs` (新增)
- `CatClawMusic.Maui/Pages/AiSettingsPage.xaml` (重写)
- `CatClawMusic.Maui/Pages/AiSettingsPage.xaml.cs` (重写)

**实现能力**:
- 启用 AI 助手开关
- LLM 提供商选择（DeepSeek / 魔搭 / 智谱 / Moonshot / 通义千问 / 讯飞星火 / llama.cpp / 自定义）
- Base URL / API Key / 模型名配置（含预设模型胶囊点击填入）
- 温度 (0-2) 与最大 Tokens 调节
- 测试连通性按钮（实时反馈成功/失败，带颜色标识）
- 保存配置 / 重置默认值
- 当前助手状态展示（Yuki）
- 配置持久化（基于 `AgentConfigStorage` + Preferences）

### P1-B: 权限管理页 ✅

**文件**:
- `CatClawMusic.Maui/ViewModels/PermissionManagementViewModel.cs` (新增)
- `CatClawMusic.Maui/Pages/PermissionManagementPage.xaml` (重写)
- `CatClawMusic.Maui/Pages/PermissionManagementPage.xaml.cs` (重写)

**实现能力**:
- 4 类权限状态展示：存储/媒体读取、管理所有文件、悬浮窗、通知权限 (Android 13+)
- 每项权限独立的"去授权"按钮
- 状态色标（绿=已授予 / 橙=未授权）
- 返回后自动刷新状态
- 打开应用系统设置页入口

### P1-C: 远程音乐服务页 ✅

**文件**:
- `CatClawMusic.Maui/ViewModels/RemoteMusicSettingsViewModel.cs` (新增)
- `CatClawMusic.Maui/Pages/RemoteMusicSettingsPage.xaml` (重写)
- `CatClawMusic.Maui/Pages/RemoteMusicSettingsPage.xaml.cs` (重写)
- `CatClawMusic.Maui/Converters/ProtocolToTextConverter.cs` (新增)
- `CatClawMusic.Data/MusicDatabase.cs` (新增 `DeleteConnectionProfileAsync`)

**实现能力**:
- 已配置连接列表（名称 / 协议徽章 / 主机）
- 缓存歌曲数量统计 + 清空缓存
- 新建 / 编辑 / 删除连接（含确认对话框）
- 连接测试（Navidrome 走 `ISubsonicService.PingAsync`，WebDAV/SMB 走 `INetworkFileService.TestConnectionAsync`）
- 协议选择（WebDAV / Navidrome / SMB），SMB 时动态显隐共享名/域名字段
- 空状态卡片

### P1-D: 插件管理页 ✅

**文件**:
- `CatClawMusic.Maui/ViewModels/PluginManagementViewModel.cs` (新增)
- `CatClawMusic.Maui/Pages/PluginManagementPage.xaml` (重写)
- `CatClawMusic.Maui/Pages/PluginManagementPage.xaml.cs` (重写)

**实现能力**:
- 插件列表（图标 / 名称 / 分类徽章 / 来源徽章 / 版本 / 作者 / 描述 / 启用状态）
- 点击卡片切换启用/禁用（持久化到 Preferences）
- 汇总统计（共 X 个插件，已启用 Y 个）
- 空状态卡片
- 刷新入口

### 公共改动

- `CatClawMusic.Maui/MauiProgram.cs`: 注册 4 个新 ViewModel 到 DI 容器
- 沿用现有毛玻璃/圆角卡片设计风格，与 LocalMusicSettingsPage 等已实现页面保持一致

---

## 构建验证

- **Debug 构建** (`dotnet build -c Debug -f net10.0-android`): ✅ **0 错误**, 137 警告（均为预存 nullable/过时 API 警告）
- **Release publish** (签名 APK): 进行中（AOT 编译）

---

## 关键决策

1. **MVVM 一致性**: 4 个页面均采用 `ObservableObject` + `[ObservableProperty]` + `[RelayCommand]` 模式，与现有 `LocalMusicSettingsViewModel` 等保持一致
2. **DI 注入**: 通过构造函数注入服务，在 `MauiProgram.cs` 注册为 Transient
3. **导航路由**: 复用 `AppShell.xaml.cs` 已注册的 `settings/xxx` 路由，无需新增路由
4. **风格统一**: 所有页面沿用 Hero Header + GlassCard + PrimaryGlowBrush 视觉语言
5. **ConnectionProfile 命名冲突**: 用 `using ConnectionProfile = CatClawMusic.Core.Models.ConnectionProfile;` alias 解决与 `Microsoft.Maui.Networking.ConnectionProfile` 的冲突
6. **Switch 属性**: MAUI 中 `Switch` 用 `IsToggled`（非 WPF/UWP 的 `IsChecked`）
7. **权限检查不弹框**: `CheckNotificationPermissionAsync` 只用 `CheckStatusAsync`，请求逻辑独立在 `RequestNotificationPermissionAsync`

---

## 后续待办（文档 Phase 2-5）

本次完成 Phase 1。文档第 13 节建议的另外 2 件"立即执行"事项尚未做：

- [ ] **Phase 2**: 发现页 AI 与搜索逻辑拆分（文档 7.x 节）
- [ ] **Phase 3**: 底部迷你播放器（文档 8.2 P3-A 节）

以及 Phase 4（详情页统一）、Phase 5（设置生效链路）按文档排期推进。

---

**构建命令参考**:
```
dotnet build CatClawMusic.Maui/CatClawMusic.Maui.csproj -c Debug -f net10.0-android -p:AndroidSdkDirectory="C:/Users/lvjin/AppData/Local/Android/Sdk" -p:JavaSdkDirectory="C:/Program Files/Android/openjdk/jdk-21.0.8"
```
