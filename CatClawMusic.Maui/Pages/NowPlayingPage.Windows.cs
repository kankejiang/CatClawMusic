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
    // Windows 歌词视图（CollectionView ItemsSource）
    private readonly List<LyricLineViewModel> _winLyricItems = new();
    private int _winLastHighlight = -1;
    private bool _winFollow = true;
    // 最近一次"程序发起"的滚动时刻，用于把自动滚动产生的 Scrolled 事件与用户手动拖动区分开
    private DateTime _winLastAutoScrollAt = DateTime.MinValue;
    // 程序滚动（含其平滑动画收尾）在此时刻之前产生的所有 Scrolled 一律忽略。
    // 关键修复：WinUI ChangeView(disableAnimation:false) 的平滑滚动收尾事件会晚于
    // _winProgramScrolling 清空的时机到达，原 500ms 宽限不足以覆盖，导致一次尾随 Scrolled
    // 把 _winFollow 误置为 false —— 此后每句只走 WithoutScroll（不滚动），歌词"看起来不动了"。
    // 改用一个独立的、较长的免疫窗口（1000ms）来彻底屏蔽程序滚动的尾随事件。
    private DateTime _winExpectScrollUntil = DateTime.MinValue;
    // 程序正在做平滑滚动（动画进行中）→ 期间产生的 Scrolled 一律忽略，避免误关跟随
    private bool _winProgramScrolling;
    // 保存计算出的滚动区高度，handler attach 后强制设到原生 ScrollViewer（仅 ScrollView 方案用，保留兼容）
    private double _winLastScrollHeight;

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

        // 歌词构建
        BuildWindowsLyricViews();
        if (_winLyricItems.Count > 0 && _viewModel.CurrentLyricIndexObservable >= 0)
        {
            _ = Task.Delay(120).ContinueWith(_ =>
                MainThread.BeginInvokeOnMainThread(() =>
                    HighlightWindowsLine(_viewModel.CurrentLyricIndexObservable)));
        }

        // 手动拖动歌词时自动退出跟随，避免用户往回翻时被下一次自动滚动拽回来
        WinLyricsList.Scrolled -= OnWinLyricsScrolled;
        WinLyricsList.Scrolled += OnWinLyricsScrolled;

        // EQ 动画
        UpdateWinEqAnimation();
    }

    // ═══════════════════════════════════════
    // 歌词
    // ═══════════════════════════════════════

    // 歌词层次配色（参考设计稿：纯白当前 + 冷灰递进，避免品牌色干扰阅读）
    private static readonly Color WinLyricCurrentColor = Colors.White;                // 当前行
    private static readonly Color WinLyricNearColor = Color.FromArgb("#B8B8C8");      // 相邻 1 行
    private static readonly Color WinLyricFarColor = Color.FromArgb("#787888");       // 更远的行

    // 动画帧间隔（毫秒）：8ms ≈ 125fps 目标。所有歌词动画（字号/颜色）统一用此帧率，
    // 在高刷新率（120/144Hz）屏幕上能跑满刷新率，运动更顺滑、帧数更高。
    // （普通 60Hz 屏渲染上限仍是 60fps，但更密的采样不会更卡。）
    private const uint WinLyricFrameMs = 8;

    // 字号/颜色/模糊三层过渡的统一时长（毫秒）。
    // 用 ~480ms + CubicInOut（慢起慢收）而非原先 280ms 的 CubicOut（快起），
    // 让"新当前行从下方缓缓长大 + 缓缓变清晰"在随滚动上移的过程中清晰可读，
    // 而不是一帧内就"啪"地变到最大最清晰。
    private const uint WinLyricGrowMs = 480;

    // 行内边距（DP）：基础档（多数行）与"关键三行"档（已唱上一行 / 当前行 / 未唱下一行）。
    // 加大间距 → 每切一句时列表"向上滚动"的步长更大、更显眼（之前步长太小，几乎看不出在滚）。
    private static readonly Thickness WinLyricPadBase = new(0, 12, 0, 12);
    private static readonly Thickness WinLyricPadKey = new(0, 34, 0, 34);
    /// <summary>当前行及其上下相邻行的上下内边距加大，让关键三行更透气。</summary>
    private static Thickness WinLyricRowPadding(int i, int index)
        => (i == index || i == index - 1 || i == index + 1) ? WinLyricPadKey : WinLyricPadBase;

    /// <summary>构建 Windows 歌词视图到 WinLyricsList（CollectionView）。</summary>
    private void BuildWindowsLyricViews()
    {
        _winLyricItems.Clear();
        _winLastHighlight = -1;

        var lines = _viewModel.AllLyricLines;
        if (lines == null || lines.Count == 0)
        {
            WinNoLyricsLabel.IsVisible = true;
            WinLyricsList.ItemsSource = null;
            return;
        }
        WinNoLyricsLabel.IsVisible = false;

        var baseSize = _settings.FontSize;

        foreach (var line in lines)
        {
            _winLyricItems.Add(new LyricLineViewModel
            {
                Text = line.Text,
                Translation = line.Translation ?? string.Empty,
                MainFontSize = baseSize * 0.74,
                TransFontSize = baseSize * 0.74 - 2,
                MainColor = WinLyricFarColor,
                TransColor = WinLyricFarColor,
                MainOpacity = 1.0,
                TransOpacity = 0.85,
                Blur = 6.0,
                IsCurrent = false,
            });
        }

        WinLyricsList.ItemsSource = _winLyricItems;

        var idx = _viewModel.CurrentLyricIndexObservable >= 0 ? _viewModel.CurrentLyricIndexObservable : 0;
        HighlightWindowsLineWithoutScroll(idx);

        WinLog($"Build done: sourceLines={lines.Count} items={_winLyricItems.Count} baseSize={baseSize}");
    }

    /// <summary>刷新全部行的层次样式，但不触发滚动（构建期/初始化用）。</summary>
    private void HighlightWindowsLineWithoutScroll(int index)
    {
        if (index < 0 || index >= _winLyricItems.Count) return;

        var baseSize = _settings.FontSize;
        for (int i = 0; i < _winLyricItems.Count; i++)
            ApplyWinLyricTierInstant(i, index, baseSize);
        _winLastHighlight = index;
    }

    /// <summary>
    /// 高亮指定行并平滑滚动到"从上往下第 3 行"。
    ///
    /// 分层策略（兼顾"好看"与"不卡滚动"）：
    /// 1. 字号**只对真正发生变化的那几行做 ~240ms 缓出 grow/shrink 动画**（见 AnimateWinLyricSize）；
    ///    其余行字号不变、setter 内部值比较直接去重，不触发任何重测量。
    /// 2. 颜色 / 透明度不参与布局，单独做 ~300ms 缓出交叉淡入，营造"高亮渐显"的观感。
    /// 3. 滚动本身用原生 ScrollViewer 的 VerticalOffset 缓动（见 AnimateScrollTo），
    ///    而不是 CollectionView.ScrollTo(animate:true) —— 后者在 WinUI 3 上会被任何
    ///    布局刷新打断；这里用自驱动的 ChangeView 实现丝滑跟随，且字号动画只动 2~3 行，
    ///    不会把整列拉进重测量风暴，故滚动依旧稳。
    /// </summary>
    private void HighlightWindowsLine(int index)
    {
        if (index < 0 || index >= _winLyricItems.Count) return;

        var baseSize = _settings.FontSize;

        // 捕获每行颜色旧值，供后续交叉淡入；字号单独做 grow/shrink 动画。
        // 优化：仅把"颜色/透明度确实变化"的行加入动画列表，避免每帧无谓刷新全部 24 行
        // （这正是之前卡帧的主因——24 行 × 4 个属性每帧重绑，UI 线程被压到远低于 60fps）。
        var states = new List<(LyricLineViewModel Vm,
            Color FromMain, Color ToMain, double FromMainOp, double ToMainOp,
            Color FromTrans, Color ToTrans)>();
        for (int i = 0; i < _winLyricItems.Count; i++)
        {
            var vm = _winLyricItems[i];
            var (size, color, opacity, blur) = GetWinLyricTier(i, index, baseSize);

            // 行距：关键三行（已唱上一行 / 当前 / 未唱下一行）瞬时加大，不做动画以免布局抖动
            vm.RowPadding = WinLyricRowPadding(i, index);

            if (!LyricLineViewModel.SameColor(vm.MainColor, color) || !LyricLineViewModel.NearlyEqual(vm.MainOpacity, opacity)
                || !LyricLineViewModel.SameColor(vm.TransColor, color))
            {
                states.Add((vm, vm.MainColor, color, vm.MainOpacity, opacity,
                    vm.TransColor, color));
            }

            vm.MainFontAttributes = FontAttributes.Bold;   // 所有行加粗

            // 模糊半径：变化明显（相邻3 → 当前0，或 当前0 → 已唱6）时做缓动过渡，
            // 避免"啪"地变清晰/变糊造成当前行像闪现出来。
            var fromBlur = vm.Blur;
            if (Math.Abs(fromBlur - blur) < 0.15)
                vm.Blur = blur;
            else
                AnimateWinLyricBlur(i, fromBlur, blur);
            vm.IsCurrent = i == index;

            // 字号：变化明显的那几行（上一当前→已唱、下一未唱→当前）做 grow/shrink 动画；
            // 几乎不变的直接落位，省去无谓的重测量。
            var fromSize = vm.MainFontSize;
            if (Math.Abs(fromSize - size) < 0.5)
            {
                vm.MainFontSize = size;
                vm.TransFontSize = Math.Max(10, size - 4);
            }
            else
            {
                AnimateWinLyricSize(i, fromSize, size);
            }
        }
        _winLastHighlight = index;

        // 颜色 / 透明度 交叉淡入：只动真正变化的那几行（layout 无关，安全逐帧）。
        // 帧间隔 WinLyricFrameMs(8ms≈125fps)，高刷屏上更顺滑；每帧只动 3~5 行，开销极低。
        this.AbortAnimation("WinLyricColor");
        var colorAnim = new Animation(t =>
        {
            foreach (var s in states)
            {
                s.Vm.MainColor = LerpColor(s.FromMain, s.ToMain, t);
                s.Vm.MainOpacity = s.FromMainOp + (s.ToMainOp - s.FromMainOp) * t;
                s.Vm.TransColor = LerpColor(s.FromTrans, s.ToTrans, t);
                s.Vm.TransOpacity = s.ToMainOp * 0.88;
            }
        }, 0, 1, Easing.CubicInOut);
        colorAnim.Commit(this, "WinLyricColor", length: WinLyricGrowMs, rate: WinLyricFrameMs);

        WinLog($"Highlight idx={index}/{_winLyricItems.Count} follow={_winFollow}");
        ScrollToWindowsLine(index);
    }

    /// <summary>对某一行字号做 ~480ms 缓动 grow/shrink（CubicInOut，慢起慢收），
    /// 配合"已唱缩小 / 未唱放大"的层次过渡，让新当前行在随滚动上移的过程中"缓缓长大"。
    /// 只动这一行的两个字号属性，且 setter 值比较去重，避免连锁重测量拖慢滚动。</summary>
    private void AnimateWinLyricSize(int i, double fromSize, double toSize)
    {
        var vm = _winLyricItems[i];
        var fromTrans = vm.TransFontSize;
        var toTrans = Math.Max(10, toSize - 4);
        var key = $"WinLyricSize{i}";
        this.AbortAnimation(key);
        new Animation(t =>
        {
            vm.MainFontSize = fromSize + (toSize - fromSize) * t;
            vm.TransFontSize = fromTrans + (toTrans - fromTrans) * t;
        }, 0, 1, Easing.CubicInOut).Commit(this, key, length: WinLyricGrowMs, rate: WinLyricFrameMs);
    }

    /// <summary>对某一行的高斯模糊半径做 ~480ms 缓动过渡（CubicInOut，慢起慢收）。
    /// 之前模糊是"<b>瞬时</b>"设置的——一行从相邻(blur 3)变成当前(blur 0)时会在一帧内"啪"地变清晰，
    /// 视觉上就像当前行"闪现"；后来改为逐帧重建 Win2D 模糊层，但每帧重建整张 CompositionVisualSurface
    /// 会重新捕获文字视觉树，导致闪烁/卡顿，"变清晰"仍像跳变。现在模糊层（surface+sprite）跨帧复用，
    /// 只重建轻量的模糊 effect/brush，故此处 480ms 的缓动能平滑呈现"缓缓变清晰 / 缓缓变糊"。</summary>
    private void AnimateWinLyricBlur(int i, double fromBlur, double toBlur)
    {
        var vm = _winLyricItems[i];
        var key = $"WinLyricBlur{i}";
        this.AbortAnimation(key);
        new Animation(t => vm.Blur = fromBlur + (toBlur - fromBlur) * t, 0, 1, Easing.CubicInOut)
            .Commit(this, key, length: WinLyricGrowMs, rate: WinLyricFrameMs);
    }

    /// <summary>两个 MAUI Color 之间线性插值（t∈[0,1]）。</summary>
    private static Color LerpColor(Color a, Color b, double t)
    {
        t = t < 0 ? 0 : t > 1 ? 1 : t;
        return Color.FromRgba(
            a.Red + (b.Red - a.Red) * t,
            a.Green + (b.Green - a.Green) * t,
            a.Blue + (b.Blue - a.Blue) * t,
            a.Alpha + (b.Alpha - a.Alpha) * t);
    }

    /// <summary>
    /// 按与当前行的"方向 + 距离"返回层次样式（已唱缩小 / 未唱放大，当前行为峰值）。
    ///
    /// - 当前行 (i==index)：最大、清晰、白、加粗。
    /// - 未唱行 (i&gt;index，未来)：比已唱明显大、接近当前，颜色偏亮；越靠下略减。
    /// - 已唱行 (i&lt;index，过去)：明显缩小、颜色转冷灰、模糊更强；越往上越小越糊。
    ///
    /// 行在"未唱→当前→已唱"推进时，字号先增大后减小，配合 <see cref="AnimateWinLyricSize"/>
    /// 的缓出动画形成自然的 grow/shrink 呼吸感。
    /// </summary>
    private static (double Size, Color Color, double Opacity, double Blur) GetWinLyricTier(
        int i, int index, double baseSize)
    {
        if (i == index)
            return (baseSize * 1.5, WinLyricCurrentColor, 1.0, 0.0);   // 当前：峰值、比基础大 50%、清晰、白

        if (i < index) // 已唱（过去）：明显缩小 + 渐隐 + 渐糊，越往上越小越糊
        {
            var d = index - i;
            var size = d switch
            {
                1 => baseSize * 0.66,
                2 => baseSize * 0.60,
                _ => baseSize * 0.54,
            };
            return (Math.Max(12, size), WinLyricFarColor, d <= 2 ? 0.55 : 0.42, d <= 1 ? 6.0 : 9.0);
        }
        else // 未唱（未来）：比已唱明显大、接近当前，但明显比当前小且带模糊 → 升为当前时"长大+变清晰"落差更猛
        {
            var d = i - index;
            var size = d switch
            {
                1 => baseSize * 0.82,
                2 => baseSize * 0.78,
                _ => baseSize * 0.74,
            };
            return (Math.Max(13, size), WinLyricNearColor, d <= 2 ? 0.82 : 0.65, d <= 1 ? 4.0 : 7.0);
        }
    }

    /// <summary>无动画地把某一行落到它应有的层次样式（setter 内部已做值比较，重复调用无开销）。</summary>
    private void ApplyWinLyricTierInstant(int i, int index, double baseSize)
    {
        var item = _winLyricItems[i];
        var (size, color, opacity, blur) = GetWinLyricTier(i, index, baseSize);

        item.IsCurrent = i == index;
        item.MainFontAttributes = FontAttributes.Bold;   // 所有行加粗
        item.MainFontSize = size;
        item.MainColor = color;
        item.MainOpacity = opacity;
        item.TransFontSize = Math.Max(10, size - 4);
        item.TransColor = color;
        item.TransOpacity = opacity * 0.88;
        item.Blur = blur;                                // 高斯模糊半径（DP），由 LyricBlurEffect 消费
        item.RowPadding = WinLyricRowPadding(i, index);  // 关键三行加大行距
    }

    /// <summary>
    /// 平滑滚动到指定行。
    ///
    /// 做法：拿到 CollectionView 内原生 ScrollViewer，用自驱动的 <c>ChangeView</c> + 缓出曲线
    /// 把 VerticalOffset 从当前位置插值到"目标行固定在从上往下第 3 行"的位置（~320ms）。相比
    /// <c>CollectionView.ScrollTo(animate:true)</c>，它不受布局刷新打断，丝滑且可控。
    /// 第 3 行意味着当前行上方始终留出 2 行（index-1、index-2）的高度，这样当前行视觉上钉在
    /// 列表偏上位置，而非垂直居中——更接近主流播放器歌词页的观感。
    ///
    /// 离屏项的 ItemContainer 尚未生成时 <c>ContainerFromIndex</c> 返回 null，先瞬时定位逼出容器，
    /// 下一帧再补平滑滚动。
    /// </summary>
    private void ScrollToWindowsLine(int index)
    {
        if (index < 0 || index >= _winLyricItems.Count) return;
        if (WinLyricsList.ItemsSource == null) return;

        var sv = GetInnerScrollViewer();
        var lv = WinLyricsList.Handler?.PlatformView as WinListViewBase;
        var container = lv?.ContainerFromIndex(index) as WinFrameworkElement;

        // 容器未就绪（首次进入 / 离屏）：瞬时定位逼出容器，稍后补平滑滚动
        if (sv == null || container == null)
        {
            try
            {
                _winLastAutoScrollAt = DateTime.UtcNow;
                _winExpectScrollUntil = DateTime.UtcNow.AddMilliseconds(1000);
                WinLyricsList.ScrollTo(index, position: ScrollToPosition.Center, animate: false);
                WinLog($"ScrollTo[realize] idx={index}/{_winLyricItems.Count}");
            }
            catch (Exception ex)
            {
                WinLog($"ScrollTo[realize] FAILED idx={index}: {ex.Message}");
            }

            _ = Task.Delay(70).ContinueWith(_ => MainThread.BeginInvokeOnMainThread(() =>
            {
                if (WinLyricsList.ItemsSource != null && index < _winLyricItems.Count
                    && index >= 2) // 第 3 行以下才有完整 2 行做落脚，否则直接落 top
                {
                    ScrollToWindowsLine(index);
                }
                else
                {
                    // 顶部几行：没有 2 行可垫，直接瞬时顶到列表开头即可
                    try
                    {
                        _winLastAutoScrollAt = DateTime.UtcNow;
                        _winExpectScrollUntil = DateTime.UtcNow.AddMilliseconds(1000);
                        WinLyricsList.ScrollTo(0, position: ScrollToPosition.Start, animate: false);
                    }
                    catch { }
                }
            }));
            return;
        }

        // 强制布局更新：刚被我们改成新字号的容器要让 ActualHeight/位置立即生效，
        // 否则算出的目标偏移会基于旧高度，丝滑滚到位时实际已偏离目标行。
        try { container.UpdateLayout(); } catch { }
        try { sv.UpdateLayout(); } catch { }

        // 计算目标偏移：把当前行钉在"从上往下第 3 行"——即它上方留出 2 行（index-1、index-2）的高度。
        // 上面两行若已实体化就用其真实高度，否则用当前行高度兜底估算，保证位置稳定不跳。
        double topGap = 0;
        for (int k = 1; k <= 2; k++)
        {
            int above = index - k;
            if (above < 0) break;
            var aboveC = lv?.ContainerFromIndex(above) as WinFrameworkElement;
            var aboveH = aboveC?.ActualHeight ?? container.ActualHeight;
            topGap += aboveH;
        }

        var itemTopInViewport = container.TransformToVisual(sv)
            .TransformPoint(new global::Windows.Foundation.Point(0, 0)).Y;
        var targetOffset = sv.VerticalOffset + itemTopInViewport - topGap;
        targetOffset = Math.Clamp(targetOffset, 0, sv.ScrollableHeight);

        WinLog($"ScrollTo[anim] idx={index}/{_winLyricItems.Count} " +
               $"from={sv.VerticalOffset:F1} to={targetOffset:F1} topGap={topGap:F1}");
        AnimateScrollTo(targetOffset);
    }

    /// <summary>
    /// 平滑滚动到 target。
    ///
    /// 关键：不再用 MAUI 的 <c>Animation</c> 每帧在 UI 线程调用 <c>ChangeView</c> 做插值——
    /// 那种做法帧数受 UI 线程负载拖累，容易掉帧、看起来"帧数低"。
    /// 改为调用 <c>ChangeView(..., disableAnimation:false)</c>，把平滑滚动交给 WinUI 在
    /// **合成线程（compositor）**上完成，帧率直接等于显示器刷新率（60/120/144Hz），
    /// 与 UI 线程解耦，运动明显更顺滑、帧数更高。
    /// 用 <c>ViewChanged</c> 的 <c>IsIntermediate</c> 判断动画结束，期间持续刷新时间戳，
    /// 避免被 <c>OnWinLyricsScrolled</c> 误判为"用户手动滚动"而退出跟随。
    /// </summary>
    private void AnimateScrollTo(double target)
    {
        var sv = GetInnerScrollViewer();
        if (sv == null) return;

        var from = sv.VerticalOffset;
        if (Math.Abs(target - from) < 0.5)
        {
            _winLastAutoScrollAt = DateTime.UtcNow;
            _winExpectScrollUntil = DateTime.UtcNow.AddMilliseconds(1000);
            return;
        }

        _winProgramScrolling = true;
        _winLastAutoScrollAt = DateTime.UtcNow;
        _winExpectScrollUntil = DateTime.UtcNow.AddMilliseconds(1000);

        void OnViewChanged(object? s, WinScrollViewerViewChangedEventArgs e)
        {
            _winLastAutoScrollAt = DateTime.UtcNow;
            _winExpectScrollUntil = DateTime.UtcNow.AddMilliseconds(1000);
            if (!e.IsIntermediate) // 平滑滚动已结束
            {
                if (sv != null) sv.ViewChanged -= OnViewChanged;
                _winProgramScrolling = false;
            }
        }
        sv.ViewChanged += OnViewChanged;

        // disableAnimation:false —— 由 WinUI 在合成线程上做缓出平滑滚动
        sv.ChangeView(null, target, null, disableAnimation: false);
    }

    /// <summary>拿到 CollectionView 内层的原生 ScrollViewer（用于手动平滑滚动）。</summary>
    private WinScrollViewer? GetInnerScrollViewer()
    {
        if (WinLyricsList.Handler?.PlatformView is not WinListViewBase lv) return null;
        return FindVisualChild<WinScrollViewer>(lv);
    }

    private static T? FindVisualChild<T>(WinDependencyObject? parent) where T : WinDependencyObject
    {
        if (parent == null) return null;
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            var found = FindVisualChild<T>(child);
            if (found != null) return found;
        }
        return null;
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
        if (WinDragArea.Handler?.PlatformView is Microsoft.UI.Xaml.UIElement el)
        {
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

    /// <summary>切换歌词跟随模式，并同步按钮外观；重新开启时立即回到当前行。</summary>
    private void SetWinFollow(bool follow)
    {
        if (_winFollow == follow) return;
        _winFollow = follow;

        WinFollowLabel.Text = follow ? "跟随" : "手动";
        WinFollowBtn.BackgroundColor = follow
            ? (Color)Application.Current!.Resources["PrimaryColor"]
            : Color.FromArgb("#10FFFFFF");

        if (follow && _winLyricItems.Count > 0 && _viewModel.CurrentLyricIndexObservable >= 0)
            HighlightWindowsLine(_viewModel.CurrentLyricIndexObservable);
    }

    /// <summary>
    /// 用户手动拖动歌词列表 → 自动退出跟随模式。
    /// 程序自身发起的 ScrollTo 也会触发 Scrolled，用时间窗口把它过滤掉。
    /// </summary>
    private void OnWinLyricsScrolled(object? sender, ItemsViewScrolledEventArgs e)
    {
        if (!_winFollow) return;
        if (_winProgramScrolling) return; // 程序平滑滚动进行中，忽略自身产生的滚动事件
        // 屏蔽程序滚动收尾阶段的尾随 Scrolled（见 _winExpectScrollUntil 注释），
        // 否则会把 _winFollow 误关，导致后续切句不再滚动。只有超出免疫窗口的滚动才视为用户手动拖动。
        if (DateTime.UtcNow < _winExpectScrollUntil) return;
        SetWinFollow(false);
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
        WinMuteBtn.Source = _winIsMuted ? "ic_volume_mute" : "ic_volume";
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

    // ═══════════════════════════════════════
    // 最大化图标
    // ═══════════════════════════════════════

    private void UpdateWinMaximizeIcon()
    {
        if (App.CurrentAppWindow?.Presenter is not Microsoft.UI.Windowing.OverlappedPresenter presenter)
            return;
        var isMaximized = presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Maximized;
        WinMaximizeIcon.IsVisible = !isMaximized;
        WinRestoreIcon.IsVisible = isMaximized;
    }
}
#endif
