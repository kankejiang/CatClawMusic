# CatClawMusic 插件开发模板

猫爪音乐（CatClawMusic）**插件开发模板**：包含一个可编译、可安装的最小插件工程与完整开发指南。
宿主客户端是"空壳"，功能通过 `.ccp` 插件包扩展（在线音源、歌词、封面、协议、主题、菜单等）。

## 目录结构

```
CatClawMusic.Plugins/
├── CatClawMusic.Plugins.Template.csproj   # 模板工程（复制后改名）
├── Template/                              # 模板源码
│   ├── TemplatePlugin.cs                  # 插件主类（示例：ILyricsProviderPlugin）
│   ├── TemplateApiClient.cs               # 外部 API 客户端封装（HttpClient 约定）
│   └── TemplateLrcParser.cs               # 纯 .NET 工具类示例（LRC 解析）
└── README.md                              # 本指南
```

## 快速开始

1. 复制本目录为你的插件工程，例如 `D:\Code\CatClawMusic.Plugins.MyPlugin`；
2. 全局替换 `Template` → `MyPlugin`（工程文件名、csproj 的 `RootNamespace`/`AssemblyName`、
   `Template/` 目录名、类名与命名空间）；
3. 按需实现契约接口（见下方速查表），删除不需要的示例文件；
4. 构建安装：

```bash
dotnet build -c Release
```

产物 `bin/Release/net10.0/CatClawMusic.Plugins.Template.ccp`（改名为
`CatClawMusic.Plugins.MyPlugin.ccp`），在宿主应用
**插件管理 → ＋ 添加 → 本地安装** 导入后启用。

> 插件是裸 .NET 程序集：文件扩展名 `.ccp` 就是 DLL。宿主通过 `Assembly.Load`
> 加载后扫描 `IPlugin` 实现——第一个实例为主插件，其余自动成为子插件。

## 构建依赖

| 依赖 | 说明 |
|------|------|
| `CatClawMusic.Core` | 契约接口与模型定义，必须引用（默认相对路径 `..\CatClawMusic.Core`，可用 `-p:CatClawCoreProject=...` 覆盖指向任意位置的宿主仓库） |
| `Microsoft.Maui.Controls` | 仅做 UI 插件时需要（版本与宿主一致，模板已注释好） |
| `CommunityToolkit.Mvvm` | 仅做 ViewModel 时需要 |

`CopyLocalLockFileAssemblies=false`：插件不携带依赖，全部由宿主提供——
因此**禁止**在插件内引入宿主不存在的第三方包（除非做成嵌入资源或拷贝进 .ccp）。

## 插件生命周期

```
宿主启动 → LoadInstalledPlugins（Assembly.Load .ccp）
        → InitializeAsync()（每个启用的插件实例）
        → 运行期按契约接口被调用（GetLyricsAsync / SearchAsync / CreateEntryPage ...）
        → ShutdownAsync()（目前宿主未接线退出通知，见下方"宿主已知缺口"）
```

## 获取宿主服务

- **契约方法传入 DI 容器**：`IViewContributorPlugin.CreateEntryPage(IServiceProvider)`、
  `IMenuContributorPlugin.OnMenuItemClicked(..., object fragment)` 等可直接解析
  `PlayQueue`、`IAudioPlayerService`、`MusicDatabase`、`ILyricsService`、`INavigationService`、
  `IDialogService` 等；
- **后台/无参方法**：`MauiProgram.Services`（public static IServiceProvider）可自行
  `GetService<T>()`；
- **复用宿主公开静态工具**：如 `LyricsService.TryParseLyrics(text)`（LRC/TTML/AMLL
  自动识别）、`TagReader` 的写方法（`WriteMetadata`/`WriteCoverToFile`/
  `WriteEmbeddedLyrics`）、`DownloadManager.EnqueueStream`（给外部注入下载源）。

## 扩展点速查表（宿主 Core 契约接口）

✅ = 宿主已接线消费；⚠️ = 接口已定义但宿主未接线（插件实现暂不生效，需等宿主接线）

| 契约接口 | 状态 | 宿主消费位置 | 做什么 |
|----------|------|-------------|--------|
| `IOnlineMusicPlugin` | ✅ | OnlineMusicAggregator 聚合搜索、LyricsService RemoteId 歌词路由、WebViewLogin 浏览器登录、红心同步 | 在线音源：搜索/歌单/播放直链/歌词/FM |
| `IViewContributorPlugin` | ✅ | 发现页顶栏入口按钮 | 贡献一个完整入口页（宿主 Push 你的 ContentPage） |
| 发现子 tab（鸭子类型） | ✅ | 反射探测 `TabTitle`/`TabIcon`/`TabOrder`/`CreateTabView` | 在发现页内嵌 tab，宿主零 Core 依赖 |
| `ILyricsProviderPlugin` | ✅ | LyricsService 歌词兜底链 | 为本地/远程歌曲在线补歌词（当前零实现，装上即生效） |
| `IThemeProviderPlugin` | ⚠️ | 未接线 | 主题皮肤包 |
| `IProtocolProviderPlugin` | ⚠️ | 未接线 | FTP/SFTP/网络电台等远程协议 |
| `ICoverProviderPlugin` | ⚠️ | 未接线 | 在线补封面 |
| `IMenuContributorPlugin` | ⚠️ | 未接线 | 歌曲长按菜单注入项 |
| `IPluginConfigurable` | ⚠️ | 未接线 | 插件自带配置页（宿主仍是硬编码网易云 Cookie 弹窗） |
| `IPlayerPagePlugin` | ⚠️ | 未接线 | 替换播放页 |
| `IAudioVisualizerPlugin` / `IAudioEnhancerPlugin` | ⚠️ | 未接线 | 频谱可视化 / 音效增强（依赖宿主采样管线，改动量大） |

## 已知宿主缺口（做插件时避坑）

- `Song.Duration` 单位不一致（注释毫秒、部分写入路径存秒）：消费侧请做防御
  `> 1000 ? ms : s`，已发布的 LRCLIB 插件就是这么做并经过实测；
- `IAudioPlayerService` 无 `CurrentSongChanged` 事件（只有 PlaybackStateChanged/
  PositionChanged/DurationChanged/PlaybackCompleted），做切歌联动可监听
  `PlaybackCompleted` + 轮询兜底；
- `PlaySession` 只保留最近 2000 条，做年度报告/Scrobble 需趁早或先改宿主；
- `Assembly.Load` 进默认 ALC，禁用插件后程序集仍驻留内存（需重启生效）；
- 无插件 manifest（宿主版本区间/依赖声明），兼容性靠反射适配器兜底：
  插件编译请始终引用与宿主**同一份** Core 工程，避免接口类型不一致落入反射代理路径。

## 发布

把构建出的 `.ccp` 上传到 GitHub Release Assets，宿主支持「网络安装」：
**插件管理 → ＋ 添加 → 从 GitHub Release 安装**（填入 owner/repo/tag 即可）。

## 参考实现

| 插件 | 仓库 | 演示点 |
|------|------|--------|
| 网易云音乐音源 | [kankejiang/CatClawMusic.Plugins.Netease](https://github.com/kankejiang/CatClawMusic.Plugins.Netease) | IOnlineMusicPlugin + IViewContributorPlugin + 发现子 tab + MAUI 纯代码 UI + 浏览器登录 |
| LRCLIB 在线歌词 | [kankejiang/CatClawMusic.Plugins.Lrclib](https://github.com/kankejiang/CatClawMusic.Plugins.Lrclib) | ILyricsProviderPlugin + 匹配评分 + 内存缓存（最小完整示例，建议先读） |

插件候选清单与实施路线：`docs/plugin-development-candidates.md`。
