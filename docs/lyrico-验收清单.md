# Lyrico 移植：验收状态

> 更新：P1 写入链路、P2 批量、P3 源插件、P4 设置均已真机验收。
> 剩余待验集中在「需要特定素材或网络」的少数项。

## 环境准备

```powershell
$adb = "C:\Users\lvjin\AppData\Local\Android\Sdk\platform-tools\adb.exe"
& $adb devices                                   # 确认设备在线
& $adb shell svc power stayon true               # 防休眠
& $adb shell input keyevent 224                  # 唤醒
& $adb shell wm dismiss-keyguard                 # 解锁
& $adb shell settings put system accelerometer_rotation 0
& $adb shell settings put system user_rotation 0 # 固定竖屏
```

**已知环境坑**（都踩过）：
- 设备会进 Dozing，`screencap` 返回空文件 → 交互前确认截图非空
- install 后旧 Activity 栈复用，`onCreate` 不重跑 → 先 `am force-stop` 再 `am start`
- `adb shell input text` **不支持中文**，搜索测试用英文/数字关键词
- 应用会恢复上次页面（常见播放页）→ 导航前先找「收起播放页」
- 「音乐库」是底部 tab 到处都有，不能作为判据；要判「批量处理 / 扫描音乐 / 资料库」
- **Toast 类提示只存活约 2.6 秒** —— 验证时要保存后 1.2s 内抓屏，否则误判为「无提示」
- **播放页歌词不在 `uiautomator dump` 里** —— 歌词用 Canvas 逐帧绘制（逐字高亮需要像素级控制），
  只能靠截图判断。用 dump 找歌词文本会永远找不到，从而误判为「无歌词」

---

## P0 地基 ✅

| 项 | 结果 |
|---|---|
| TagLib 三格式往返（FLAC/MP3/M4A） | ✅ PASS |
| QuickJS 引擎自检 5 项 | ✅ PASS |
| `libtaglib.so` / `libquickjs.so` 打进 APK | ✅ PASS |
| `lyrico.db` 独立、`catclaw.db` schema 未变 | ✅ PASS（测试守护） |

## P1 单曲 ✅ 核心链路已验

| 项 | 结果 |
|---|---|
| 详情页三个入口（歌词匹配/封面匹配/编辑标签） | ✅ PASS 4/4 |
| 远程曲目「编辑标签」正确提示不可写 | ✅ PASS |
| **标签读取**（标题/歌手/专辑/流派等真实值） | ✅ PASS |
| **标签写入文件**（改标题 → 重启后仍在） | ✅ PASS（本地 FLAC 真实写入） |
| **曲库同步**（详情页标题立即更新） | ✅ PASS |
| **保存反馈**（"已写入 1 个字段，曲库已同步"） | ✅ PASS |
| 歌词匹配 → 候选 → 写入 | ✅ PASS（回归：网易云 49 行逐字，写入+回读通过） |
| 封面搜索 → 写入 | ✅ PASS（`已写入封面（56 KB）`） |
| 格式转换（4 种编码往返） | ✅ 单测覆盖 |
| 播放联动（写入歌词后不切歌即刷新） | ✅ PASS（截图证实：写入后播放页显示新歌词） |

## P2 批量 ✅ 全部通过

| 项 | 结果 |
|---|---|
| 进入批量页 | ✅ PASS（修复前**闪退**） |
| 全选 / 清空 | ✅ PASS |
| 搜索过滤 / 跨搜索保留已选 | ✅ PASS |
| 选择操作 → 4 个格式 chip | ✅ PASS |
| 执行任务 | ✅ PASS |
| 任务列表（计数/状态） | ✅ PASS |
| 任务详情逐首原因 | ✅ PASS（「跳过 · 没有内嵌歌词」） |
| 远程曲目正确 Skip 而非报错 | ✅ PASS |

## P3 源插件 ✅ 全部通过

| 项 | 结果 |
|---|---|
| 设置 → 歌词源插件 入口 | ✅ PASS |
| 列出内置源（网易云音乐 / LrcShare） | ✅ PASS（修复前显示 `@plugin.name`） |
| 内置标记 / 内置源无卸载入口 | ✅ PASS |
| 启停可切换 | ✅ PASS |
| 导入ZIP / 重新扫描 / 检查更新 按钮 | ✅ PASS |
| ZIP 实际导入安装 | ✅ PASS（自造测试包，装到 `files/lyrico-plugins/`） |
| 用户插件有「卸载」入口、内置源没有 | ✅ PASS |
| 导入后结构完整（含 includeDirs 的 lib/ 与 locales/） | ✅ PASS |
| 在线目录拉取 | ✅ PASS（拉到 4 个，修复前因上游改名一直失败） |
| 在线安装实际下载安装 | ✅ PASS（`已安装 Apple Music v0.4.1`） |
| 版本比对（已装显示「已是最新」） | ✅ PASS |
| 插件日志进诊断页 | ✅ PASS（`Lyrico:*` 前缀可见） |

## P4 设置 ✅ 全部通过

| 项 | 结果 |
|---|---|
| 歌词处理入口 | ✅ PASS |
| 清理规则分组 + 开关切换 | ✅ PASS |
| 编辑字段分组 + 开关切换 | ✅ PASS |
| 艺人拆分 / 字符映射 分组 | ✅ PASS |
| 恢复默认按钮 | ✅ PASS |
| 开源许可页 + 组件与许可标注 | ✅ PASS |
| 本地修改说明（Apache-2.0 §4(b)） | ✅ PASS |

---

## 真机验收发现并修复的缺陷（5 个）

| # | 问题 | 根因 | 状态 |
|---|---|---|---|
| 1 | 批量页一打开就**闪退** | `已选 %1 首`（`$d` 被 shell 吃掉）；`stringResource` 抛 `UnknownFormatConversionException` | ✅ 修 + 测试守护 |
| 2 | 覆盖层底部被播放栏遮挡（点歌行点到播放栏） | 8 个页面缺底部安全区 | ✅ 修 |
| 3 | 搜索框聚焦后键盘盖住操作区 | 缺 `imePadding()` | ✅ 修 |
| 4 | 插件列表标题显示 `@plugin.name` | `PluginRegistry` 只给 description 做 i18n，漏了 name | ✅ 修 + 测试守护 |
| 5 | 标签保存后详情页不刷新 | `produceState` 只以 songId 为 key，同 id 不重载 | ✅ 修（加 `SongMetadataRefreshBus`） |

## 已知遗留

1. **ReplayGain 未实现** —— 需 ffmpeg + ebur128 两个未移植的原生库。
   选中时提示会全部跳过；处理时返回 Skipped 并说明原因，不静默失败。

2. **Lyrico 的播放器 / 桌面歌词 / 双皮肤 / Pro 页未移植** —— 按既定范围排除。

3. **`:data` 模块未应用 kotlinx-serialization 插件** —— 核查后确认**并非缺陷**：
   `BackupJson` / `ChatMemoryStore` 都是有意手写 JSON（注释写明跨端键名要锁定）。
   全仓库真正用 `@Serializable` 的只有 `:lyrico` 的插件 manifest，该模块已应用插件。

4. ~~测试数据被改动~~ —— 已还原：本地歌「不浪漫罪名」的标题在验证写入链路时被追加过
   `X/Y` 后缀，现已改回原值并确认持久化（列表与详情均显示「不浪漫罪名」）。
