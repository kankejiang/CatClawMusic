using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.Services;

namespace CatClawMusic.Maui.Pages;

/// <summary>全屏歌词页 —— partial 分域文件。</summary>
public partial class FullLyricsPage
{
    /// <summary>非活动方向的歌词树是否过期（主题/歌词变更只重建当前方向后置位，
    /// 首次切到另一方向时按需重建，避免横竖两套树各自维护导致的双倍构建）</summary>
    private bool _inactiveTreeDirty;

    private void BuildLyricViews()
    {
        // 只构建当前方向的歌词树（旧版每次重建横竖两套完整视图树，
        // 100 行带译文/罗马音的歌词 ≈ 上千个视图对象直接翻倍）；
        // 另一方向在 ApplyLayoutForOrientation 首次切入时懒构建。
        if (_isLandscape)
        {
            BuildLandscapeLyricViews();
            _inactiveTreeDirty = true;
        }
        else
        {
            BuildPortraitLyricViews();
            _inactiveTreeDirty = true;
        }
    }

    /// <summary>构建竖屏歌词视图</summary>
    private void BuildPortraitLyricViews()
    {
        LyricStack.Children.Clear();
        _lyricLabels.Clear();
        _lyricTransLabels.Clear();
        _lyricBorders.Clear();
        _lyricRowViews.Clear();
        _lastHighlightIndex = -1;
        _lyricRowTops = Array.Empty<double>();

        var lines = _viewModel.AllLyricLines;
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
                HorizontalTextAlignment = _settings.ToTextAlignment(),
                HorizontalOptions = _settings.ToLayoutOptions()
            };
            LyricStack.Children.Add(label);
            return;
        }

        BuildLyricStack(LyricStack, lines, _lyricLabels, _lyricBorders, _lyricRowViews, _lyricTransLabels);

        var idx = _viewModel.CurrentLyricIndexObservable >= 0 ? _viewModel.CurrentLyricIndexObservable : 0;
        if (!_isLandscape)
            HighlightLineWithoutScroll(idx);

        // 安卓端二次布局（OnSizeChanged 确定 _realWidth）可能在 720ms 之后才改变行高，
        // SizeChanged 触发时重启一轮测量即可覆盖。
        LyricClip.SizeChanged -= OnPortraitLyricClipSizeChanged;
        LyricClip.SizeChanged += OnPortraitLyricClipSizeChanged;

        // 布局完成后实测行高 + 钉当前行（恒钉 1/3 处）
        _lyricMeasureRetries = 0;
        _ = Task.Delay(60).ContinueWith(_ => MainThread.BeginInvokeOnMainThread(() =>
        {
            if (LyricClip.Handler == null) return;
            MeasureLyricRows();
            ScrollToLine(idx);
        }));
    }

    private void OnPortraitLyricClipSizeChanged(object? sender, EventArgs e)
    {
        if (_isLandscape || _lyricRowViews.Count == 0) return;
        _lyricMeasureRetries = 0;
        MeasureLyricRows();
        var idx = _viewModel.CurrentLyricIndexObservable >= 0 ? _viewModel.CurrentLyricIndexObservable : 0;
        ScrollToLine(idx);
    }

    /// <summary>构建横屏歌词视图</summary>
    private void BuildLandscapeLyricViews()
    {
        LandscapeLyricStack.Children.Clear();
        _landscapeLyricLabels.Clear();
        _landscapeLyricTransLabels.Clear();
        _landscapeLyricBorders.Clear();
        _landscapeLyricRowViews.Clear();
        _landscapeLastHighlight = -1;
        _landscapeLyricRowTops = Array.Empty<double>();

        var lines = _viewModel.AllLyricLines;
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
                HorizontalTextAlignment = _settings.ToTextAlignment(),
                HorizontalOptions = _settings.ToLayoutOptions()
            };
            LandscapeLyricStack.Children.Add(label);
            return;
        }

        BuildLyricStack(LandscapeLyricStack, lines, _landscapeLyricLabels, _landscapeLyricBorders, _landscapeLyricRowViews, _landscapeLyricTransLabels);

        var idx = _viewModel.CurrentLyricIndexObservable >= 0 ? _viewModel.CurrentLyricIndexObservable : 0;
        if (_isLandscape)
            HighlightLineWithoutScroll(idx);

        // 横屏歌词区尺寸变化时重启一轮测量（覆盖安卓二次布局/横竖屏切换）
        LandscapeLyricClip.SizeChanged -= OnLandscapeLyricClipSizeChanged;
        LandscapeLyricClip.SizeChanged += OnLandscapeLyricClipSizeChanged;

        // 布局完成后实测行高 + 钉当前行
        _landscapeLyricMeasureRetries = 0;
        _ = Task.Delay(60).ContinueWith(_ => MainThread.BeginInvokeOnMainThread(() =>
        {
            if (LandscapeLyricClip.Handler == null) return;
            MeasureLyricRows();
            ScrollToLine(idx);
        }));
    }

    private void OnLandscapeLyricClipSizeChanged(object? sender, EventArgs e)
    {
        if (!_isLandscape || _landscapeLyricRowViews.Count == 0) return;
        _landscapeLyricMeasureRetries = 0;
        MeasureLyricRows();
        var idx = _viewModel.CurrentLyricIndexObservable >= 0 ? _viewModel.CurrentLyricIndexObservable : 0;
        ScrollToLine(idx);
    }

    /// <summary>通用歌词栈构建方法（字号在构建时按设置固定 → 行高恒定，滚动锚点稳定）。
    /// 正确原理：所有行（未唱/当前/已唱）统一按同一宽度提前分行 → 当前行自动分行、
    /// 放大 1.5x 后每一行视觉宽度 = 满屏宽，非当前行与当前行分行完全一致、不会因为变宽度跳动。
    /// - 外层 host 永远满屏宽 Margin=0（不剪裁不缩容器）
    /// - 所有 Label WidthRequest 统一 = 屏宽 / 倍率 - 6字安全宽度 → StaticLayout 在倒数第 4~6 个字提前断行
    /// - 当前行放大 1.5x：(屏宽/1.5 - 6字) × 1.5 = 屏宽 - 9字视觉宽 ＜ 屏宽 → 末尾不溢出，自动分行生效</summary>
    private void BuildLyricStack(VerticalStackLayout stack, IReadOnlyList<LrcLyricLine> lines,
        List<KaraokeLabel> labelList, List<Border> borderList, List<View> rowViews,
        List<KaraokeLabel> transLabelList)
    {
        var baseSize = _settings.FontSize;
        var transSize = Math.Max(10, baseSize - 2);
        var align = _settings.ToLayoutOptions().Alignment;
        // host 永远满屏宽
        var hostMargin = new Thickness(0);
        // 构建期默认都是非当前行，所以用满屏宽公式（不拆短行）。成为当前行时由 ApplyLabelWidthRole 动态改。
        double WrappedLabelWidth(double parentW)
            => parentW > 0 ? Math.Max(40, parentW - 1) : -1;

        foreach (var line in lines)
        {
            var label = new KaraokeLabel
            {
                Text = line.Text,
                FontSize = baseSize,
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
                Padding = new Thickness(4, 6, 4, 6) // 左右各 +4：兜住 StrokeWidth=2 的外描边不被 Grid Clip 裁掉
            };
            // 缩放锚点：左对齐从左边缘向右生长，居中从中心生长，右对齐从右边缘向左生长
            label.AnchorX = align == LayoutAlignment.Center ? 0.5 : (align == LayoutAlignment.End ? 1.0 : 0.0);
            label.AnchorY = 0.5;

            var border = new Border
            {
                // 透明容器不要圆角：StrokeShape 同时是裁剪形状，会裁掉放大后歌词的四角
                StrokeThickness = 0,
                BackgroundColor = Colors.Transparent,
                Padding = new Thickness(0),
                HorizontalOptions = LayoutOptions.Fill
            };
            var host = new ContentView { Content = label, HorizontalOptions = LayoutOptions.Fill, Margin = hostMargin };
            host.LayoutChanged += (s, _) =>
            {
                if (s is View v && v.Width > 0)
                    label.WidthRequest = WrappedLabelWidth(v.Width);
            };
            border.Content = host;

            var hasTranslation = !string.IsNullOrEmpty(line.Translation) && _settings.ShowTranslation;
            var hasRoma = !string.IsNullOrEmpty(line.Roma) && _settings.ShowRoma;

            if (hasTranslation || hasRoma)
            {
                var vStack = new VerticalStackLayout { Spacing = 10, HorizontalOptions = LayoutOptions.Fill };
                vStack.Children.Add(border);

                // 罗马音行（网易云 romalrc 流）：原文与译文之间，不加入 labelList（避免打乱行索引）
                if (hasRoma)
                {
                    var romaLabel = new KaraokeLabel
                    {
                        Text = line.Roma,
                        FontSize = transSize,
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
                        Padding = new Thickness(4, 6, 4, 6)
                    };
                    romaLabel.AnchorX = align == LayoutAlignment.Center ? 0.5 : (align == LayoutAlignment.End ? 1.0 : 0.0);
                    romaLabel.AnchorY = 0.5;
                    var romaBorder = new Border
                    {
                        StrokeThickness = 0,
                        BackgroundColor = Colors.Transparent,
                        Padding = new Thickness(0),
                        HorizontalOptions = LayoutOptions.Fill
                    };
                    var romaHost = new ContentView { Content = romaLabel, HorizontalOptions = LayoutOptions.Fill, Margin = hostMargin };
                    romaHost.LayoutChanged += (s, _) =>
                    {
                        if (s is View v && v.Width > 0)
                            romaLabel.WidthRequest = WrappedLabelWidth(v.Width);
                    };
                    romaBorder.Content = romaHost;
                    vStack.Children.Add(romaBorder);
                }

                if (hasTranslation)
                {
                    var transLabel = new KaraokeLabel
                    {
                        Text = line.Translation,
                        FontSize = transSize,
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
                        Padding = new Thickness(4, 6, 4, 6) // 译文左右描边也留空间
                    };
                    transLabel.AnchorX = align == LayoutAlignment.Center ? 0.5 : (align == LayoutAlignment.End ? 1.0 : 0.0);
                    transLabel.AnchorY = 0.5;
                    var transBorder = new Border
                    {
                        StrokeThickness = 0,
                        BackgroundColor = Colors.Transparent,
                        Padding = new Thickness(0),
                        HorizontalOptions = LayoutOptions.Fill
                    };
                    var transHost = new ContentView { Content = transLabel, HorizontalOptions = LayoutOptions.Fill, Margin = hostMargin };
                    transHost.LayoutChanged += (s, _) =>
                    {
                        if (s is View v && v.Width > 0)
                            transLabel.WidthRequest = WrappedLabelWidth(v.Width);
                    };
                    transBorder.Content = transHost;
                    vStack.Children.Add(transBorder);
                    transLabelList.Add(transLabel);
                }
                else
                {
                    transLabelList.Add(null); // 索引对齐，无译文行占位
                }

                stack.Children.Add(vStack);
                rowViews.Add(vStack);
            }
            else
            {
                stack.Children.Add(border);
                rowViews.Add(border);
                transLabelList.Add(null); // 索引对齐，无译文行占位
            }

            labelList.Add(label);
            borderList.Add(border);
        }
    }

    /// <summary>
    /// 按与当前行的距离设置行级高斯模糊（Android 12+ RenderEffect）：
    /// 当前行清晰（blur=0），距离 1 行轻微虚化 1dp，≥2 行 2dp（更远不再加深，避免糊成一团）。
    /// 低版本 Android 自动无操作，保持透明度分层兜底。
    /// </summary>
    private void ApplyRowBlur(KaraokeLabel lbl, int dist)
    {
#if ANDROID
        if (lbl.Handler?.PlatformView is CatClawMusic.Maui.Platforms.Android.KaraokePlatformView pv)
        {
            float r = dist <= 0 ? 0f : dist == 1 ? 1f : 2f;
            pv.SetRowBlur(r);
        }
#endif
    }

    /// <summary>切换某标签的自动分行宽度：
    /// - 非当前行：host.Width（满屏宽，短歌词不强行分行，长歌词按正常屏宽分）
    /// - 当前行：host.Width / LyricCurrentScale（按倍率缩宽度，放大 1.5x 后视觉 = 满屏宽，自动分行不切字）</summary>
    private void ApplyLabelWidthRole(KaraokeLabel lbl, bool isActive)
    {
        if (lbl == null) return;
        // 向上找外层 host ContentView（Border.Content → ContentView），拿父容器可用宽度
        if (lbl.Parent is not ContentView host || host.Width <= 0)
            host = lbl.Parent?.Parent as ContentView; // 兜底
        if (host == null || host.Width <= 0) return;

        double w;
        if (isActive)
            w = host.Width / LyricCurrentScale - 1;      // 当前行：屏宽/倍率 → 放大后视觉 = 满屏宽（自动分行）
        else
            w = host.Width - 1;                          // 非当前行：满屏宽，短行不拆
        lbl.WidthRequest = Math.Max(40, w);
    }

    /// <summary>仅高亮不滚动（构建期/初始化用）</summary>
    private void HighlightLineWithoutScroll(int index)
    {
        var labels = ActiveLyricLabels;
        if (index < 0 || index >= labels.Count) return;
        var transLabels = ActiveLyricTransLabels;

        for (int i = 0; i < labels.Count; i++)
        {
            var lbl = labels[i];
            if (i == index)
            {
                lbl.FillProgress = _viewModel.CurrentLineFillProgress;
                lbl.Opacity = 1.0;
                lbl.Scale = LyricCurrentScale;
            }
            else
            {
                lbl.FillProgress = 0;
                lbl.Opacity = 0.35;
                lbl.Scale = 1.0;
            }
            ApplyLabelWidthRole(lbl, i == index); // 当前行：满屏/倍率宽；非当前行：窄提前换行
            ApplyRowBlur(lbl, Math.Abs(i - index));
            if (transLabels.Count > i && transLabels[i] is KaraokeLabel tl)
                ApplyRowBlur(tl, Math.Abs(i - index)); // 译文与主歌词同步失焦
        }

        ApplyLyricRowGap(index, animate: false);
        ActiveLastHighlight = index;

        // ⚠ 当前行变窄（WidthRequest 从满宽→屏宽/1.5）导致 StaticLayout 多分一行、行高变大。
        // 紧接着的 ScrollToLine/PinActiveLine 仍用旧锚点表 tops + 旧 rowH，计算的位置比实际偏高
        // → 布局完成后（下一帧）强制重测锚点 + 再钉一次，保证不顶出裁剪区。
        // 构建/初始化场景用 animate=false（立即钉行，不缓动）。
        RelayoutAndRepinAfterHighlightChange(index, animate: false);
    }

    /// <summary>高亮切换导致行高/宽度变化：下一帧强制重排 + 重测锚点 + 重新定位。
    /// <paramref name="animate"/>=true（切句）时用 <see cref="ScrollToLine"/> 缓动到新位置，
    /// 保持滚动的平滑过渡；=false（构建/初始化/恢复）时用 <see cref="PinActiveLineNow"/> 立即钉行。
    /// 性能：同帧内连续切行合并为一次两阶段重排（posted 标志 + 最新 index 胜出），与 NowPlayingPage 一致。</summary>
    private bool _relayoutPosted;
    private int _pendingRelayoutIndex = -1;
    private bool _pendingRelayoutAnimate;

    private void RelayoutAndRepinAfterHighlightChange(int index, bool animate = true)
    {
        // 1) 先行高扩展：把动态 Margin 设好（必须先设，后面 MeasureLyricRows 才能读到正确行高）
        ApplyLyricRowGap(index, animate: false);
        var stack = ActiveLyricStack;
        stack.InvalidateMeasure(); // 强制 MAUI 重新测量+布局（让行高更新到扩展后的真实高度）

        _pendingRelayoutIndex = index;
        _pendingRelayoutAnimate = animate;
        if (_relayoutPosted) return;
        _relayoutPosted = true;

        Dispatcher.Dispatch(() =>
        {
            _relayoutPosted = false;
            var idx = _pendingRelayoutIndex;
            var anim = _pendingRelayoutAnimate;
            if (ActiveLyricClip.Handler == null) return;
            // 2) label 已按新宽度（屏宽/倍率）重新测量 → mainLblH 才是真实多行高度，
            //    此时再应用一次行高，容器 HeightRequest 才会算足，避免换行第二行被裁
            ApplyLyricRowGap(idx, animate: false);
            stack.InvalidateMeasure();
            Dispatcher.Dispatch(() =>
            {
                if (ActiveLyricClip.Handler == null) return;
                // 3) 清空锚点快照，强制 MeasureLyricRows 不做 stable 快速跳过
                if (_isLandscape) _lastLandscapeMeasuredTops = Array.Empty<double>();
                else _lastMeasuredTops = Array.Empty<double>();
                ref int retries = ref ActiveMeasureRetries;
                retries = 0;
                MeasureLyricRows();
                if (ActiveLyricRowTops.Length == ActiveLyricRowViews.Count && idx >= 0 && idx < ActiveLyricRowViews.Count)
                {
                    // 4) 以新锚点 + 新行高重新定位：切句时缓动（保持平滑），构建/恢复时立即钉
                    if (anim)
                        ScrollToLine(idx);
                    else
                        PinActiveLineNow(idx);
                }
            });
        });
    }

    /// <summary>高亮并滚动（当前行 Scale 放大 + 行距呼吸 + 整体平移钉 1/3 处）</summary>
    private void HighlightLine(int index)
    {
        var labels = ActiveLyricLabels;
        if (index < 0 || index >= labels.Count) return;

        // 每次高亮当前行之前：自检锚点表是否与真实行高匹配。
        // 锁屏解锁、前后台切换时，Activity/Handler 可能重建，
        // 旧锚点表依然被 ScrollToLine 使用 → 当前行越滚越偏。
        // 这里先校验一次，保证基准是最新实测行高。
        var rows = ActiveLyricRowViews;
        if (ActiveLyricRowTops.Length != rows.Count || !ValidateActiveTops())
        {
            ref int retries = ref ActiveMeasureRetries;
            retries = 0;
            if (_isLandscape) _lastLandscapeMeasuredTops = Array.Empty<double>();
            else _lastMeasuredTops = Array.Empty<double>();
            MeasureLyricRows();
        }

        ref int lastHl = ref ActiveLastHighlight;
        var affectedMin = Math.Max(0, Math.Min(index, lastHl) - 5);
        var affectedMax = Math.Min(labels.Count - 1, Math.Max(index, lastHl) + 5);
        var prev = lastHl;
        var transLabels = ActiveLyricTransLabels;

        for (int i = affectedMin; i <= affectedMax; i++)
        {
            var lbl = labels[i];
            if (i == index)
            {
                lbl.FillProgress = _viewModel.CurrentLineFillProgress;
                lbl.Opacity = 1.0;
            }
            else
            {
                lbl.FillProgress = 0;
                lbl.Opacity = 0.35;
            }

            // Scale 走缓动动画（跳过当前/旧行，交给 AnimateLyricRowScale 处理）
            if (i != index && i != prev)
                lbl.Scale = i == index ? LyricCurrentScale : 1.0;

            ApplyLabelWidthRole(lbl, i == index); // 当前行：满屏/倍率宽（分行少）；非当前行：窄提前换行
            ApplyRowBlur(lbl, Math.Abs(i - index));
            if (transLabels.Count > i && transLabels[i] is KaraokeLabel tl)
                ApplyRowBlur(tl, Math.Abs(i - index)); // 译文与主歌词同步失焦
        }

        AnimateLyricRowScale(index, LyricCurrentScale);
        if (prev >= 0 && prev != index)
            AnimateLyricRowScale(prev, 1.0);

        ApplyLyricRowGap(index, animate: true);

        lastHl = index;

        if (!_userScrolling)
            ScrollToLine(index);

        // 当前行变窄 → StaticLayout 多分一行，行高变大，上一步 ScrollToLine 用的还是旧锚点+旧rowH（位置偏上被切）。
        // 下一帧强制重排、重测锚点，再用 ScrollToLine 缓动到新位置（而非 PinActiveLineNow 直接跳），
        // 保证位置正确的同时保持平滑过渡——与 Windows 端 ScrollToWindowsLine 的 380ms 缓动行为一致。
        RelayoutAndRepinAfterHighlightChange(index);
    }

    /// <summary>动态行高：给当前行的**容器**加真实高度（Scale 是渲染变换，视觉高 = labelH×1.5，
    /// 容器高度不变就会上下溢出被裁）。三件套：
    /// 1) 容器 HeightRequest = 原高 + 主歌词label高×(LyricCurrentScale−1) —— 补上放大溢出的高度；
    /// 2) 主歌词 Border 垂直 Fill —— 吸收新增空间，自身长到 1.5×labelH；
    /// 3) 主歌词 label 垂直居中 —— 放大内容以容器中心对称，上下都完整贴合不溢出。
    /// 邻居行 ±1 只留小呼吸 Margin；非当前行恢复自动高度 + 顶部对齐。
    /// 性能：直接赋值目标值，不再对 HeightRequest/Margin 做逐帧布局属性动画（与 NowPlayingPage 一致）。</summary>
    private void ApplyLyricRowGap(int index, bool animate)
    {
        var rows = ActiveLyricRowViews;
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            // 定位主歌词 Border（无译文：row 就是 Border；有译文：row 是 VStack，第 0 个是主歌词 Border）
            var mainBorder = row as Border
                             ?? (row as VerticalStackLayout)?.Children.FirstOrDefault() as Border;

            if (i == index && mainBorder != null)
            {
                // 主歌词 label 未放大布局高度（fallback 字号）
                double mainLblH = 0;
                if (mainBorder.Content is ContentView host && host.Content is KaraokeLabel lbl)
                    mainLblH = lbl.Height > 0 ? lbl.Height : _settings.FontSize;
                if (mainLblH <= 0) mainLblH = _settings.FontSize;

                // ⚠ 容器高直接 = label真实高 × 倍率 + 呼吸，不能依赖 oldRowH（旧行高在 label 重测前
                // 还是未 wrap 时的单行值，会让换行成多行的当前行容器高度不足 → 第二行底部被裁）。
                double newRowH = mainLblH * (float)LyricCurrentScale + LyricGapExtra * 2;

                if (Math.Abs(row.HeightRequest - newRowH) > 0.5)
                    row.HeightRequest = newRowH;
                mainBorder.VerticalOptions = LayoutOptions.Fill;
                if (mainBorder.Content is ContentView hostC)
                {
                    hostC.VerticalOptions = LayoutOptions.Fill;
                    if (hostC.Content is KaraokeLabel lblC)
                        lblC.VerticalOptions = LayoutOptions.Center;
                }
            }
            else if (mainBorder != null)
            {
                // 非当前行：恢复自动高度 + 顶部对齐
                if (row.HeightRequest > 0) row.HeightRequest = -1;
                mainBorder.VerticalOptions = LayoutOptions.Start;
                if (mainBorder.Content is ContentView hostN)
                {
                    hostN.VerticalOptions = LayoutOptions.Start;
                    if (hostN.Content is KaraokeLabel lblN)
                        lblN.VerticalOptions = LayoutOptions.Start;
                }
            }

            // 邻居行小呼吸 Margin（直接赋值，无逐帧动画）
            double top = 0, bottom = 0;
            if (i == index - 1) bottom = LyricGapExtra;
            else if (i == index + 1) top = LyricGapExtra;

            if (Math.Abs(row.Margin.Top - top) < 0.5 &&
                Math.Abs(row.Margin.Bottom - bottom) < 0.5) continue;

            row.Margin = new Thickness(0, top, 0, bottom);
        }
    }

    /// <summary>缓动把第 i 行缩放到目标倍率（Scale 渲染变换，不影响行高与滚动锚点）。</summary>
    private void AnimateLyricRowScale(int i, double target)
    {
        var labels = ActiveLyricLabels;
        if (i < 0 || i >= labels.Count) return;
        var lbl = labels[i];
        if (Math.Abs(lbl.Scale - target) < 0.01) return;

        lbl.AbortAnimation("ScaleTo");
        _ = lbl.ScaleTo(target, LyricAnimMs, Easing.CubicInOut);
    }

    /// <summary>
    /// 实测各行顶部 Y（由实测行高累加）与裁剪区高度，供"当前行恒钉 1/3 处"锚点计算。
    /// 关键约束：所有行高度必须都 > 0.5 才写 tops 表，避免回退猜测造成累积残差 → 越滚越偏。
    /// 未就绪按 150ms 间隔重试，最多 20 次（3 秒覆盖安卓 OnSizeChanged 二次布局）。
    /// 新增：_lastMeasuredTops 快照 → 布局未变化时快速跳过，减少重复累加开销。
    /// </summary>
    private void MeasureLyricRows()
    {
        var rows = ActiveLyricRowViews;
        if (rows.Count == 0) return;

        var clip = ActiveLyricClip;
        double clipH = clip.Bounds.Height;
        if (clipH <= 0)
        {
            clipH = _isLandscape ? _landscapeLyricClipHeight : _lyricClipHeight;
            if (clipH <= 0) return;
        }

        var stack = ActiveLyricStack;
        double spacing = stack.Spacing;
        double startY = stack.Padding.Top;

        // 前置：若保留了上一次的合法锚点表且布局未变，直接跳过（避免不必要的重试）
        var lastTops = _isLandscape ? _lastLandscapeMeasuredTops : _lastMeasuredTops;
        if (lastTops.Length == rows.Count)
        {
            double yy = startY;
            bool stable = true;
            for (int i = 0; i < rows.Count; i++)
            {
                var h = rows[i].Height;
                if (h <= 0.5) { stable = false; break; }
                if (Math.Abs(lastTops[i] - yy) > 0.5) { stable = false; break; }
                yy += h + spacing;
            }
            if (stable)
            {
                if (_isLandscape) _landscapeLyricClipHeight = clipH;
                else _lyricClipHeight = clipH;
                return;
            }
        }

        // 预扫：整表全干净才提交
        bool allReady = true;
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].Height <= 0.5) { allReady = false; break; }
        }
        if (!allReady)
        {
            ref int retries = ref ActiveMeasureRetries;
            if (retries < 20)
            {
                retries++;
                _ = Task.Delay(150).ContinueWith(_ => MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (ActiveLyricClip.Handler == null) return;
                    MeasureLyricRows();
                    var idx = _viewModel.CurrentLyricIndexObservable >= 0 ? _viewModel.CurrentLyricIndexObservable : 0;
                    if (ActiveLyricRowTops.Length == rows.Count)
                        PinActiveLineNow(idx);
                }));
            }
            return;
        }

        if (_isLandscape)
            _landscapeLyricClipHeight = clipH;
        else
            _lyricClipHeight = clipH;

        var tops = new double[rows.Count];
        // 起点含 Stack 的 Top Padding，避免每行中心恒定偏下
        double y = startY;
        for (int i = 0; i < rows.Count; i++)
        {
            tops[i] = y;
            y += rows[i].Height + spacing;
        }

        if (_isLandscape)
        {
            _landscapeLyricRowTops = tops;
            _lastLandscapeMeasuredTops = (double[])tops.Clone();
        }
        else
        {
            _lyricRowTops = tops;
            _lastMeasuredTops = (double[])tops.Clone();
        }
    }

    /// <summary>
    /// 计算目标行应钉住的平移量 TranslationY（在 LyricStack 内部坐标）。
    /// 常规：当前行中心钉裁剪区 30% 处；但当前行放大（Scale 1.5x）后视觉高度变大，
    /// 若按 30% 钉会把超出行顶出裁剪区被剪裁 → 用"视觉高度"做上下夹紧：
    /// - 放得下：中心在 [minC, maxC] 区间内贴近 30% 处（保证整行完整可见）
    /// - 行太高放不下（视觉高 ≥ 裁剪区）：顶格显示，至少头部完整可见
    /// </summary>
    private double ComputePinnedTargetY(int index, double clipH)
    {
        var rows = ActiveLyricRowViews;
        var rowH = rows[index].Height > 0 ? rows[index].Height : 40;
        double visualH = rowH * LyricCurrentScale;            // 当前行放大后的视觉高度
        double topMargin = ActiveLyricStack.Padding.Top + 4;  // 顶部/底部安全边距
        double minC = topMargin + visualH / 2.0;              // 顶部不溢出的最小中心
        double maxC = clipH - topMargin - visualH / 2.0;      // 底部不溢出的最大中心
        double center = clipH * 0.30;                         // 理想：1/3 处
        if (minC <= maxC)
            center = Math.Clamp(center, minC, maxC);          // 放得下 → 夹紧保证完整显示
        else
            center = minC;                                    // 超高 → 顶格，头部优先可见
        return center - (ActiveLyricRowTops[index] + rowH / 2.0);
    }

    /// <summary>
    /// 把歌词缓动钉到指定行：当前行中心**恒定**落在裁剪区 1/3 处。
    /// 滚动 = 整体平移 LyricStack.TranslationY（合成线程变换，不重排），380ms CubicInOut，
    /// 首尾不夹紧 → 位置永远一致。用户拖动（_userScrolling）期间不自动滚动。
    /// </summary>
    private void ScrollToLine(int index)
    {
        var labels = ActiveLyricLabels;
        if (index < 0 || index >= labels.Count) return;

        var rows = ActiveLyricRowViews;
        if (ActiveLyricRowTops.Length != rows.Count) return;

        try
        {
            var clipH = ActiveLyricClipHeight;
            if (clipH <= 0) return;

            double targetY = ComputePinnedTargetY(index, clipH);

            ActiveLyricStack.CancelAnimations();
            ActiveLyricStack.TranslateTo(0, targetY, LyricAnimMs, Easing.CubicInOut);
        }
        catch { }
    }

    /// <summary>轻击歌词区域返回播放页</summary>
}
