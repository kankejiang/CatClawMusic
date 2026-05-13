# CatClawMusic v1.0.5 Release Notes

## 🎉 版本亮点

本次更新完善了文档并增强了播放体验，包含播放状态持久化、音频焦点处理等多项改进。

---

## ✨ 新增功能

### 📝 文档完善
- **完整功能列表**：README.md 现已详细记录所有已实现功能
- **应用截图**：添加 4 张应用截图展示主要功能界面
- **截图折叠**：截图部分使用可折叠标签，保持 README 简洁

### ▶️ 播放增强
- **播放状态持久化**：自动保存和恢复播放位置、播放模式、播放队列
  - 每 5 秒自动保存进度
  - 应用重启后恢复上次播放状态（暂停状态，不自动播放）
- **音频焦点处理**：智能处理音频中断
  - 永久丢失焦点（如电话）→ 自动暂停
  - 临时丢失焦点（如导航提示）→ 暂停后可恢复
  - 降低音量（如通知音）→ 音量降至 1/3
- **Basic Auth 支持**：支持 URL 嵌入用户名密码的流媒体
  - 格式：`http://user:pass@host/path`
  - 自动提取并转换为 Authorization 请求头

### 🎶 歌词改进
- **网络歌词缓存**：缓存远程歌词到磁盘，提升加载速度
  - 缓存位置：`Context.CacheDir/lyrics/lyrics_{songId}.lrc`
  - 优先读取缓存，减少网络请求
- **拖拽指示器**：歌词拖拽定位时显示水平虚线和跳转按钮
- **毛玻璃效果**：Android 12+ 全屏歌词页面支持模糊背景

### 💚 收藏与历史优化
- **收藏保留**：网络重新扫描后自动恢复收藏状态
  - 清除旧数据前保存 RemoteId → AddedAt 映射
  - 重新扫描后根据 RemoteId 恢复收藏
- **播放历史去重计次**：同一歌曲多次播放只保留一条记录，计数播放次数

---

## 🔧 技术改进

### 网络功能
- **Navidrome 增量扫描优化**：逐专辑回调，提升大型音乐库响应速度
- **WebDAV Range 请求**：支持只下载文件头部，用于提取 Tag 信息

### 数据库优化
- **索引优化**：为 Songs、Albums、PlayHistory 表添加索引，提升查询速度
- **批量操作**：优化网络歌曲入库性能

### ExoPlayer 深度集成
- **自定义 DataSource**：支持 content:// URI 和 HTTP URL 混合播放
- **WakeLock 改进**：防止 CPU 休眠，确保后台播放稳定
- **WiFi Lock**：防止锁屏后网络断开

---

## 📦 依赖更新

- Xamarin.AndroidX.Media3.ExoPlayer: 1.10.0
- Xamarin.Google.Android.Material: 1.12.0
- CommunityToolkit.Mvvm: 8.2.2
- Microsoft.Extensions.DependencyInjection: 9.0.0

---

## 🐛 Bug 修复

- 修复播放队列恢复时可能出现的索引越界问题
- 修复网络歌词获取失败时的错误处理
- 修复收藏状态在网络重新扫描后丢失的问题
- 修复播放历史记录重复问题

---

## 📱 系统要求

- **最低版本**：Android 12 (API 31)
- **目标版本**：Android 14 (API 34)
- **.NET 版本**：.NET 9.0
- **架构**：ARM64, ARM32, x86_64

---

## 🚀 安装说明

1. 下载 `CatClawMusic-v1.0.5.apk`
2. 在 Android 设备上启用"未知来源"安装
3. 安装 APK 文件
4. 首次启动需要授予存储权限和悬浮窗权限

---

## 📝 完整变更日志

**新增文件**：
- `images/screenshot-1.jpg` - 歌词页面截图
- `images/screenshot-2.jpg` - 播放列表截图
- `images/screenshot-3.jpg` - 音乐库截图
- `images/screenshot-4.jpg` - 设置页面截图
- `设计文档-v2.0.md` - 详细设计文档

**核心代码变更**：
- `AudioPlayerService.cs` (+94 行) - 音频焦点、Basic Auth、播放状态持久化
- `MusicDatabase.cs` (+166 行) - 播放历史去重计次、收藏保留、索引优化
- `NowPlayingViewModel.cs` (+23 行) - 网络歌词缓存
- `PlaylistViewModel.cs` (+52 行) - 播放列表管理优化
- `README.md` (重大更新) - 完善功能说明

---

## 🔮 下一步计划

- 支持更多网络协议（SMB、DLNA、FTP、NFS）
- 桌面歌词自定义字体
- 智能播放推荐
- 歌词搜索与编辑

---

## 🙏 致谢

感谢所有贡献者和用户的支持！

---

**发布日期**：2026 年 5 月 13 日  
**提交哈希**：ed16469  
**标签**：v1.0.5
