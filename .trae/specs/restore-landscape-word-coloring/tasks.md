# Tasks
- [x] Task 1: 恢复横屏模式逐字着色功能
  - [x] SubTask 1.1: 恢复 `ApplyDefaultLyricColors` 中 `_lyricCurrent` 的 `SungColor`/`UnsungColor` 设置
  - [x] SubTask 1.2: 恢复 `ApplyCurrentLineWithSpannable` 中的渐变分支（LyricStyle == 1 时使用 `SetSpannableWithProgress`）
  - [x] SubTask 1.3: 恢复 `CurrentLyricSpannable` 的 PropertyChanged 事件处理
  - [x] SubTask 1.4: 恢复 `CurrentLyricProgress` 的 PropertyChanged 事件处理
  - [x] SubTask 1.5: 恢复 `SyncUIFromViewModel` 中 `UpdateLyricSpannable` 预创建逻辑
- [x] Task 2: 构建验证

# Task Dependencies
- Task 2 depends on Task 1
