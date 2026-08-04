#if WINDOWS
using System.Collections.Generic;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.Helpers;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.UI.Xaml.Media;
using DispatcherTimer = Microsoft.UI.Xaml.DispatcherTimer;
using WinScrollViewer = Microsoft.UI.Xaml.Controls.ScrollViewer;
using WinScrollViewerViewChangedEventArgs = Microsoft.UI.Xaml.Controls.ScrollViewerViewChangedEventArgs;
using WinListViewBase = Microsoft.UI.Xaml.Controls.ListViewBase;
using WinFrameworkElement = Microsoft.UI.Xaml.FrameworkElement;
using WinDependencyObject = Microsoft.UI.Xaml.DependencyObject;

namespace CatClawMusic.Maui.Pages;

/// <summary>
/// NowPlayingPage 的 Windows 桌面播放布局逻辑（基于 Aurora 原型）。
/// 仅编译进 Windows 目标，安卓端完全不受影响。
/// 负责亚克力标题栏 EQ 动画、3D 封面、歌词遮罩淡入与跟随、
/// 底部三栏控制坞（播放/进度/音量）等桌面专属交互。
/// </summary>
public partial class NowPlayingPage
{
    // Windows 歌词视图：自绘静态堆叠（不用 CollectionView/虚拟化，从根上消除"跳动"）。
    // 所有行一次性铺开到 WinLyricStack，滚动 = 整体平移它的 TranslationY（合成线程变换，不重排）。
    // 切句时把 TranslationY 用缓动动画 tween 到目标行（参考 BetterLyrics 的 ScrollOffset 机制）。
    private readonly List<WinLyricRow> _winRows = new();
    private double[] _winRowTops = Array.Empty<double>();   // 每行顶部在 stack 内的 Y
    private double _winRowHeight;       // 实测行高（行距恒定 → 锚点稳定）
    private double _winStackHeight;     // 整个 stack 高度
    private double _winClipHeight;      // 歌词裁剪区高度
    private int _winLastHighlight = -1;
    private bool _winFollow = true;
    private bool _winPanWired;
    private double _winPanStartY;
    private double _winLastScrollHeight;   // 兼容字段（已被静态堆叠实测高度替代，无读取方）
    private int _winMeasureRetries;        // 行高测量重试计数（布局未就绪时补偿）

    /// <summary>一行歌词的视图引用（代码构建，非绑定）。</summary>
    private sealed class WinLyricRow
    {
        public Grid Container = null!;
        public Label Main = null!;
        public Label Trans = null!;
        public Ellipse Dot = null!;
    }

    // Windows 封面尺寸缓存
    private int _winCoverSize;

    // Windows 播放状态（用于 EQ 动画与播放图标切换）
    private bool _winIsPlaying;

    // 音量与静音
    private double _winLastVolume = 1.0;
    private bool _winIsMuted;

    // EQ 频谱动画定时器（WinUI DispatcherTimer，已在 FrostedBackgroundHandler 中验证可用）
    private DispatcherTimer? _winEqTimer;
    private double _winEqTime;

    // ═══════════════════════════════════════
    // 布局接入
    // ═══════════════════════════════════════

    /// <summary>Windows 端布局：显示 WindowsStage，隐藏竖屏与横屏布局，并按窗口尺寸计算封面大小。</summary>
    private void ApplyWindowsLayout(double width, double height)
    {
        if (width <= 0 || height <= 0) return;

        // 首次进入：切换可见性
        if (!WindowsStage.IsVisible)
        {
            WindowsStage.IsVisible = true;
            MainContent.IsVisible = false;
            BottomControlsRoot.IsVisible = false;
            TopNavBar.IsVisible = false;
            PhoneControls.IsVisible = false;
            DesktopControls.IsVisible = false;
            BottomActionBar.IsVisible = false;
            LandscapeRoot.IsVisible = false;
            ApplyWindowsSafeArea();
            SetupWinDragArea();
        }

        // 封面尺寸：左栏宽 ≈ 0.42 * 舞台宽；可用高 = 窗口高 - 标题栏48 - 控制坞92 - 上下留白
        double stageWidth = width;
        double leftColWidth = stageWidth * 0.42;
        double availableHeight = height - 48 - 92 - 48;
        int coverSize = Math.Clamp((int)Math.Min(availableHeight, leftColWidth - 96), 240, 520);

        if (_winCoverSize != coverSize)
        {
            _winCoverSize = coverSize;
            WinCover.WidthRequest = coverSize;
            WinCover.HeightRequest = coverSize;
            WinCoverImage.WidthRequest = coverSize;
            WinCoverImage.HeightRequest = coverSize;
        }

        // CollectionView 模式下不需要 ScrollViewer 高度兜底（CollectionView 基于 ListViewBase，
        // 在 WinUI 3 后端 100% 撑满父空间，不会有 ScrollViewer.VP=0 的 bug）。
        // 保留 _winLastScrollHeight 字段以兼容旧代码。
        _winLastScrollHeight = Math.Max(0, availableHeight - 80);
    }

    /// <summary>Windows 安全区：标题栏顶部留出状态栏空间。</summary>
    private void ApplyWindowsSafeArea()
    {
        var topInset = SafeAreaHelper.TopInset;
        WindowsStage.Padding = new Thickness(0, topInset, 0, 0);
    }

    /// <summary>WindowsStage 就绪初始化：构建歌词、同步滑块、初始化音量与图标状态。</summary>
    private void OnWindowsStageReady()
    {
        ApplyWindowsSafeArea();

        // 播放图标
        _winIsPlaying = _viewModel.IsPlaying;
        UpdateWinPlayIcon();

        // 收藏图标
        UpdateWinLikeIcon();

        // 进度滑块
        if (_viewModel.Duration > 0)
            WinProgressSlider.Maximum = _viewModel.Duration;
        if (_viewModel.Progress > 0)
            WinProgressSlider.Value = _viewModel.Progress;

        // 音量：从音频服务读取当前值
        try
        {
            var v = _audioPlayer.Volume;
            _winLastVolume = v > 0 ? v : 1.0;
            _winIsMuted = v <= 0;
            WinVolumeSlider.Value = v;
            UpdateWinMuteIcon();
        }
        catch { }

        // 歌词构建（自绘静态堆叠 + 平移滚动）
        BuildWindowsLyricViews();
        ApplyWindowsLyricBackdrop();
        WireWinLyricPanGesture();

        // EQ 动画
        UpdateWinEqAnimation();
    }

    // ═══════════════════════════════════════
    // 歌词
    // ═══════════════════════════════════════

    // 歌词层次配色（参考设计稿：纯白当前 + 冷灰递进，避免品牌色干扰阅读）
    private static readonly Color WinLyricCurrentColor = Colors.White;                  // 当前行
    private static readonly Color WinLyricNearColor = Color.FromArgb("#A8AAB0");      // 偏亮的中性灰(参考图未唱档)
    // 已唱档的冷灰：2026-08-02 按用户要求已停用（已唱与未唱同色），保留常量以便回退。
    private static readonly Color WinLyricFarColor = Color.FromArgb("#5A5C66");

    // 滚动缓动时长（毫秒）：~380ms + CubicInOut，与 BetterLyrics 的 ScrollOffset tween 同量级，
    // 切句时整列平缓上移一格，丝滑无跳动。

    /// <summary>当前行主文本放大倍率（视觉上字号 +50%）。
    /// 用 Scale 渲染变换而非改 FontSize：Scale 不参与布局测量 → 行高恒定 → 滚动锚点稳定，
    /// 若直接改 FontSize 会让当前行变高、整列重排，滚动必然出现跳动。</summary>
    private const double WinLyricCurrentScale = 1.25;

    /// <summary>当前行放大/缩回的缓动时长，与滚动 tween 同步，视觉上"缓缓长大"。</summary>
    private const uint WinLyricScaleMs = 380;

    /// <summary>当前行上下额外的呼吸空间（px）。基础行距已减半到 6px，当前行因 Scale=1.5
    /// 需要更多空间，故让它上方所有行整体上移、下方所有行整体下移各 <c>WinLyricGapExtra</c>，
    /// 只把当前行上下这两条缝隙撑开（其余缝隙不变）。
    /// ⚠ 必须用 <c>TranslationY</c> 渲染平移而非 Padding：Padding 会改行高，破坏
    /// <c>_winRowTops</c> 的"所有行等高"前提，滚动锚点失效并重新引入跳动。</summary>
    private const double WinLyricGapExtra = 6;

    // ── 高斯模糊景深 ──────────────────────────────────────────
    // 层次不再只靠颜色/透明度表达，而是叠加"清晰度"：当前行全清晰，越远越模糊。
    // 好处正如需求所述——不必再纠结字号/字体差异，焦点天然落在当前行。

    /// <summary>相邻行（距离 1）的模糊半径（DP）。</summary>
    private const double WinLyricBlurNear = 3.5;

    /// <summary>距离 2 行的模糊半径。</summary>
    private const double WinLyricBlurMid = 6.5;

    /// <summary>距离 ≥3 行的模糊半径（最远档，不再继续加深，避免糊成一团色块）。</summary>
    private const double WinLyricBlurFar = 9.0;

    /// <summary>
    /// 模糊生效时是否隐藏"清晰原文"，只留模糊副本。
    ///
    /// 模糊层是一个叠在行容器**之上**的兄弟 SpriteVisual（见 LyricBlurPlatformEffect），
    /// 原本清晰的文字仍在其下方，笔画边缘会透出来形成重影 → 远行看着"糊不彻底"。
    /// 把行容器自身 Opacity 设为 0 即可只留模糊副本：
    /// <c>CompositionVisualSurface</c> 捕获的是 SourceVisual 的**子树**，
    /// SourceVisual 自身的 Offset/Opacity/Clip 不计入采样
    /// （现有实现需手动 <c>_sprite.Offset = visual.Offset</c> 正是此证据），
    /// 所以容器透明不会让模糊层一起消失。
    ///
    /// ⚠ 若某版本 WinUI 行为有别，表现为"非当前行整片空白"——把此开关改为 false 即可回退到
    /// 带轻微重影但一定可见的模式。
    /// </summary>
    /// <summary>
    /// 模糊生效时是否把行容器自身 Opacity 设为 0（仅留模糊副本）。
    /// .NET 10 MAUI 的 WinUI CompositionVisualSurface 在本环境会把 SourceVisual 自己的 Opacity 也算进采样，
    /// 即便 SetBlurAmount&lt;0.01 也会"略微欠采"，导致非当前行整片消失。关闭后回归"清晰原文 + 模糊副本叠加"：
    /// 笔画边缘有极轻微重影（模糊半径 ≤6.5 不明显），但所有行都可见。
    /// </summary>
    private const bool WinLyricBlurHideSource = false;

    /// <summary>构建 Windows 歌词：所有行一次性代码构建为 WinLyricStack 的子 Grid（自绘静态堆叠，非虚拟化）。</summary>
    private void BuildWindowsLyricViews()
    {
        _winRows.Clear();
        WinLyricStack.Children.Clear();
        WinLyricStack.TranslationY = 0;
        _winLastHighlight = -1;
        _winRowTops = Array.Empty<double>();

        var lines = _viewModel.AllLyricLines;
        if (lines == null || lines.Count == 0)
        {
            WinNoLyricsLabel.IsVisible = true;
            return;
        }
        WinNoLyricsLabel.IsVisible = false;

        var baseSize = _settings.FontSize;
        var transSize = Math.Max(10, baseSize - 4);

        foreach (var line in lines)
        {
            var row = new Grid
            {
                // 行距：上下各 3px → 行间 6px（原 7+7=14px，按需求减半）。
                // 当前行上下的呼吸空间不靠 Padding，而是靠 ApplyWinRowGap 用
                // Container.TranslationY 渲染平移撑开——Padding 若随当前行变化会改行高，
                // 破坏 _winRowTops 等高锚点表并重新引入跳动。
                Padding = new Thickness(0, 3, 0, 3),
                RowSpacing = 3,
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto },
                },
                RowDefinitions = new RowDefinitionCollection
                {
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = GridLength.Auto },
                },
            };

            var main = new Label
            {
                Text = line.Text,
                FontSize = baseSize,
                FontFamily = "OpenSansRegular",
                FontAttributes = FontAttributes.Bold,
                TextColor = WinLyricNearColor,
                LineBreakMode = LineBreakMode.WordWrap,
                HorizontalOptions = LayoutOptions.Start,
                VerticalOptions = LayoutOptions.Center,
                // 以左边缘为缩放锚点，放大时向右生长而不是向左溢出屏幕外
                AnchorX = 0,
                AnchorY = 0.5,
            };
            Grid.SetRow(main, 0); Grid.SetColumn(main, 0);

            // 译文常驻占位：无译文也占固定高度 → 所有行高一致 → 滚动锚点稳定
            var trans = new Label
            {
                Text = line.Translation ?? string.Empty,
                FontSize = transSize,
                FontFamily = "OpenSansRegular",
                TextColor = WinLyricNearColor,
                Opacity = 0.85,
                LineBreakMode = LineBreakMode.WordWrap,
                HorizontalOptions = LayoutOptions.Start,
                VerticalOptions = LayoutOptions.Center,
                HeightRequest = transSize * 1.4,
            };
            Grid.SetRow(trans, 1); Grid.SetColumn(trans, 0);

            var dot = new Ellipse
            {
                WidthRequest = 5, HeightRequest = 5,
                Fill = Color.FromArgb("#FF5A5A"),
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Start,
                Margin = new Thickness(0, 4, 18, 0),
                IsVisible = false,
            };
            dot.Shadow = new Microsoft.Maui.Controls.Shadow { Brush = Color.FromArgb("#FF5A5A"), Radius = 6f, Opacity = 0.9f };
            Grid.SetRow(dot, 0); Grid.SetColumn(dot, 1);

            row.Children.Add(main);
            row.Children.Add(trans);
            row.Children.Add(dot);

            // 整行（正文 + 译文）一起失焦：模糊挂在行容器上，而不是逐个 Label，
            // 这样译文与正文的模糊程度一致，不会出现"正文糊了译文还清晰"的割裂。
            row.Effects.Add(new CatClawMusic.Maui.Effects.LyricBlurEffect());

            WinLyricStack.Children.Add(row);
            _winRows.Add(new WinLyricRow { Container = row, Main = main, Trans = trans, Dot = dot });
        }

        var idx = _viewModel.CurrentLyricIndexObservable >= 0 ? _viewModel.CurrentLyricIndexObservable : 0;
        HighlightWindowsLineWithoutScroll(idx);

        // 布局完成后实测行高 + 各行顶 Y，再一次性钉到当前行。
        // 注意用 idx（未开播时 = 0）而不是 CurrentLyricIndexObservable：
        // 未播放时也要先把第 1 行钉到第 3 行位置，否则一开播会突然"向下跳一格"。
        _winRowHeight = 0;
        _winMeasureRetries = 0;
        _ = Task.Delay(60).ContinueWith(_ => MainThread.BeginInvokeOnMainThread(() =>
        {
            MeasureWinRows();
            if (_winFollow)
                ScrollToWindowsLine(_viewModel.CurrentLyricIndexObservable >= 0
                    ? _viewModel.CurrentLyricIndexObservable : 0, animate: false);
        }));

        WinLog($"Build done: sourceLines={lines.Count} rows={_winRows.Count} baseSize={baseSize}");
    }

    /// <summary>刷新全部行的层次样式，但不触发滚动（构建期/初始化用）。</summary>
    private void HighlightWindowsLineWithoutScroll(int index)
    {
        if (index < 0 || index >= _winRows.Count) return;
        for (int i = 0; i < _winRows.Count; i++)
            ApplyWinLyricTierInstant(i, index);
        ApplyWinRowGap(index, animate: false);
        _winLastHighlight = index;
    }

    /// <summary>
    /// 高亮指定行（颜色/透明度/红点分层），并缓动滚动到该行（当前行钉在离顶部 2 行处）。
    /// 滚动 = 整体平移 WinLyricStack.TranslationY（合成线程变换，不重排），故丝滑无跳动——
    /// 这正是 BetterLyrics 用 Canvas + ScrollOffset tween 达成的效果，这里用 MAUI 等价实现。
    /// 当前行主文本额外用 Scale 放大 50%（渲染变换，不改行高 → 不影响滚动锚点）。
    /// </summary>
    private void HighlightWindowsLine(int index)
    {
        if (index < 0 || index >= _winRows.Count) return;

        var prev = _winLastHighlight;
        for (int i = 0; i < _winRows.Count; i++)
            ApplyWinLyricTierInstant(i, index, setScale: i != index && i != prev);
        _winLastHighlight = index;

        // 新当前行缓缓长大到 1.5，旧当前行缓缓缩回 1.0（与滚动同步的 380ms CubicInOut）
        AnimateWinRowScale(index, WinLyricCurrentScale);
        if (prev >= 0 && prev != index)
            AnimateWinRowScale(prev, 1.0);

        // 当前行上下呼吸空间随之平滑迁移（同为 380ms，与放大/滚动三者同步）
        ApplyWinRowGap(index, animate: true);

        ScrollToWindowsLine(index, animate: true);
        WinLog($"Highlight idx={index}/{_winRows.Count} follow={_winFollow}");
    }

    /// <summary>
    /// 按与当前行的"方向 + 距离"返回层次（颜色 + 透明度）。参考图风格：所有行**同字号**，
    /// 层次全靠颜色/红点 + 整体毛玻璃背景表达（见 OnWindowsStageReady 中给 WinLyricClip
    /// 套的 AcrylicBrush）。"清晰度"差异由系统级毛玻璃统一提供，每行不单独做模糊。
    /// - 当前行：白、不透明、红点。
    /// - 其余行（已唱 / 未唱一视同仁）：中性灰，仅按**距离**递减透明度，越远越暗。
    ///   2026-08-02 按用户要求取消了已唱行的冷灰暗档，两侧完全对称。
    /// </summary>
    private static (Color Color, double Opacity) GetWinLyricTier(int i, int index)
    {
        if (i == index)
            return (WinLyricCurrentColor, 1.0);

        var d = Math.Abs(i - index);

        // 已唱与未唱**同色同透明度**（用户要求）：非当前行不再区分过去/未来，
        // 焦点完全交给当前行的白色 + Scale 1.5 + 红点，两侧对称衬托。
        return (WinLyricNearColor, d == 1 ? 0.80 : 0.65);
    }

    /// <summary>把第 i 行落到它应有的层次（颜色/透明度/红点/缩放）。不触发布局、不做动画。
    /// <paramref name="setScale"/>=false 时跳过缩放，交给 <see cref="AnimateWinRowScale"/> 缓动处理。</summary>
    private void ApplyWinLyricTierInstant(int i, int index, bool setScale = true)
    {
        var row = _winRows[i];
        var (color, opacity) = GetWinLyricTier(i, index);

        row.Main.TextColor = color;
        row.Main.Opacity = opacity;
        row.Trans.TextColor = color;
        row.Trans.Opacity = opacity * 0.8;
        row.Dot.IsVisible = i == index;

        if (setScale)
        {
            row.Main.AbortAnimation("ScaleTo");
            row.Main.Scale = i == index ? WinLyricCurrentScale : 1.0;
        }
    }

    /// <summary>瞬时把第 i 行的模糊落位（含"隐藏清晰原文"的容器透明度）。</summary>
    private void SetWinRowBlurInstant(int i, double blur)
    {
        var container = _winRows[i].Container;
        container.AbortAnimation(WinBlurAnimName);
        container.AbortAnimation("FadeTo");

        CatClawMusic.Maui.Effects.LyricBlurEffect.SetBlurAmount(container, blur);
        if (WinLyricBlurHideSource)
            container.Opacity = blur > 0.01 ? 0 : 1;
    }

    /// <summary>模糊过渡动画的名字，用于精确取消（不能用 CancelAnimations——会连带掐断缩放/行距动画）。</summary>
    private const string WinBlurAnimName = "WinLyricBlur";

    /// <summary>
    /// 缓动把第 i 行的模糊过渡到目标半径，与放大/行距/滚动同为 380ms CubicInOut，
    /// 视觉上"新当前行缓缓变清晰、旧当前行缓缓失焦"。
    /// 同步淡入/淡出清晰原文层（见 <see cref="WinLyricBlurHideSource"/>），二者交叉淡化。
    /// </summary>
    private void AnimateWinRowBlur(int i, double target)
    {
        if (i < 0 || i >= _winRows.Count) return;
        var container = _winRows[i].Container;

        var from = CatClawMusic.Maui.Effects.LyricBlurEffect.GetBlurAmount(container);
        if (Math.Abs(from - target) < 0.05) return;

        container.AbortAnimation(WinBlurAnimName);
        new Animation(v => CatClawMusic.Maui.Effects.LyricBlurEffect.SetBlurAmount(container, v), from, target)
            .Commit(container, WinBlurAnimName, 16, WinLyricScaleMs, Easing.CubicInOut);

        if (WinLyricBlurHideSource)
        {
            container.AbortAnimation("FadeTo");
            _ = container.FadeTo(target > 0.01 ? 0 : 1, WinLyricScaleMs, Easing.CubicInOut);
        }
    }

    /// <summary>缓动把第 i 行主文本缩放到目标倍率（Scale 是渲染变换，不影响行高与滚动锚点）。</summary>
    private void AnimateWinRowScale(int i, double target)
    {
        if (i < 0 || i >= _winRows.Count) return;
        var main = _winRows[i].Main;
        if (Math.Abs(main.Scale - target) < 0.01) return;

        main.AbortAnimation("ScaleTo");
        _ = main.ScaleTo(target, WinLyricScaleMs, Easing.CubicInOut);
    }

    /// <summary>
    /// 只把当前行上下两条缝隙撑开 <see cref="WinLyricGapExtra"/>：
    /// 当前行以上的行整体上移 -e，以下的行整体下移 +e，当前行自身不动。
    /// 相邻同侧行之间的相对距离不变 → 其余行距保持"减半"后的紧凑值。
    /// 用 Container.TranslationY（渲染变换，不重排、不改 _winRowTops），与整体滚动的
    /// WinLyricStack.TranslationY 独立叠加，互不干扰。
    /// 切句时实际只有 2 行的目标值发生变化，其余行会被阈值判断跳过。
    /// </summary>
    private void ApplyWinRowGap(int index, bool animate)
    {
        for (int i = 0; i < _winRows.Count; i++)
        {
            double target = i < index ? -WinLyricGapExtra
                          : i > index ? WinLyricGapExtra
                          : 0;

            var container = _winRows[i].Container;
            if (Math.Abs(container.TranslationY - target) < 0.5) continue;

            // 精确取消：容器上同时跑着模糊过渡与原文淡入淡出，CancelAnimations 会把它们一起掐断
            container.AbortAnimation("TranslateTo");
            if (animate)
                _ = container.TranslateTo(0, target, WinLyricScaleMs, Easing.CubicInOut);
            else
                container.TranslationY = target;
        }
    }

    // ═══════════════════════════════════════
    // 滚动：整体平移 WinLyricStack.TranslationY（合成线程变换，不重排）
    // ═══════════════════════════════════════

    /// <summary>把歌词瞬时/缓动钉到指定行：当前行**恒定**落在从上往下第 3 行的位置（离顶 2 行高）。
    ///
    /// 刻意**不做**顶部/底部夹紧——这正是"开头第 1 句就已经在第 3 行、结尾最后一句仍停在第 3 行"
    /// 的关键。夹紧会让开头几句挤在第 1/2 行、结尾几句掉到底部，当前行位置飘忽。
    /// 代价是首尾会露出空白区（顶部 2 行高、底部约 clipH-2行高），这是预期效果：
    /// 歌词像一条无限长的带子匀速穿过固定的"读取窗口"。
    ///
    /// 滚动实现 = 平移 WinLyricStack.TranslationY（合成线程变换，不重排）→ 丝滑无跳动，
    /// 等价于 BetterLyrics 的 ScrollOffset tween。animate=true 时 380ms CubicInOut 缓动上移一格。</summary>
    private void ScrollToWindowsLine(int index, bool animate)
    {
        if (index < 0 || index >= _winRows.Count) return;
        if (_winRowTops.Length != _winRows.Count) return;

        _winClipHeight = WinLyricClip.Bounds.Height; // 实时读，兼容窗口尺寸变化

        // 当前行顶部恒定落在离裁剪区顶部 2 行高度处。首尾不夹紧 → 位置永远一致。
        double topGap = 2 * (_winRowHeight > 0 ? _winRowHeight : 1);
        double targetY = topGap - _winRowTops[index];

        WinLyricStack.CancelAnimations();
        if (animate)
            WinLyricStack.TranslateTo(0, targetY, 380, Easing.CubicInOut);
        else
            WinLyricStack.TranslationY = targetY;

        WinLog($"Scroll idx={index} targetY={targetY:F1} topGap={topGap:F1}");
    }

    /// <summary>
    /// 实测各行顶部 Y（由实测行高累加，不依赖 Bounds.Y 的布局时机，避免未就绪时全 0 → 整列不滚）、
    /// 行高、整体高度、裁剪区高度，供滚动夹紧与锚点计算。行高未就绪（布局未跑完）时自动重试。
    /// </summary>
    private void MeasureWinRows()
    {
        if (_winRows.Count == 0) return;
        _winClipHeight = WinLyricClip.Bounds.Height;
        _winRowTops = new double[_winRows.Count];
        double y = 0;
        bool needsRetry = false;
        for (int i = 0; i < _winRows.Count; i++)
        {
            _winRowTops[i] = y;                 // 由前序行高累加（关键：不读 Bounds.Y，规避时机问题）
            var h = _winRows[i].Container.Bounds.Height;
            if (h <= 0.5)
            {
                h = _winRowHeight > 0 ? _winRowHeight : 40;   // 未就绪时先用回退值，重试会校正
                needsRetry = true;
            }
            y += h;
        }
        _winStackHeight = y;
        _winRowHeight = _winRows.Count > 0 ? y / _winRows.Count : 0;

        WinLog($"Measure: rows={_winRows.Count} rowH={_winRowHeight:F1} stackH={_winStackHeight:F1} clipH={_winClipHeight:F1} retry={_winMeasureRetries}");

        if (needsRetry && _winMeasureRetries < 6)
        {
            _winMeasureRetries++;
            _ = Task.Delay(120).ContinueWith(_ => MainThread.BeginInvokeOnMainThread(() =>
            {
                MeasureWinRows();
                if (_winFollow)
                    ScrollToWindowsLine(_viewModel.CurrentLyricIndexObservable >= 0
                        ? _viewModel.CurrentLyricIndexObservable : 0, animate: false);
            }));
        }
    }

    // ═══════════════════════════════════════
    // Windows 系统级毛玻璃背景（Acrylic）
    // ═══════════════════════════════════════

    /// <summary>
    /// 把 WinLyricClip 的平台 Grid 背景换成 AcrylicBrush（Windows 系统级毛玻璃）。
    /// 模糊由系统合成，歌词文字因为"透过毛玻璃看"自然柔化——
    /// 这才是 Windows 自带的真毛玻璃，比手动 Effect 稳得多（之前行级 Effect 实测效果
    /// 几乎看不出来：清晰原文与模糊副本 1:1 叠加时清晰笔画完全主导）。
    ///
    /// WinUI 3 的 AcrylicBrush 默认采宿主背后（无需设 Backdrop 即可模糊整个窗口下面的内容），
    /// 不支持时自动回退到 <c>FallbackColor</c>。这里用半透明深色 + 0.85 TintOpacity，
    /// 让磨砂感透出，但又不会把歌词列完全遮住。
    /// </summary>
    private void ApplyWindowsLyricBackdrop()
    {
        var platform = WinLyricClip.Handler?.PlatformView as Microsoft.UI.Xaml.Controls.Grid;
        if (platform is null) return;

        // 浅色磨砂 Tint：歌词列底色是 #080B1A，alpha 0xC8（≈78%）让背后透出来
        var tint = Windows.UI.Color.FromArgb(0xC8, 0x08, 0x0B, 0x1A);

        var brush = new AcrylicBrush
        {
            TintColor = tint,
            TintOpacity = 0.85,
            FallbackColor = tint,
        };
        platform.Background = brush;
    }

    // ═══════════════════════════════════════
    // 手动拖拽（用户拖动歌词 → 退出跟随、自由浏览）
    // ═══════════════════════════════════════

    /// <summary>给歌词裁剪区挂一个平移手势：用户拖动即退出跟随、手动浏览歌词。</summary>
    private void WireWinLyricPanGesture()
    {
        if (_winPanWired) return;
        _winPanWired = true;
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += OnWinLyricPan;
        WinLyricClip.GestureRecognizers.Add(pan);
    }

    private void OnWinLyricPan(object? sender, PanUpdatedEventArgs e)
    {
        if (e.StatusType == GestureStatus.Started)
        {
            WinLyricStack.CancelAnimations();
            _winPanStartY = WinLyricStack.TranslationY;
            if (_winFollow) SetWinFollow(false); // 拖动即退出跟随
        }
        else if (e.StatusType == GestureStatus.Running)
        {
            // 拖拽范围与自动滚动保持一致：允许把第 1 行拖到第 3 行位置（正向上界 = topGap），
            // 也允许把最后一行拖到第 3 行位置（负向下界）。否则拖到首尾会被硬弹回，
            // 与"当前行恒定钉在第 3 行"的观感不符。
            double topGap = 2 * (_winRowHeight > 0 ? _winRowHeight : 1);
            double lastTop = _winRowTops.Length > 0 ? _winRowTops[^1] : 0;
            double minTranslate = topGap - lastTop;   // 末行钉到第 3 行时的平移量（负值）
            var y = Math.Clamp(_winPanStartY + e.TotalY, Math.Min(minTranslate, topGap), topGap);
            WinLyricStack.TranslationY = y;
        }
    }

    private static void WinLog(string msg)
    {
        try
        {
            File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "catclaw_startup.log"),
                $"[{DateTime.Now:HH:mm:ss.fff}] WINLYR: {msg}\n");
        }
        catch { }
    }

    // ═══════════════════════════════════════
    // 顶栏拖拽（与 Controls/TitleBar 同一套手动拖动方案）
    // ═══════════════════════════════════════

    private bool _winIsDragging;
    private int _winDragStartMouseX, _winDragStartMouseY;
    private int _winDragStartWinX, _winDragStartWinY, _winDragWinW, _winDragWinH;
    private Microsoft.UI.Xaml.UIElement? _winDragElement;

    private const uint WIN_SWP_NOZORDER = 0x0004;
    private const uint WIN_SWP_NOACTIVATE = 0x0010;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out WinDragPoint lpPoint);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out WinDragRect lpRect);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct WinDragPoint { public int X; public int Y; }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct WinDragRect { public int Left; public int Top; public int Right; public int Bottom; }

    private void SetupWinDragArea()
    {
        // 使用系统 SetTitleBar 让 WinDragArea 成为标题栏拖拽区域
        // 系统会自动处理窗口拖拽、双击最大化/还原
        if (App.CurrentNativeWindow is { } win && WinDragArea.Handler?.PlatformView is Microsoft.UI.Xaml.UIElement el)
        {
            try
            {
                win.SetTitleBar(el);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"NowPlayingPage SetTitleBar failed: {ex.Message}");
                // 后备：手动拖拽
                el.PointerPressed -= OnWinDragPointerPressed;
                el.PointerMoved -= OnWinDragPointerMoved;
                el.PointerReleased -= OnWinDragPointerReleased;
                el.DoubleTapped -= OnWinDragDoubleTapped;
                el.PointerPressed += OnWinDragPointerPressed;
                el.PointerMoved += OnWinDragPointerMoved;
                el.PointerReleased += OnWinDragPointerReleased;
                el.DoubleTapped += OnWinDragDoubleTapped;
            }
        }
    }

    private void OnWinDragPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint((Microsoft.UI.Xaml.UIElement)sender).Properties.IsLeftButtonPressed) return;
        if (App.CurrentAppWindow == null) return;
        if (App.CurrentAppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter p
            && p.State == Microsoft.UI.Windowing.OverlappedPresenterState.Maximized) return;

        var hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(App.CurrentAppWindow.Id);
        GetCursorPos(out var pt);
        _winDragStartMouseX = pt.X;
        _winDragStartMouseY = pt.Y;
        GetWindowRect(hwnd, out var rc);
        _winDragStartWinX = rc.Left;
        _winDragStartWinY = rc.Top;
        _winDragWinW = rc.Right - rc.Left;
        _winDragWinH = rc.Bottom - rc.Top;

        _winIsDragging = true;
        _winDragElement = (Microsoft.UI.Xaml.UIElement)sender;
        _winDragElement.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnWinDragPointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_winIsDragging || App.CurrentAppWindow == null) return;

        GetCursorPos(out var pt);
        var dx = pt.X - _winDragStartMouseX;
        var dy = pt.Y - _winDragStartMouseY;
        var hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(App.CurrentAppWindow.Id);
        SetWindowPos(hwnd, IntPtr.Zero, _winDragStartWinX + dx, _winDragStartWinY + dy,
            _winDragWinW, _winDragWinH, WIN_SWP_NOZORDER | WIN_SWP_NOACTIVATE);
        e.Handled = true;
    }

    private void OnWinDragPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_winIsDragging) return;
        _winIsDragging = false;
        _winDragElement?.ReleasePointerCapture(e.Pointer);
        _winDragElement = null;
        e.Handled = true;
    }

    private void OnWinDragDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        if (App.CurrentAppWindow?.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            if (presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Restored)
                presenter.Maximize();
            else
                presenter.Restore();
        }
        e.Handled = true;
    }

    /// <summary>行级渐隐已废弃：CollectionView 自带可见性管理（ItemContainer 复用），
    /// 再叠加距离渐隐会让 Item 模式产生闪烁。保留空方法以兼容旧事件订阅。</summary>
    private void UpdateWinLyricRowOpacity()
    {
        // CollectionView 自带视口可见性管理，不再需要手动算行级渐隐
    }

    // ═══════════════════════════════════════
    // 播放控制
    // ═══════════════════════════════════════

    private void OnWinPlayTapped(object? sender, TappedEventArgs e)
    {
        _viewModel.TogglePlayPauseCommand.Execute(null);
    }

    /// <summary>刷新播放按钮图标（白色圆按钮 + 深色图标）与 EQ 动画状态。</summary>
    private void UpdateWinPlayIcon()
    {
        WinPlayIcon.Source = _winIsPlaying
            ? ImageSourceHelper.FromNameOriginal("ic_pause_light")
            : ImageSourceHelper.FromNameOriginal("ic_play_light");
    }

    /// <summary>播放状态变化：更新图标与 EQ 动画。</summary>
    private void OnWinPlayingChanged()
    {
        _winIsPlaying = _viewModel.IsPlaying;
        UpdateWinPlayIcon();
        UpdateWinEqAnimation();
    }

    // ═══════════════════════════════════════
    // 收藏
    // ═══════════════════════════════════════

    private void OnWinLikeTapped(object? sender, TappedEventArgs e)
    {
        _viewModel.ToggleLikeCommand.Execute(null);
    }

    /// <summary>刷新收藏 chip 图标与文字（已收藏时显示爱心 + 主题色）。</summary>
    private void UpdateWinLikeIcon()
    {
        var liked = _viewModel.IsLiked;
        WinLikeIcon.Source = liked
            ? ImageSourceHelper.FromNameOriginal("ic_favorite_white")
            : ImageSourceHelper.FromNameOriginal("ic_favorite_border_white");
        WinLikeLabel.Text = liked ? "已收藏" : "收藏";
        WinLikeLabel.TextColor = liked
            ? (Color)Application.Current!.Resources["LikeColor"]
            : Colors.White;
    }

    // ═══════════════════════════════════════
    // 跟随开关
    // ═══════════════════════════════════════

    private void OnWinFollowTapped(object? sender, TappedEventArgs e)
        => SetWinFollow(!_winFollow);

    /// <summary>切换歌词跟随模式，并同步按钮外观；重新开启时立即缓动回到当前行。</summary>
    private void SetWinFollow(bool follow)
    {
        if (_winFollow == follow) return;
        _winFollow = follow;

        WinFollowLabel.Text = follow ? "跟随" : "手动";
        WinFollowBtn.BackgroundColor = follow
            ? (Color)Application.Current!.Resources["PrimaryColor"]
            : Color.FromArgb("#10FFFFFF");

        // 当前行滚动 = WinLyricStack.TranslationY 缓动平移（合成线程变换，不重排）。
        // 重新跟随时从当前位移 tween 回当前行，无跳动。不再需要旧 CollectionView 的自动滚动定时器。
        if (follow && _winRows.Count > 0 && _viewModel.CurrentLyricIndexObservable >= 0)
        {
            HighlightWindowsLine(_viewModel.CurrentLyricIndexObservable);
        }
        // follow=false 时无需停任何定时器（平移由手势直接驱动，跟随由切句时 HighlightWindowsLine 驱动）。
    }

    // ═══════════════════════════════════════
    // 音量与静音
    // ═══════════════════════════════════════

    private void OnWinVolumeChanged(object? sender, ValueChangedEventArgs e)
    {
        var v = Math.Clamp(e.NewValue, 0.0, 1.0);
        try
        {
            _audioPlayer.Volume = v;
        }
        catch { }
        _winIsMuted = v <= 0;
        if (v > 0) _winLastVolume = v;
        UpdateWinMuteIcon();
    }

    private void OnWinMuteTapped(object? sender, EventArgs e)
    {
        if (_winIsMuted)
        {
            var restore = _winLastVolume > 0 ? _winLastVolume : 0.7;
            WinVolumeSlider.Value = restore;
        }
        else
        {
            _winLastVolume = WinVolumeSlider.Value;
            WinVolumeSlider.Value = 0;
        }
    }

    private void UpdateWinMuteIcon()
    {
        WinMuteBtn.Source = ImageSourceHelper.FromNameOriginal(_winIsMuted ? "ic_volume_mute" : "ic_volume");
        WinMuteBtn.Opacity = _winIsMuted ? 0.5 : 1.0;
    }

    // ═══════════════════════════════════════
    // EQ 频谱动画
    // ═══════════════════════════════════════

    private void UpdateWinEqAnimation()
    {
        if (_winIsPlaying)
            StartWinEq();
        else
            StopWinEq();
    }

    private void StartWinEq()
    {
        if (_winEqTimer != null) return;
        _winEqTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _winEqTimer.Tick += OnWinEqTick;
        _winEqTimer.Start();
    }

    private void StopWinEq()
    {
        if (_winEqTimer == null) return;
        _winEqTimer.Stop();
        _winEqTimer.Tick -= OnWinEqTick;
        _winEqTimer = null;
        // 复位为低条
        WinEq1.HeightRequest = 4;
        WinEq2.HeightRequest = 4;
        WinEq3.HeightRequest = 4;
        WinEq4.HeightRequest = 4;
    }

    private void OnWinEqTick(object? sender, object e)
    {
        _winEqTime += 0.08;
        WinEq1.HeightRequest = 3 + 9 * (0.5 + 0.5 * Math.Sin(_winEqTime * 3.1));
        WinEq2.HeightRequest = 3 + 9 * (0.5 + 0.5 * Math.Sin(_winEqTime * 4.3 + 1.0));
        WinEq3.HeightRequest = 3 + 9 * (0.5 + 0.5 * Math.Sin(_winEqTime * 3.7 + 2.0));
        WinEq4.HeightRequest = 3 + 9 * (0.5 + 0.5 * Math.Sin(_winEqTime * 5.0 + 0.5));
    }
}
#endif
