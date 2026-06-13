# 恢复横屏模式逐字着色效果 Spec

## Why
之前按用户要求"删除横屏模式的歌词渐变效果"时，将逐字着色（StrokeTextView 的 LyricProgress 渐变）也一并移除了。用户现在发现横屏模式不再有逐字着色效果，需要恢复。

## What Changes
- 恢复 `LandscapeNowPlayingFragment` 中当前歌词行的逐字着色（LyricProgress 渐变）功能
- 恢复 `CurrentLyricSpannable` 和 `CurrentLyricProgress` 的 PropertyChanged 事件处理
- 恢复 `ApplyCurrentLineWithSpannable` 中的渐变分支
- 恢复 `ApplyDefaultLyricColors` 中 `_lyricCurrent` 的 `SungColor`/`UnsungColor` 设置
- 保留已完成的粗描边增强可读性改进（未唱行描边不受影响）

## Impact
- Affected code: `LandscapeNowPlayingFragment.cs`

## MODIFIED Requirements
### Requirement: 横屏模式当前行逐字着色
横屏模式当前歌词行 SHALL 支持逐字着色效果（与竖屏模式一致），使用 `StrokeTextView` 的 `LyricProgress` 属性实现已唱/未唱双色渐变。

#### Scenario: 逐字着色正常工作
- **WHEN** 用户在横屏模式播放歌曲且歌词样式为逐字模式（LyricStyle == 1）
- **THEN** 当前行显示逐字着色效果，已唱部分为活跃色，未唱部分为未唱色

#### Scenario: 纯文本模式正常工作
- **WHEN** 用户在横屏模式播放歌曲且歌词样式为非逐字模式（LyricStyle != 1）
- **THEN** 当前行显示纯文本，无渐变效果

#### Scenario: 未唱行描边保持
- **WHEN** 横屏模式歌词显示
- **THEN** 未唱行保持粗描边镂空效果，确保任何背景色下可读
