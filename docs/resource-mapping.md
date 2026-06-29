# CatClawMusic 资源映射文档

本文档记录从 Xamarin.Android 到 .NET MAUI 的图标资源迁移映射。

## 迁移概述

- **源目录**: `CatClawMusic.UI/Resources/drawable/`
- **目标目录**: `CatClawMusic.Maui/Resources/Images/`
- **迁移日期**: 2025-06-30
- **总计文件**: 59个（4个PNG + 55个SVG）

## PNG 文件映射

以下 PNG 文件已直接复制到 MAUI 项目：

| 原始文件名 | MAUI 引用路径 | 用途 | 大小 |
|-----------|--------------|------|------|
| `avatar_yuki.png` | `Images/avatar_yuki.png` | 用户头像 | 4.9 MB |
| `cover_default.png` | `Images/cover_default.png` | 默认专辑封面 | 7.0 MB |
| `splash_cat_dark.png` | `Images/splash_cat_dark.png` | 深色模式启动图 | 2.7 MB |
| `splash_cat_light.png` | `Images/splash_cat_light.png` | 浅色模式启动图 | 3.2 MB |

**注意**: `splash_cat_dark.png` 和 `splash_cat_light.png` 在 MAUI 中通常不使用，因为 MAUI 使用单独的 Splash 屏幕配置。这些文件已复制但可能需要后续处理。

## SVG 图标映射

以下 Vector Drawable 已转换为 SVG 格式：

### 导航和操作图标

| 原始文件名 | MAUI 引用路径 | 用途 | 原始颜色 | 转换后颜色 |
|-----------|--------------|------|---------|-----------|
| `ic_home.xml` | `Images/ic_home.svg` | 首页 | `?attr/catClawTabInactive` | `#666666` |
| `ic_library.xml` | `Images/ic_library.svg` | 音乐库 | `#000000` | `#000000` |
| `ic_search.xml` | `Images/ic_search.svg` | 搜索 | `#D4C5C9` (tint) | `#D4C5C9` |
| `ic_settings.xml` | `Images/ic_settings.svg` | 设置 | `#D4C5C9` (tint) | `#D4C5C9` |
| `ic_menu.xml` | `Images/ic_menu.svg` | 菜单 | `#000000` | `#000000` |
| `ic_close.xml` | `Images/ic_close.svg` | 关闭 | `#000000` | `#000000` |
| `ic_back.xml` | `Images/ic_back.svg` | 返回 | `#000000` | `#000000` |
| `ic_arrow_back.xml` | `Images/ic_arrow_back.svg` | 后退 | `#000000` | `#000000` |
| `ic_arrow_forward.xml` | `Images/ic_arrow_forward.svg` | 前进 | `#000000` | `#000000` |

### 播放控制图标

| 原始文件名 | MAUI 引用路径 | 用途 | 原始颜色 | 转换后颜色 |
|-----------|--------------|------|---------|-----------|
| `ic_play.xml` | `Images/ic_play.svg` | 播放 | `#FFFFFF` | `#FFFFFF` |
| `ic_pause.xml` | `Images/ic_pause.svg` | 暂停 | `#FFFFFF` | `#FFFFFF` |
| `ic_skip_next.xml` | `Images/ic_skip_next.svg` | 下一曲 | `#000000` | `#000000` |
| `ic_skip_previous.xml` | `Images/ic_skip_previous.svg` | 上一曲 | `#000000` | `#000000` |
| `ic_repeat.xml` | `Images/ic_repeat.svg` | 循环播放 | `#000000` | `#000000` |
| `ic_repeat_one.xml` | `Images/ic_repeat_one.svg` | 单曲循环 | `#000000` | `#000000` |
| `ic_shuffle.xml` | `Images/ic_shuffle.svg` | 随机播放 | `#000000` | `#000000` |
| `ic_equalizer.xml` | `Images/ic_equalizer.svg` | 均衡器 | `#000000` | `#000000` |
| `ic_eq.xml` | `Images/ic_eq.svg` | 均衡器（详细） | `#000000` | `#000000` |

### 收藏和喜欢图标

| 原始文件名 | MAUI 引用路径 | 用途 | 原始颜色 | 转换后颜色 |
|-----------|--------------|------|---------|-----------|
| `ic_favorite.xml` | `Images/ic_favorite.svg` | 收藏 | `?attr/catClawPrimaryColor` | `#FF6B9D` |
| `ic_favorite_border.xml` | `Images/ic_favorite_border.svg` | 未收藏 | `#000000` | `#000000` |

### 通知栏图标

| 原始文件名 | MAUI 引用路径 | 用途 |
|-----------|--------------|------|
| `ic_notif_play.xml` | `Images/ic_notif_play.svg` | 通知栏播放 |
| `ic_notif_pause.xml` | `Images/ic_notif_pause.svg` | 通知栏暂停 |
| `ic_notif_next.xml` | `Images/ic_notif_next.svg` | 通知栏下一曲 |
| `ic_notif_previous.xml` | `Images/ic_notif_previous.svg` | 通知栏上一曲 |
| `ic_notif_favorite.xml` | `Images/ic_notif_favorite.svg` | 通知栏收藏 |
| `ic_notif_favorite_border.xml` | `Images/ic_notif_favorite_border.svg` | 通知栏未收藏 |
| `ic_notif_lyric_on.xml` | `Images/ic_notif_lyric_on.svg` | 通知栏歌词开 |
| `ic_notif_lyric_off.xml` | `Images/ic_notif_lyric_off.svg` | 通知栏歌词关 |

### 主题和模式图标

| 原始文件名 | MAUI 引用路径 | 用途 |
|-----------|--------------|------|
| `ic_dark_mode.xml` | `Images/ic_dark_mode.svg` | 深色模式 |
| `ic_light_mode.xml` | `Images/ic_light_mode.svg` | 浅色模式 |
| `ic_system_mode.xml` | `Images/ic_system_mode.svg` | 系统模式 |
| `ic_mode_single.xml` | `Images/ic_mode_single.svg` | 单曲模式 |
| `ic_mode_dual.xml` | `Images/ic_mode_dual.svg` | 双曲模式 |

### 其他功能图标

| 原始文件名 | MAUI 引用路径 | 用途 |
|-----------|--------------|------|
| `ic_album.xml` | `Images/ic_album.svg` | 专辑 |
| `ic_artist.xml` | `Images/ic_artist.svg` | 艺术家 |
| `ic_playlist.xml` | `Images/ic_playlist.svg` | 播放列表 |
| `ic_music_note.xml` | `Images/ic_music_note.svg` | 音乐音符 |
| `ic_folder.xml` | `Images/ic_folder.svg` | 文件夹 |
| `ic_person.xml` | `Images/ic_person.svg` | 个人 |
| `ic_edit.xml` | `Images/ic_edit.svg` | 编辑 |
| `ic_delete.xml` | `Images/ic_delete.svg` | 删除 |
| `ic_refresh.xml` | `Images/ic_refresh.svg` | 刷新 |
| `ic_filter.xml` | `Images/ic_filter.svg` | 过滤 |
| `ic_sort.xml` | `Images/ic_sort.svg` | 排序 |
| `ic_lyrics.xml` | `Images/ic_lyrics.svg` | 歌词 |
| `ic_sleep.xml` | `Images/ic_sleep.svg` | 睡眠定时 |
| `ic_lock.xml` | `Images/ic_lock.svg` | 锁定 |
| `ic_lock_locked.xml` | `Images/ic_lock_locked.svg` | 已锁定 |
| `ic_landscape.xml` | `Images/ic_landscape.svg` | 横屏 |
| `ic_send.xml` | `Images/ic_send.svg` | 发送 |
| `ic_check.xml` | `Images/ic_check.svg` | 确认 |
| `ic_check_box.xml` | `Images/ic_check_box.svg` | 复选框选中 |
| `ic_check_box_outline.xml` | `Images/ic_check_box_outline.svg` | 复选框未选中 |

### Widget 图标

| 原始文件名 | MAUI 引用路径 | 用途 |
|-----------|--------------|------|
| `ic_widget_favorite.xml` | `Images/ic_widget_favorite.svg` | Widget收藏 |
| `ic_widget_favorite_border.xml` | `Images/ic_widget_favorite_border.svg` | Widget未收藏 |
| `ic_widget_next.xml` | `Images/ic_widget_next.svg` | Widget下一曲 |
| `ic_widget_previous.xml` | `Images/ic_widget_previous.svg` | Widget上一曲 |

## 跳过的文件

以下文件在迁移过程中被跳过，因为它们是 Android 特有的背景或控件样式，在 MAUI 中有不同的实现方式：

| 文件名 | 类型 | 跳过原因 | MAUI 替代方案 |
|-------|------|---------|--------------|
| `bg_*.xml` (10个文件) | 背景形状 | Android 背景资源 | 使用 MAUI 的 `Frame`, `Border`, 或 `GraphicsView` |
| `cb_*.xml` (3个文件) | 复选框样式 | Android 控件样式 | 使用 MAUI 的 `CheckBox` 控件 |
| `btn_jump_bg.xml` | 按钮背景 | Android 按钮样式 | 使用 MAUI 的 `Button` 样式 |
| `dash_line.xml` | 虚线 | Android 形状 | 使用 MAUI 的 `Path` 或自定义绘制 |
| `drag_indicator_line.xml` | 拖拽指示器 | Android 形状 | 使用 MAUI 的自定义绘制 |
| `highlighted_lyric_bg.xml` | 歌词高亮背景 | Android 背景 | 使用 MAUI 的 `BackgroundColor` |
| `red_dot.xml` | 红点指示器 | Android 形状 | 使用 MAUI 的 `Ellipse` 或 `BoxView` |
| `splash_screen.xml` | 启动屏 | Android 启动屏 | 使用 MAUI 的 `SplashScreen` 配置 |

## MAUI 中的资源使用

### 在 XAML 中使用

```xml
<!-- 显示图标 -->
<Image Source="Images/ic_play.svg" 
       WidthRequest="24" 
       HeightRequest="24" />

<!-- 使用色调 -->
<Image Source="Images/ic_favorite.svg" 
       WidthRequest="24" 
       HeightRequest="24"
       TintColor="{DynamicResource PrimaryColor}" />
```

### 在 C# 中使用

```csharp
// 从资源加载图片
var imageSource = ImageSource.FromFile("Images/ic_play.svg");

// 在 Image 控件中显示
myImage.Source = imageSource;
```

### 项目文件配置

确保在 `CatClawMusic.Maui.csproj` 中包含以下配置：

```xml
<ItemGroup>
  <MauiImage Include="Resources\Images\**" />
</ItemGroup>
```

## 颜色处理说明

### 颜色引用替换

在转换过程中，以下 Android 颜色引用已被替换为具体的颜色值：

| Android 引用 | 替换值 | 说明 |
|------------|--------|------|
| `?attr/catClawPrimaryColor` | `#FF6B9D` | 应用主色调（粉色） |
| `?attr/catClawTabInactive` | `#666666` | 非活动标签颜色 |
| `?attr/catClawTextPrimary` | `#FFFFFF` | 主要文本颜色 |
| `@android:color/white` | `#FFFFFF` | Android 白色 |
| `@android:color/black` | `#000000` | Android 黑色 |

### 动态颜色支持

如果需要在 MAUI 中支持动态颜色（如深色/浅色模式），可以：

1. **移除 SVG 中的 fill 属性**，使其在 XAML 中可以通过 `TintColor` 设置
2. **使用 MAUI 的主题系统**，为不同主题定义不同的颜色

示例 - 修改 SVG 以支持动态颜色：

```svg
<!-- 修改前 -->
<path fill="#FF6B9D" d="..." />

<!-- 修改后（移除 fill，使用 TintColor） -->
<path d="..." />
```

然后在 XAML 中：

```xml
<Image Source="Images/ic_favorite.svg" 
       TintColor="{AppThemeBinding Light={StaticResource PrimaryColor},
                                Dark={StaticResource PrimaryColorDark}}" />
```

## 验证清单

- [x] 所有 PNG 文件已复制
- [x] 所有简单 Vector Drawable 已转换为 SVG
- [x] 颜色引用已正确替换
- [x] SVG 文件格式正确
- [ ] MAUI 项目文件已配置（需要检查）
- [ ] 构建测试通过（需要验证）
- [ ] 图标在应用中正确显示（需要测试）

## 待办事项

1. **验证 MAUI 项目配置** - 检查 `.csproj` 文件是否包含 `MauiImage` 配置
2. **构建测试** - 运行项目构建，确保资源被正确识别
3. **运行时测试** - 在模拟器或设备上测试图标显示
4. **优化 SVG** - 对于需要动态颜色的图标，考虑移除 `fill` 属性
5. **处理跳过的文件** - 为跳过的背景和形状创建 MAUI 等效实现

## 参考资料

- [MAUI 图像文档](https://learn.microsoft.com/dotnet/maui/user-interface/images/)
- [MAUI 图标文档](https://learn.microsoft.com/dotnet/maui/user-interface/images/icons)
- [SVG 在 MAUI 中使用](https://learn.microsoft.com/dotnet/maui/user-interface/images/svg)

---

**最后更新**: 2025-06-30  
**执行人**: software-engineer (Alex)
