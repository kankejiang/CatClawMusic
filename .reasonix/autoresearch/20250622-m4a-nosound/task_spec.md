# Task: M4A 播放无声音
## Goal
M4A 文件播放无声音 → 通过 FFmpeg 转码使 M4A 正常播放

## Root cause (from log)
```
[FFmpeg] 初始化失败: ffmpeg
[CatClaw] FFmpeg 转码失败/未就绪，尝试 ExoPlayer 原生
```
FFmpeg 二进制未打包进 APK assets 目录

## Scoped
- 获取 Android arm64-v8a FFmpeg 二进制
- 放入 Assets/ffmpeg
- 验证构建通过
- 验证播放有声音

## Non-goals
- 不修改 FFmpegService 核心逻辑（已正确）
- 不修改 AudioPlayerService 回退逻辑（已正确）
- 不修改其他格式支持

## Success criteria
1. FFmpeg 二进制存在于 Assets/ffmpeg
2. 构建通过
3. 运行时 FFmpegService.InitializeAsync() 成功
4. M4A 文件通过 FFmpeg 转码后播放有声音
