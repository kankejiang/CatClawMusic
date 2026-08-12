using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

using CatClawMusic.Maui.Controls;

namespace CatClawMusic.Maui.Pages;

/// <summary>正在播放页 —— 歌词引擎 partial 文件。</summary>
public partial class NowPlayingPage
{
    private void ToggleLandscapeLyricsMode()
    {
        _landscapeLyricsMode = !_landscapeLyricsMode;
        ApplyLandscapeLyricsMode();
        if (_landscapeLyricsMode)
        {
            BuildLandscapeLyricViews();
            var idx = _viewModel.CurrentLyricIndexObservable >= 0 ? _viewModel.CurrentLyricIndexObservable : 0;
            HighlightLandscapeLineWithoutScroll(idx);
            _ = Task.Delay(100).ContinueWith(_ => MainThread.BeginInvokeOnMainThread(() => HighlightLandscapeLine(idx)));
        }
    }

    /// <summary>应用横屏歌词模式的可见性</summary>
    private void ApplyLandscapeLyricsMode()
    {
        LandscapeTitleBlock.IsVisible = !_landscapeLyricsMode;
        LandscapeToolsRow.IsVisible = !_landscapeLyricsMode;
        LandscapeCurrentLyric.IsVisible = !_landscapeLyricsMode;
        LandscapeLyricsScroll.IsVisible = _landscapeLyricsMode;
        // 歌词模式下隐藏进度条与播放控件，呈现纯净歌词页；再次点击歌词恢复横屏模式
        LandscapeProgressRow.IsVisible = !_landscapeLyricsMode;
        LandscapeControlsRow.IsVisible = !_landscapeLyricsMode;

        // 歌词模式下右栏内容变化但封面布局不变（封面始终贴上下边、左距=状态栏+5px）
    }

    /// <summary>构建横屏多行歌词视图（目标为 LandscapeLyricStack）。
    /// 横屏歌词不用 Scale 放大，是直接改 FontSize 16→19（约 1.1875x），因此 StaticLayout 在当前行字体变大时，
    /// 相同的控件宽度容纳字数会减少 → 最后 1-2 个字会被换出新行或超出。这里用与竖屏相同的方案：
    /// Label 包 ContentView，LayoutChanged 时按 / 横屏放大倍率 收缩 WidthRequest，让静态布局时字号 16 的宽度更小，
    /// 字号切到 19 后也不会溢出父容器。</summary>
    private void BuildLandscapeLyricViews()
    {
        LandscapeLyricStack.Children.Clear();
        _landscapeLyricLabels.Clear();
        _landscapeLyricBorders.Clear();
        _landscapeLastHighlight = -1;

        var lines = _viewModel.AllLyricLines;
        // 横屏字号放大倍率：当前行 19 / 非当前行 16 = 1.1875，取 1.22 留一点余量
        const double landscapeFontScale = 1.22;
        double WrappedLandscapeLabelWidth(double parentW)
            => parentW > 0 ? Math.Max(60, parentW / landscapeFontScale - 1) : -1;
        var align = _settings.ToLayoutOptions().Alignment;
        double anchorX = align == LayoutAlignment.Center ? 0.5 : (align == LayoutAlignment.End ? 1.0 : 0.0);

        if (lines == null || lines.Count == 0)
        {
            var label = new KaraokeLabel
            {
                Text = _viewModel.NoLyricsText,
                FontSize = 16,
                FontFamily = "OpenSansSemibold",
                TextColor = (Color)Application.Current!.Resources["TextHintColor"],
                OutlineColor = Color.FromRgba(1f, 1f, 1f, 0.5f),
                StrokeWidth = 1,
                FillProgress = 1,
                HorizontalTextAlignment = TextAlignment.Start,
                HorizontalOptions = LayoutOptions.Fill
            };
            LandscapeLyricStack.Children.Add(label);
            return;
        }

        foreach (var line in lines)
        {
            var label = new KaraokeLabel
            {
                Text = line.Text,
                FontSize = 16,
                FontFamily = "OpenSansSemibold",
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.White,
                OutlineColor = Color.FromRgba(1f, 1f, 1f, 0.5f),
                StrokeWidth = 2,
                FillProgress = 0,
                HorizontalTextAlignment = _settings.ToTextAlignment(),
                HorizontalOptions = _settings.ToLayoutOptions(),
                LineBreakMode = LineBreakMode.WordWrap,
                Opacity = 0.2,
                Padding = new Thickness(0, 6)
            };
            label.AnchorX = anchorX;
            label.AnchorY = 0.5;

            var border = new Border
            {
                // 透明容器不要圆角：StrokeShape 同时是裁剪形状，会裁掉放大后歌词的四角
                StrokeThickness = 0,
                BackgroundColor = Colors.Transparent,
                Padding = new Thickness(0),
                HorizontalOptions = LayoutOptions.Fill
            };
            var host = new ContentView { Content = label, HorizontalOptions = LayoutOptions.Fill };
            host.LayoutChanged += (s, _) =>
            {
                if (s is View v && v.Width > 0)
                    label.WidthRequest = WrappedLandscapeLabelWidth(v.Width);
            };
            border.Content = host;

            if (!string.IsNullOrEmpty(line.Translation))
            {
                var stack = new VerticalStackLayout { Spacing = 10, HorizontalOptions = LayoutOptions.Fill };
                stack.Children.Add(border);

                var transLabel = new KaraokeLabel
                {
                    Text = line.Translation,
                    FontSize = 14,
                    FontFamily = "OpenSansSemibold",
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.White,
                    OutlineColor = Color.FromRgba(1f, 1f, 1f, 0.5f),
                    StrokeWidth = 1.5,
                    FillProgress = 0,
                    HorizontalTextAlignment = _settings.ToTextAlignment(),
                    HorizontalOptions = _settings.ToLayoutOptions(),
                    LineBreakMode = LineBreakMode.WordWrap,
                    Opacity = 0.2,
                    Padding = new Thickness(0, 6)
                };
                transLabel.AnchorX = anchorX;
                transLabel.AnchorY = 0.5;
                var transBorder = new Border
                {
                    StrokeThickness = 0,
                    BackgroundColor = Colors.Transparent,
                    Padding = new Thickness(0),
                    HorizontalOptions = LayoutOptions.Fill
                };
                var transHost = new ContentView { Content = transLabel, HorizontalOptions = LayoutOptions.Fill };
                transHost.LayoutChanged += (s, _) =>
                {
                    if (s is View v && v.Width > 0)
                        transLabel.WidthRequest = WrappedLandscapeLabelWidth(v.Width);
                };
                transBorder.Content = transHost;
                stack.Children.Add(transBorder);
                LandscapeLyricStack.Children.Add(stack);
            }
            else
            {
                LandscapeLyricStack.Children.Add(border);
            }

            _landscapeLyricLabels.Add(label);
            _landscapeLyricBorders.Add(border);
        }

        var idx = _viewModel.CurrentLyricIndexObservable >= 0 ? _viewModel.CurrentLyricIndexObservable : 0;
        HighlightLandscapeLineWithoutScroll(idx);
    }

    private void HighlightLandscapeLineWithoutScroll(int index)
    {
        if (index < 0 || index >= _landscapeLyricLabels.Count) return;

        for (int i = 0; i < _landscapeLyricLabels.Count; i++)
        {
            var lbl = _landscapeLyricLabels[i];

            if (i == index)
            {
                lbl.FontSize = 19;
                lbl.FontAttributes = FontAttributes.None;
                lbl.FillProgress = _viewModel.CurrentLineFillProgress;
                lbl.Opacity = 1.0;
            }
            else
            {
                lbl.FontAttributes = FontAttributes.None;
                lbl.FillProgress = 0;
                lbl.FontSize = 16;
                lbl.Opacity = 0.35;
            }
        }

        _landscapeLastHighlight = index;
    }

    private void HighlightLandscapeLine(int index)
    {
        if (index < 0 || index >= _landscapeLyricLabels.Count) return;

        var affectedMin = Math.Max(0, Math.Min(index, _landscapeLastHighlight) - 5);
        var affectedMax = Math.Min(_landscapeLyricLabels.Count - 1, Math.Max(index, _landscapeLastHighlight) + 5);

        for (int i = affectedMin; i <= affectedMax; i++)
        {
            var lbl = _landscapeLyricLabels[i];

            if (i == index)
            {
                lbl.FontSize = 19;
                lbl.FontAttributes = FontAttributes.None;
                lbl.FillProgress = _viewModel.CurrentLineFillProgress;
                lbl.Opacity = 1.0;
            }
            else
            {
                lbl.FontAttributes = FontAttributes.None;
                lbl.FillProgress = 0;
                lbl.FontSize = 16;
                lbl.Opacity = 0.35;
            }
        }

        _landscapeLastHighlight = index;

        ScrollToLandscapeLine(index);
    }

    private async void ScrollToLandscapeLine(int index)
    {
        if (index < 0 || index >= _landscapeLyricLabels.Count) return;

        try
        {
            var label = _landscapeLyricLabels[index];

            // 与竖屏同理：布局未完成时 label.Height=0，需重试直到布局就绪。
            // 使用原生 GetLocationOnScreen 获取精确坐标，避免 MAUI Y 属性不准确。
            // 使用原生 Android ScrollView.SmoothScrollTo 替代 MAUI ScrollToAsync（后者在 Android 上不可靠）。
            for (int attempt = 0; attempt < 12; attempt++)
            {
                if (label.Height > 0)
                {
#if ANDROID
                    if (label.Handler?.PlatformView is Android.Views.View nativeLabel
                        && LandscapeLyricsScroll.Handler?.PlatformView is Android.Widget.ScrollView nativeScroll)
                    {
                        var labelLoc = new int[2];
                        var scrollLoc = new int[2];
                        nativeLabel.GetLocationOnScreen(labelLoc);
                        nativeScroll.GetLocationOnScreen(scrollLoc);
                        // viewportY = label 中心在 ScrollView 可见区域中的 Y 坐标
                        var viewportY = labelLoc[1] - scrollLoc[1] + nativeLabel.Height / 2;
                        // 目标滚动位置 = 当前滚动偏移 + (viewportY - 期望位置)
                        // 期望位置 = ScrollView 高度的 1/3（与竖屏一致的视觉效果）
                        int targetScrollY = nativeScroll.ScrollY + (int)(viewportY - nativeScroll.Height * 0.33);
                        targetScrollY = Math.Max(0, targetScrollY);
                        if (Math.Abs(nativeScroll.ScrollY - targetScrollY) > 2)
                        {
                            nativeScroll.SmoothScrollTo(0, targetScrollY);
                        }
                        return;
                    }
#else
                    // 非Android平台使用MAUI ScrollToAsync
                    var targetY = label.Y - LandscapeLyricsScroll.Height * 0.33;
                    if (Math.Abs(LandscapeLyricsScroll.ScrollY - targetY) > 2)
                    {
                        await LandscapeLyricsScroll.ScrollToAsync(0, Math.Max(0, targetY), true);
                    }
                    return;
#endif
                }
                await Task.Delay(200);
            }
        }
        catch { }
    }

    /// <summary>获取 ScrollView 当前垂直滚动偏移（跨平台兼容）</summary>
    private static double GetScrollViewVerticalOffset(ScrollView sv)
    {
        try
        {
#if ANDROID
            if (sv.Handler?.PlatformView is Android.Widget.ScrollView nsv)
                return nsv.ScrollY;
#elif WINDOWS
            if (sv.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.ScrollViewer nsv)
                return nsv.VerticalOffset;
#endif
        }
        catch { }
        return 0;
    }

    /// <summary>获取元素相对于 LandscapeLyricsScroll 内容顶部的 Y 坐标。
    /// 遍历父容器累加 Y，包括 LandscapeLyricStack 自身的 Y（ScrollView 内内容的偏移）。</summary>
    private double GetRelativeYLandscape(VisualElement element)
    {
        double y = element.Y + element.Height / 2;
        var parent = element.Parent as VisualElement;
        while (parent != null)
        {
            y += parent.Y;
            if (parent == LandscapeLyricStack)
                break;
            parent = parent.Parent as VisualElement;
        }
        return y;
    }

    /// <summary>点击歌曲详情入口：跳转到歌曲详情页</summary>
}
