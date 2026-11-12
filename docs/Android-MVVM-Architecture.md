# CatClawMusic Android 架构规范（MVVM）

> 2026-11-12 起强制执行。新增/修改代码必须遵守；存量代码按「迁移清单」分批收口。
> 包根：`com.catclaw.music.kt`，模块：`:core`（纯 Kotlin）/ `:data`（Android 实现）/ `:app`（Compose UI）。

## 1. 分层与依赖方向

```
:app  ui/          Compose Screen + ViewModel（只依赖 domain/repository 接口与 UI 状态模型）
        │  ── 单向 ──▶
        ▼
:app  data/…        Repository（聚合 DAO / 网络 / 缓存 / 播放器，单例状态源）
        │
        ▼
:data  db/ netease/ cache/ service/   DAO、HTTP、缓存、前台服务
:core  model/ playback/ lyrics/ …     纯 Kotlin 领域模型与逻辑（无 Android 依赖）
```

依赖只能**自上而下**：`ui → data(repository) → :data/:core`。
禁止 `:core` 依赖 `:data`；禁止 `:data` 依赖 `:app`。

## 2. 硬性规则

1. **每个页面必须有 `@HiltViewModel` 的 ViewModel**，Screen 不得内联 ViewModel 类。
2. **Composable 不得直接持有**：DAO、`NeteaseApiClient`、`PlayerController`、`AudioCache`、`*Settings`、`CoroutineScope`。
   只能读 `uiState: StateFlow<XxxUiState>`，只能调 VM 的**意图方法**（`onPlayClick`、`onQueryChange`）。
3. **VM 不引用 Compose 类型**（`Color`/`Dp`/`Modifier`/`stringResource` 等一律不进 VM）。
   文案用资源 id 或字符串 key 由 UI 解析。
4. **跨页面共享状态用 Repository 单例**，禁止「借另一个页面的 VM」。
   反例：`PlayingScreen` 曾用 `hiltViewModel<LibraryViewModel>()` 拿歌曲菜单能力。
5. **一次性事件**（Toast/导航）用 `Channel`/`SharedFlow`，不要塞进 `UiState` 的 `String?`（会重复触发）。
6. 状态模型命名：`XxxUiState`（不可变 data class）+ `XxxEvent`；Repository 返回领域模型，VM 负责映射成 UiState。
7. 线程：VM 的 `viewModelScope` 默认主线程；IO（DB/网络/文件）在 Repository 或 `Dispatchers.IO` 内完成，不得阻塞 UI。
8. 新依赖一律构造注入（`@Inject constructor`），禁止在 VM/Composable 里 `new` 单例或 `Context.getSharedPreferences`。

## 3. 现状盘点（2026-11-12）

- 已有 **20 个 `@HiltViewModel`**、34 处 `hiltViewModel()`，MVVM 骨架成立。
- 主要缺口：
  - **没有 Repository 层**：VM 直接注入 `LibraryDao` / `LibraryScanner` / `AudioCache` / `NeteaseApiClient`。
  - 部分状态控制器曾放在 `ui/netease/`（已下沉，见 §4）。
  - `PlayingScreen` 借用 `LibraryViewModel` 取菜单/收藏能力。
  - `AboutScreen` / `PermissionManagementScreen` / `DiagnosticLogScreen` 的 VM 内联在 Screen 文件里。
  - `NeteaseMusicViewModel` 同时承担首页/歌单广场/搜索/详情/FM，职责过宽。

## 4. 已完成（2026-11-12）

- `ui/netease/NeteaseRadioController` → `data/netease/NeteaseRadioRepository`
- `ui/netease/NeteaseLikeController` → `data/netease/NeteaseLikeRepository`
- `ui/netease/NeteaseSongMapper` → `data/netease/NeteaseSongMapper`（数据映射属于 data 层）

## 5. 迁移清单（按优先级）

| 优先级 | 事项 | 验收 |
|---|---|---|
| P0 | 新建 `data/repository/MusicRepository`：包装 `LibraryDao`（songs/albums/artists/favorites/recent 查询） | `LibraryViewModel` 不再直接注入 `LibraryDao` |
| P0 | 新建 `data/repository/SongMenuRepository`：收藏、加入歌单、下一首播放、详情 | `PlayingScreen` 不再 `hiltViewModel<LibraryViewModel>()` |
| P1 | 拆 `NeteaseMusicViewModel`：Home / Square / Search / Detail / Radio 各自的 VM 或子状态机 | 单个 VM ≤ 300 行 |
| P1 | `AboutScreen` / `PermissionManagementScreen` / `DiagnosticLogScreen` 的 VM 拆到独立文件 | 每个 Screen 一个 VM 文件 |
| P2 | `PlaylistViewModel` / `SearchViewModel` 改用 Repository，去掉 DAO 注入 | `grep -rn "Dao" app/src/main/kotlin/.../ui/` 为空 |

## 6. 验收命令

```bash
# Composable 不得直连数据层
grep -rn "LibraryDao\|NeteaseApiClient\|PlayerController\|AudioCache" \
  app/src/main/kotlin/com/catclaw/music/kt/ui --include=*.kt

# 每个 Screen 必须有 VM
grep -rn "hiltViewModel()" app/src/main/kotlin/com/catclaw/music/kt/ui --include=*.kt

# VM 不得引用 Compose 类型
grep -rn "import androidx.compose" app/src/main/kotlin/com/catclaw/music/kt/ui/*/[A-Z]*ViewModel.kt
```
