using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

using CatClawMusic.Maui.Controls;

namespace CatClawMusic.Maui.Pages;

/// <summary>正在播放页 —— 歌词引擎 partial 文件。</summary>
public partial class NowPlayingPage
{
    private void BuildLyricViews()
    {
        LyricStack.Children.Clear();
        _lyricLabels.Clear();
        _lyricBorders.Clear();
        _lyricRowViews.Clear();
        _lastHighlightIndex = -1;
        _lyricRowTops = Array.Empty<double>();

        var lines = _viewModel.AllLyricLines;
        if (lines == null || lines.Count == 0)
            return;

        // 正确原理：只给当前行按倍率缩宽度，非当前行满宽不拆短行。
        // - 非当前行：host.Width（满屏宽，短行不拆；长行按正常屏宽分行）
        // - 当前行：屏宽 / 1.5 → 放大 1.5x 后视觉 = 满屏宽，自动分行生效，末尾不切
        const double labelFontSize = 14;
        var align = _settings.ToLayoutOptions().Alignment;
        var hostMargin = new Thickness(0); // host 永远满屏宽
        // 构建期默认都是非当前行，所以用满屏宽公式（不拆短行）。成为当前行时由 ApplyLabelWidthRole 动态改。
        double WrappedLabelWidth(double parentW)
            => parentW > 0 ? Math.Max(40, parentW - 1) : -1;

        foreach (var line in lines)
        {
            var label = new KaraokeLabel
            {
                Text = line.Text,
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
                Padding = new Thickness(4, 4, 4, 4) // 左右各 +4：兜住 StrokeWidth=1.5 的外描边不被 Grid Clip 裁掉
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
                // 必须 Fill：让内部 ContentView 拿到父宽度约束 → Label 再按倍率收缩 WidthRequest
                HorizontalOptions = LayoutOptions.Fill
            };
            var host = new ContentView { Content = label, HorizontalOptions = LayoutOptions.Fill, Margin = hostMargin };
            host.LayoutChanged += (s, _) =>
            {
                if (s is View v && v.Width > 0)
                    label.WidthRequest = WrappedLabelWidth(v.Width);
            };
            border.Content = host;

            if (!string.IsNullOrEmpty(line.Translation) && _settings.ShowTranslation)
            {
                var stack = new VerticalStackLayout { Spacing = 10, HorizontalOptions = LayoutOptions.Fill };
                stack.Children.Add(border);

                // 罗马音行（网易云 romalrc 流）：显示在原文与译文之间，不参与主行高亮/测量
                if (!string.IsNullOrEmpty(line.Roma) && _settings.ShowRoma)
                    stack.Children.Add(BuildSubLyricRow(line.Roma, 11, align, hostMargin));

                var transLabel = new KaraokeLabel
                {
                    Text = line.Translation,
                    FontSize = 11,
                    FontFamily = "OpenSansSemibold",
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.White,
                    OutlineColor = Color.FromRgba(1f, 1f, 1f, 0.5f),
                    StrokeWidth = 1.5,
                    FillProgress = 0,
                    HorizontalTextAlignment = _settings.ToTextAlignment(),
                    HorizontalOptions = _settings.ToLayoutOptions(),
                    LineBreakMode = LineBreakMode.WordWrap,
                    Padding = new Thickness(4, 4, 4, 4) // 译文左右描边也留空间
                };
                transLabel.AnchorX = align == LayoutAlignment.Center ? 0.5 : (align == LayoutAlignment.End ? 1.0 : 0.0);
                transLabel.AnchorY = 0.5;
                // 用与主歌词相同结构的 Border + ContentView 包裹，确保分行宽度一致
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
                stack.Children.Add(transBorder);
                LyricStack.Children.Add(stack);
                _lyricRowViews.Add(stack);
            }
            else if (!string.IsNullOrEmpty(line.Roma) && _settings.ShowRoma)
            {
                // 无译文但有罗马音：罗马音并入副行结构
                var stack = new VerticalStackLayout { Spacing = 10, HorizontalOptions = LayoutOptions.Fill };
                stack.Children.Add(border);
                stack.Children.Add(BuildSubLyricRow(line.Roma, 11, align, hostMargin));
                LyricStack.Children.Add(stack);
                _lyricRowViews.Add(stack);
            }
            else
            {
                LyricStack.Children.Add(border);
                _lyricRowViews.Add(border);
            }

            _lyricLabels.Add(label);
            _lyricBorders.Add(border);
        }

        if (_lyricLabels.Count > 0)
        {
            var idx = _viewModel.CurrentLyricIndexObservable >= 0 ? _viewModel.CurrentLyricIndexObservable : 0;
            HighlightLineWithoutScroll(idx);

            // 安卓端 KaraokePlatformView.OnSizeChanged 会在首次布局后触发二次重测（_realWidth 确定），
            // 因此行高可能晚于 720ms 才稳定；SizeChanged 触发时重启一轮测量可覆盖此类场景。
            LyricClip.SizeChanged -= OnLyricClipSizeChanged;
            LyricClip.SizeChanged += OnLyricClipSizeChanged;

            // 布局完成后实测行高 + 钉当前行（首行也恒钉 1/3 处，避免开播时突然跳动）
            _lyricMeasureRetries = 0;
            _ = Task.Delay(60).ContinueWith(_ => MainThread.BeginInvokeOnMainThread(() =>
            {
                if (LyricClip.Handler == null) return;
                MeasureLyricRows();
                ScrollToLine(idx);
            }));
        }
    }

    private void OnLyricClipSizeChanged(object? sender, EventArgs e)
    {
        if (_isLandscape || _lyricRowViews.Count == 0) return;
        _lyricMeasureRetries = 0;
        MeasureLyricRows();
        var idx = _viewModel.CurrentLyricIndexObservable >= 0 ? _viewModel.CurrentLyricIndexObservable : 0;
        ScrollToLine(idx);
    }

    /// <summary>构建副歌词行（罗马音/译文，与主歌词同款 Border+ContentView 结构，分行宽度一致）。
    /// 不加入任何高亮/测量列表——副行跟随行容器显示，避免打乱主歌词行索引。</summary>
    private View BuildSubLyricRow(string text, double fontSize, LayoutAlignment align, Thickness hostMargin)
    {
        var subLabel = new KaraokeLabel
        {
            Text = text,
            FontSize = fontSize,
            FontFamily = "OpenSansSemibold",
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            OutlineColor = Color.FromRgba(1f, 1f, 1f, 0.5f),
            StrokeWidth = 1.5,
            FillProgress = 0,
            HorizontalTextAlignment = _settings.ToTextAlignment(),
            HorizontalOptions = _settings.ToLayoutOptions(),
            LineBreakMode = LineBreakMode.WordWrap,
            Padding = new Thickness(4, 4, 4, 4)
        };
        subLabel.AnchorX = align == LayoutAlignment.Center ? 0.5 : (align == LayoutAlignment.End ? 1.0 : 0.0);
        subLabel.AnchorY = 0.5;
        var subBorder = new Border
        {
            StrokeThickness = 0,
            BackgroundColor = Colors.Transparent,
            Padding = new Thickness(0),
            HorizontalOptions = LayoutOptions.Fill
        };
        var subHost = new ContentView { Content = subLabel, HorizontalOptions = LayoutOptions.Fill, Margin = hostMargin };
        subHost.LayoutChanged += (s, _) =>
        {
            if (s is View v && v.Width > 0)
                subLabel.WidthRequest = Math.Max(40, v.Width - 1);
        };
        subBorder.Content = subHost;
        return subBorder;
    }

    /// <summary>
    /// 实测各行顶部 Y（由实测行高累加）与裁剪区高度，供"当前行恒钉 1/3 处"的锚点计算。
    /// 关键约束：所有行的高度必须都 > 0.5（真实测量完成）才写 tops 表，
    /// 否则任何回退猜测都会随累积产生"越滚越偏"的残差。
    /// 未就绪时按 150ms 间隔重试，最多 20 次（3 秒窗口覆盖安卓 OnSizeChanged 二次布局）。
    /// </summary>
    private void MeasureLyricRows()
    {
        if (_isLandscape) return;
        if (_lyricRowViews.Count == 0) return;

        double clipH = LyricClip.Bounds.Height;
        if (clipH <= 0) clipH = _lyricClipHeight;

        double spacing = LyricStack.Spacing;
        var startY = LyricStack.Padding.Top;

        // 前置：若保留了上一次的合法锚点表且布局未变，直接跳过（避免不必要的重试）
        if (_lastMeasuredTops.Length == _lyricRowViews.Count)
        {
            double yy = startY;
            bool stable = true;
            for (int i = 0; i < _lyricRowViews.Count; i++)
            {
                var h = _lyricRowViews[i].Height;
                if (h <= 0.5) { stable = false; break; }
                if (Math.Abs(_lastMeasuredTops[i] - yy) > 0.5) { stable = false; break; }
                yy += h + spacing;
            }
            if (stable) { _lyricClipHeight = clipH; return; }
        }

        // 预扫：必须所有行高度都已就绪才提交整表，只要一行 <0.5 就延后，
        // 绝对不写入回退值（之前的 40dp 回退值会把后面所有行的锚点带入累积残差，越滚越偏）。
        bool allReady = true;
        for (int i = 0; i < _lyricRowViews.Count; i++)
        {
            if (_lyricRowViews[i].Height <= 0.5) { allReady = false; break; }
        }
        if (!allReady)
        {
            if (_lyricMeasureRetries < 20)
            {
                _lyricMeasureRetries++;
                _ = Task.Delay(150).ContinueWith(_ => MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (LyricClip.Handler == null) return;
                    MeasureLyricRows();
                    var idx = _viewModel.CurrentLyricIndexObservable >= 0 ? _viewModel.CurrentLyricIndexObservable : 0;
                    if (idx < _lyricRowViews.Count && _lyricRowTops.Length == _lyricRowViews.Count)
                        PinLineNow(idx);
                }));
            }
            return;
        }

        _lyricClipHeight = clipH;
        var tops = new double[_lyricRowViews.Count];
        double y = startY;
        for (int i = 0; i < _lyricRowViews.Count; i++)
        {
            tops[i] = y;
            y += _lyricRowViews[i].Height + spacing;
        }
        _lyricRowTops = tops;
        _lastMeasuredTops = (double[])tops.Clone();
    }

    // 歌词行视觉层次（复刻 Windows）：
    // Scale 渲染变换放大（不参与布局测量 → 行高恒定 → 滚动锚点稳定，不会像改 FontSize 那样跳动），
    // 行距呼吸用行容器 TranslationY 渲染平移撑开当前行上下缝隙（独立于整体滚动的 LyricStack.TranslationY）。

    /// <summary>
    /// 自检当前锚点表是否与真实布局一致：
    /// - 锚点表数量与行数一致
    /// - 所有行高度 > 0.5
    /// - 逐行累加结果与 _lyricRowTops 误差在 1dp 内
    /// 锁屏解锁 / 从后台回前台时，Handler/PlatformView 可能重建导致行高变化，
    /// 旧锚点表依然被 ScrollToLine 引用 → 越滚越偏。调用前校验保证定位基准可靠。
    /// </summary>
    private bool ValidateLyricTops()
    {
        if (_lyricRowTops.Length != _lyricRowViews.Count) return false;
        var spacing = LyricStack.Spacing;
        double y = LyricStack.Padding.Top;
        for (int i = 0; i < _lyricRowViews.Count; i++)
        {
            var h = _lyricRowViews[i].Height;
            if (h <= 0.5) return false;
            if (Math.Abs(_lyricRowTops[i] - y) > 1.0) return false;
            y += h + spacing;
        }
        return true;
    }

    /// <summary>锁屏解锁/回前台的"强制钉线"操作：重启测量 + 取消动画后直接跳目标位。</summary>
    private void ForcePinCurrentLine()
    {
        if (_isLandscape || _lyricRowViews.Count == 0) return;
        var idx = Math.Max(0, _viewModel.CurrentLyricIndexObservable);
        if (idx >= _lyricRowViews.Count) return;

        // 重启一轮测量（清空锚点表 → 下次 ScrollToLine 若不满足就先重测）
        _lyricMeasureRetries = 0;
        _lastMeasuredTops = Array.Empty<double>();
        MeasureLyricRows();

        if (_lyricRowTops.Length != _lyricRowViews.Count)
        {
            // 测量尚未就绪，稍后再试 3 次（最多 900ms）
            ScheduleRetriedPin(3);
            return;
        }

        PinLineNow(idx);
    }

    /// <summary>
    /// 计算目标行应钉住的平移量 TranslationY（在 LyricStack 内部坐标）。
    /// 常规：当前行中心钉裁剪区 33% 处；但当前行放大（Scale 1.5x）后视觉高度变大，
    /// 若按 33% 钉会把超出行顶出裁剪区被剪裁 → 用"视觉高度"做上下夹紧：
    /// - 放得下：中心在 [minC, maxC] 区间内贴近 33% 处（保证整行完整可见）
    /// - 行太高放不下（视觉高 ≥ 裁剪区）：顶格显示，至少头部完整可见
    /// </summary>
    private double ComputePinnedTargetY(int index, double clipH)
    {
        var rowH = _lyricRowViews[index].Height > 0 ? _lyricRowViews[index].Height : 40;
        double visualH = rowH * LyricCurrentScale;            // 当前行放大后的视觉高度
        double topMargin = LyricStack.Padding.Top + 4;        // 顶部/底部安全边距
        double minC = topMargin + visualH / 2.0;              // 顶部不溢出的最小中心
        double maxC = clipH - topMargin - visualH / 2.0;      // 底部不溢出的最大中心
        double center = clipH * 0.33;                         // 理想：1/3 处
        if (minC <= maxC)
            center = Math.Clamp(center, minC, maxC);          // 放得下 → 夹紧保证完整显示
        else
            center = minC;                                    // 超高 → 顶格，头部优先可见
        return center - (_lyricRowTops[index] + rowH / 2.0);
    }

    /// <summary>取消动画，立即把目标行钉在 1/3 处（不走缓动，避免前台视觉上还在滑）。</summary>
    private void PinLineNow(int index)
    {
        if (index < 0 || index >= _lyricRowViews.Count) return;
        if (_lyricRowTops.Length != _lyricRowViews.Count) return;
        try
        {
            _lyricClipHeight = LyricClip.Bounds.Height;
            if (_lyricClipHeight <= 0) return;
            double targetY = ComputePinnedTargetY(index, _lyricClipHeight);
            LyricStack.CancelAnimations();
            // 直接赋值 TranslationY，避免 380ms 缓动的视觉滑动
            LyricStack.TranslationY = targetY;
        }
        catch { }
    }

    private void ScheduleRetriedPin(int remaining)
    {
        if (remaining <= 0) return;
        _ = Task.Delay(300).ContinueWith(_ => MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_isLandscape || _lyricRowViews.Count == 0) return;
            // 先自检一次，失效就重测
            if (!ValidateLyricTops())
            {
                _lyricMeasureRetries = 0;
                MeasureLyricRows();
            }
            var idx = Math.Max(0, _viewModel.CurrentLyricIndexObservable);
            if (_lyricRowTops.Length == _lyricRowViews.Count && idx < _lyricRowViews.Count)
                PinLineNow(idx);
            else
                ScheduleRetriedPin(remaining - 1);
        }));
    }
    // ⚠ 所有行必须统一 FontSize：平移滚动的锚点表依赖行高恒定，任何动态字号切换都会导致
    // 锚点失效 → 当前行越滚越偏（已踩坑）。当前行的强调完全交给 Scale。
    private const double LyricCurrentScale = 1.25;    // 当前行放大倍率（末尾 3 字安全宽度机制兜底，放大也不会溢出）
    private const double LyricGapExtra = 6;          // 当前行上下额外呼吸空间（px）
    private const uint LyricAnimMs = 380;            // 与滚动 tween 同步的缓动时长

    /// <summary>动态行高：给当前行的**容器**加真实高度（Scale 是渲染变换，视觉高 = labelH×1.5，
    /// 容器高度不变就会上下溢出被裁）。三件套：
    /// 1) 容器 HeightRequest = 原高 + 主歌词label高×(LyricCurrentScale−1) —— 补上放大溢出的高度；
    /// 2) 主歌词 Border 垂直 Fill —— 吸收新增空间，自身长到 1.5×labelH；
    /// 3) 主歌词 label 垂直居中 —— 放大内容以容器中心对称，上下都完整贴合不溢出。
    /// 邻居行 ±1 只留小呼吸 Margin；非当前行恢复自动高度 + 顶部对齐。
    /// 性能：直接赋值目标值，不再对 HeightRequest/Margin 做逐帧动画——Margin/HeightRequest
    /// 是布局属性，旧版 380ms 内每 16ms 改一次会让整棵 VerticalStackLayout 持续重测
    /// （切句卡顿的主因之一）；绝大多数行因 0.5 阈值跳过，只有新旧当前行/邻居实际写入。</summary>
    private void ApplyLyricRowGap(int index, bool animate)
    {
        for (int i = 0; i < _lyricRowViews.Count; i++)
        {
            var row = _lyricRowViews[i];
            // 定位主歌词 Border（无译文：row 就是 Border；有译文：row 是 VStack，第 0 个是主歌词 Border）
            var mainBorder = row as Border
                             ?? (row as VerticalStackLayout)?.Children.FirstOrDefault() as Border;

            if (i == index && mainBorder != null)
            {
                // 主歌词 label 未放大布局高度（fallback 字号）
                double mainLblH = 0;
                if (mainBorder.Content is ContentView host && host.Content is KaraokeLabel lbl)
                    mainLblH = lbl.Height > 0 ? lbl.Height : 14;
                if (mainLblH <= 0) mainLblH = 14;

                // ⚠ 容器高直接 = label真实高 × 倍率 + 呼吸，不依赖 oldRowH：
                // label 重测前 oldRowH 还是未 wrap 时的旧行高，换行多行时容器高度不足 → 第二行被裁。
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

    /// <summary>缓动把第 i 行主歌词缩放到目标倍率（Scale 是渲染变换，不影响行高与滚动锚点）。</summary>
    private void AnimateLyricRowScale(int i, double target)
    {
        if (i < 0 || i >= _lyricLabels.Count) return;
        var lbl = _lyricLabels[i];
        if (Math.Abs(lbl.Scale - target) < 0.01) return;

        lbl.AbortAnimation("ScaleTo");
        _ = lbl.ScaleTo(target, LyricAnimMs, Easing.CubicInOut);
    }

    /// <summary>切换某标签的自动分行宽度：
    /// - 非当前行：host.Width（满屏宽，短歌词不强行分行，长歌词按正常屏宽分）
    /// - 当前行：host.Width / LyricCurrentScale（按倍率缩宽度，放大 1.5x 后视觉 = 满屏宽，自动分行不切字）</summary>
    private void ApplyLabelWidthRole(KaraokeLabel lbl, bool isActive)
    {
        if (lbl == null) return;
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

    private void HighlightLineWithoutScroll(int index)
    {
        if (index < 0 || index >= _lyricLabels.Count) return;

        for (int i = 0; i < _lyricLabels.Count; i++)
        {
            var lbl = _lyricLabels[i];

            if (i == index)
            {
                // 当前行：实心填充，进度由 ViewModel 逐字计算（逐行模式为 1.0）
                lbl.FillProgress = _viewModel.CurrentLineFillProgress;
                lbl.Scale = LyricCurrentScale;
            }
            else
            {
                // 非当前行：浅色实心，统一字号和不透明度
                lbl.FillProgress = 0;
                lbl.Opacity = 0.35;
                lbl.Scale = 1.0;
            }
            ApplyLabelWidthRole(lbl, i == index); // 当前行：满屏/倍率宽；非当前行：窄提前换行
        }

        ApplyLyricRowGap(index, animate: false);
        _lastHighlightIndex = index;

        // ⚠ 当前行变窄（WidthRequest 从满宽→屏宽/1.5）导致 StaticLayout 多分一行、行高变大。
        // 紧接着的 PinLineNow 仍用旧锚点表 tops + 旧 rowH，计算的位置比实际偏高
        // → 布局完成后（下一帧）强制重测锚点 + 再钉一次，保证不顶出裁剪区。
        // 构建/初始化场景用 animate=false（立即钉行，不缓动）。
        RelayoutAndRepinAfterHighlightChange(index, animate: false);
    }

    /// <summary>高亮切换导致行高/宽度变化：下一帧强制重排 + 重测锚点 + 重新定位。
    /// <paramref name="animate"/>=true（切句）时用 <see cref="ScrollToLine"/> 缓动到新位置，
    /// 保持滚动的平滑过渡（与 FullLyricsPage 一致）；=false（构建/初始化/恢复）时用
    /// <see cref="PinLineNow"/> 立即钉行。
    /// 性能：同帧内连续切行合并为一次两阶段重排（posted 标志 + 最新 index 胜出）——
    /// 旧版每次切行都叠加一层 Dispatch→Invalidate→Dispatch 链，短句歌曲一秒多次切行
    /// 会在主线程排满整树重测。</summary>
    private bool _relayoutPosted;
    private int _pendingRelayoutIndex = -1;
    private bool _pendingRelayoutAnimate;

    private void RelayoutAndRepinAfterHighlightChange(int index, bool animate = true)
    {
        // 1) 先行高扩展：把动态 Margin 设好（必须先设，后面 MeasureLyricRows 才能读到正确行高）
        ApplyLyricRowGap(index, animate: false);
        LyricStack.InvalidateMeasure(); // 强制 MAUI 重新测量+布局（让行高更新到扩展后的真实高度）

        _pendingRelayoutIndex = index;
        _pendingRelayoutAnimate = animate;
        if (_relayoutPosted) return;
        _relayoutPosted = true;

        Dispatcher.Dispatch(() =>
        {
            _relayoutPosted = false;
            var idx = _pendingRelayoutIndex;
            var anim = _pendingRelayoutAnimate;
            if (LyricClip.Handler == null) return;
            // 2) label 已按新宽度（屏宽/倍率）重新测量 → mainLblH 才是真实多行高度，
            //    此时再应用一次行高，容器 HeightRequest 才够，避免换行第二行被裁
            ApplyLyricRowGap(idx, animate: false);
            LyricStack.InvalidateMeasure();
            Dispatcher.Dispatch(() =>
            {
                if (LyricClip.Handler == null) return;
                // 3) 清空锚点快照，强制 MeasureLyricRows 不做 stable 快速跳过
                _lastMeasuredTops = Array.Empty<double>();
                _lyricMeasureRetries = 0;
                MeasureLyricRows();
                if (_lyricRowTops.Length == _lyricRowViews.Count && idx >= 0 && idx < _lyricRowViews.Count)
                {
                    // 4) 以新锚点 + 新行高重新定位：切句时缓动（保持平滑），构建/恢复时立即钉
                    if (anim)
                        ScrollToLine(idx);
                    else
                        PinLineNow(idx);
                }
            });
        });
    }

    private void HighlightLine(int index)
    {
        if (index < 0 || index >= _lyricLabels.Count) return;

        // 每次高亮当前行之前：自检锚点表是否与真实行高匹配。
        // 锁屏解锁、前后台切换时，Activity/Handler 可能重建，
        // 旧锚点表依然被 ScrollToLine 使用 → 当前行越滚越偏。
        // 这里先校验一次，保证基准是最新实测行高。
        if (_lyricRowTops.Length != _lyricRowViews.Count || !ValidateLyricTops())
        {
            _lyricMeasureRetries = 0;
            _lastMeasuredTops = Array.Empty<double>();
            MeasureLyricRows();
        }

        var affectedMin = Math.Max(0, Math.Min(index, _lastHighlightIndex) - 4);
        var affectedMax = Math.Min(_lyricLabels.Count - 1, Math.Max(index, _lastHighlightIndex) + 4);
        var prev = _lastHighlightIndex;

        for (int i = affectedMin; i <= affectedMax; i++)
        {
            var lbl = _lyricLabels[i];

            if (i == index)
            {
                lbl.FillProgress = _viewModel.CurrentLineFillProgress;
                lbl.Opacity = 1.0;
            }
            else
            {
                // 非当前行：浅色实心，统一字号和不透明度
                lbl.FillProgress = 0;
                lbl.Opacity = 0.35;
            }

            // Scale 走缓动动画（跳过当前/旧行，交给 AnimateLyricRowScale 处理）
            if (i != index && i != prev)
                lbl.Scale = i == index ? LyricCurrentScale : 1.0;

            ApplyLabelWidthRole(lbl, i == index); // 当前行：满屏/倍率宽（分行少）；非当前行：窄提前换行
        }

        // 新当前行缓缓放大，旧当前行缓缓缩回（与滚动同为 380ms CubicInOut）
        AnimateLyricRowScale(index, LyricCurrentScale);
        if (prev >= 0 && prev != index)
            AnimateLyricRowScale(prev, 1.0);

        // 当前行上下呼吸空间平滑迁移
        ApplyLyricRowGap(index, animate: true);

        _lastHighlightIndex = index;

        ScrollToLine(index);

        // 当前行变窄 → StaticLayout 多分一行，行高变大，上一步 ScrollToLine 用的还是旧锚点+旧rowH（位置偏上被切）。
        // 下一帧强制重排、重测锚点，再用 ScrollToLine 缓动到新位置（而非 PinLineNow 直接跳），
        // 保证位置正确的同时保持平滑过渡——与 FullLyricsPage / Windows 端行为一致。
        RelayoutAndRepinAfterHighlightChange(index);
    }

    /// <summary>
    /// 把歌词缓动钉到指定行：当前行中心**恒定**落在裁剪区 1/3 处（复刻 Windows 的滚动方案）。
    /// 滚动 = 整体平移 LyricStack.TranslationY（合成线程变换，不重排 → 丝滑无跳动），
    /// 380ms CubicInOut 缓动，等价于 BetterLyrics 的 ScrollOffset tween。
    /// 首尾不夹紧：第 1 句也钉在 1/3 处、最后一句仍停 1/3 处，位置永远一致。
    /// </summary>
    private void ScrollToLine(int index)
    {
        if (index < 0 || index >= _lyricRowViews.Count) return;
        if (_lyricRowTops.Length != _lyricRowViews.Count) return;

        try
        {
            _lyricClipHeight = LyricClip.Bounds.Height; // 实时读，兼容区域尺寸变化
            if (_lyricClipHeight <= 0) return;

            double targetY = ComputePinnedTargetY(index, _lyricClipHeight);

            LyricStack.CancelAnimations();
            LyricStack.TranslateTo(0, targetY, 380, Easing.CubicInOut);
        }
        catch { }
    }

#if WINDOWS
    /// <summary>在 WinUI 可视树中查找 ScrollViewer（用于 CollectionView 手动定位歌词行）</summary>
    private static Microsoft.UI.Xaml.Controls.ScrollViewer? FindScrollViewer(Microsoft.UI.Xaml.DependencyObject obj)
    {
        for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(obj); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(obj, i);
            if (child is Microsoft.UI.Xaml.Controls.ScrollViewer sv)
                return sv;
            var result = FindScrollViewer(child);
            if (result != null)
                return result;
        }
        return null;
    }
#endif

    /// <summary>当用户开始拖动进度条时触发，标记拖动状态并通知视图模型开始定位。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
}
