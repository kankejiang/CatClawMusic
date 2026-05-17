# 更新日志 (CHANGELOG)

## [1.0.7] - 2025-05-17

### 🎉 MusicTag 元数据编辑插件（重大更新）

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
