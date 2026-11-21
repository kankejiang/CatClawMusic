# Lyrico 移植：验收状态

> 更新：P2 批量、P3 源插件、P4 设置已完成真机验收。
> 仍待补：P1 的回归确认（此前已通过，代码后有改动）+ 少量联网/写文件项。

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
- 设备会进 Dozing，`screencap` 返回空文件 → 交互前必须确认截图非空
- install 后旧 Activity 栈被复用，`onCreate` 不一定重跑 → 先 `am force-stop` 再 `am start`
- `adb shell input text` **不支持中文**，搜索测试要用英文/数字关键词
- 应用会恢复上次页面（常见是播放页）→ 导航前先找「收起播放页」
- 「音乐库」是底部 tab 到处都有，不能作为「当前在音乐库」的判据；
  要判「批量处理 / 扫描音乐 / 资料库」是否出现

---

## P0 地基 ✅

| 项 | 结果 |
|---|---|
| TagLib 三格式往返（FLAC/MP3/M4A 写歌词+标题后回读） | ✅ PASS |
| QuickJS 引擎自检（标量/上下文/JSON/接口绑定/嵌套）5 项 | ✅ PASS |
| `libtaglib.so` / `libquickjs.so` 打进 APK（arm64 + x86_64） | ✅ PASS |
| `lyrico.db` 独立、`catclaw.db` schema 未变 | ✅ PASS（测试守护） |

## P1 单曲

| 项 | 结果 |
|---|---|
| 歌词匹配（搜索 → 候选） | ✅ 曾 PASS |
| 歌词写入（写内嵌 → 回读校验） | ✅ 曾 PASS |
| 格式转换（4 种编码往返） | ✅ 单测覆盖 |
| 封面搜索 / 写入 | ✅ 代码完成，服务层曾真机 PASS |
| 标签编辑（读 → 改 → 写 → 回读） | ✅ 曾 PASS |
| 播放联动（写入后不切歌即刷新） | ⬜ 待验 |
| 详情页三个入口（歌词匹配 / 封面匹配 / 编辑标签） | ✅ PASS（回归，4/4） |
| 远程曲目「编辑标签」正确提示不可写 | ✅ PASS（"该文件不支持编辑"） |

## P2 批量 ✅ 全部通过（2026-11-21）

| 项 | 结果 |
|---|---|
| 进入批量页 | ✅ PASS（修复前此处**闪退**） |
| 全选 | ✅ PASS（1849 首） |
| 搜索过滤 | ✅ PASS |
| 跨搜索保留已选 | ✅ PASS |
| 选择操作 → 出现 4 个格式 chip | ✅ PASS |
| 执行任务 | ✅ PASS |
| 任务列表 | ✅ PASS（已完成 / 2 首 / 成功0·跳过2·失败0） |
| 任务详情逐首原因 | ✅ PASS（「跳过 · 没有内嵌歌词」） |
| 远程曲目正确 Skip 而非报错 | ✅ PASS |

## P3 源插件 ✅ 全部通过（2026-11-21）

| 项 | 结果 |
|---|---|
| 设置 → 歌词源插件 入口 | ✅ PASS |
| 列出内置源（网易云音乐 / LrcShare） | ✅ PASS（修复前标题显示 `@plugin.name`） |
| 内置源有「内置」标记 | ✅ PASS |
| 内置源不显示卸载入口 | ✅ PASS |
| 启停可切换 | ✅ PASS（启用 ↔ 已停用） |
| 导入 ZIP / 重新扫描 / 检查更新 按钮 | ✅ PASS |
| ZIP 导入实际安装 | ⬜ 待验（需准备插件包） |
| 在线目录拉取与安装 | ⬜ 待验（需网络稳定） |
| 插件日志进诊断页 | ⬜ 待验 |

## P4 设置 ✅ 全部通过（2026-11-21）

| 项 | 结果 |
|---|---|
| 设置 → 歌词处理 入口 | ✅ PASS |
| 清理规则分组（4 项开关） | ✅ PASS |
| 编辑字段分组（8 项开关，标题歌手专辑必显） | ✅ PASS |
| 开关可切换 | ✅ PASS（去掉首尾空白、专辑艺人） |
| 艺人拆分分组 | ✅ PASS |
| 字符映射分组 | ✅ PASS |
| 恢复默认按钮 | ✅ PASS |
| 关于 → 开源许可页 | ✅ PASS |
| 列出 Lyrico / TagLib / utfcpp 等组件 | ✅ PASS |
| 标注 Apache-2.0 / MPL-1.1 / BSL-1.0 | ✅ PASS |
| 有本地修改说明（Apache-2.0 §4(b) 合规） | ✅ PASS |

---

## 真机验收发现并修复的缺陷

| # | 问题 | 根因 | 状态 |
|---|---|---|---|
| 1 | 批量页一打开就闪退 | 字符串 `已选 %1 首`（`$d` 被 shell 变量展开吃掉）；`stringResource` 抛 `UnknownFormatConversionException` | ✅ 已修 + 加测试守护 |
| 2 | 覆盖层底部被播放栏遮挡（点歌行实际点到播放栏） | 8 个页面缺底部安全区 | ✅ 已修 |
| 3 | 搜索框聚焦后键盘盖住操作区 | 缺 `imePadding()` | ✅ 已修 |
| 4 | 插件列表标题显示 `@plugin.name` | `PluginRegistry` 只给 description 做 i18n 解析，漏了 name | ✅ 已修 + 加测试守护 |

---

## 已知遗留

1. **ReplayGain 未实现** —— 依赖 ffmpeg + ebur128 两个未移植的原生库。
   选中时提示会全部跳过，处理时返回 Skipped 并说明原因，不静默失败。

2. **Lyrico 的播放器 / 桌面歌词 / 双皮肤 / Pro 页未移植** —— 按既定范围排除
   （复用本工程既有播放器与视觉语言）。

3. **`:data` 模块未应用 kotlinx-serialization 插件** —— 核查后确认**并非缺陷**：
   该模块的 `BackupJson` / `ChatMemoryStore` 都是有意的**手写 JSON**
   （注释写明理由：跨端交换格式要锁定键名）。全仓库真正使用 `@Serializable`
   的只有 `:lyrico` 的插件 manifest，而该模块已应用插件。
