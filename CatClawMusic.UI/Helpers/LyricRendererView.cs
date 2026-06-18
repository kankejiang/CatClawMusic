using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Widget;
using CatClawMusic.Core.Models;
using CatClawMusic.UI.ViewModels;

namespace CatClawMusic.UI.Helpers;

/// <summary>
/// 共享歌词渲染视图：封装 ScrollView + 多行歌词 + 高亮 + 滚动 + 逐字进度的完整渲染系统。
/// 供全屏歌词页（FullLyricsFragment）和播放页（NowPlayingFragment）复用。
/// </summary>
public class LyricRendererView : FrameLayout
{
    // 颜色预设（与 FullLyricsFragment 保持一致，用于 LoadSettings 旧版索引迁移）
    private static readonly string[] PresetColorHex =
        { "#FFFFFFFF", "#FF000000", "#FFFFEB3B", "#FF69F0AE", "#FFFF80AB", "#FF64B5F6", "#FFFFAB40", "#FFFF6E6E", "#FFCE93D8", "#FF4DD0E1" };
    private static readonly string[] InactivePresetHex =
        { "#CCBBBBBB", "#CC555555", "#CC000000", "#DDDDDDDD", "#CC90A4AE", "#CCB39DDB", "#CCBDBDBD", "#CC78909C" };
    private static readonly string[] BgColorHex = { "#99F0EBE3", "#990F0D16", "#33000000" };

    // 内部视图
    private readonly ScrollView _scrollView;
    private readonly LinearLayout _lyricsContainer;
    private readonly List<LyricViewItem> _lyricViews = new();
    private readonly List<WeakReference<TextView>> _translationViews = new();

    // 高亮/拖拽状态
    private int _lastLyricIndex = -1;
    private int _lastDuetPartnerIndex = -1;
    private bool _userScrolling;
    private bool _isDragging;
    private int _draggedLyricIndex = -1;
    private readonly Handler _scrollResumeHandler = new(Looper.MainLooper!);
    private readonly Handler _dragResumeHandler = new(Looper.MainLooper!);

    // 内部配置（从 prefs 读取，与公开属性配合）
    private int _lyricBgColorIndex = 0;
    private int _lyricActiveArgb = unchecked((int)0xFFFFFFFF);
    private int _lyricInactiveArgb = unchecked((int)0xCCBBBBBB);
    private Dictionary<string, int> _roleSideMap = new(StringComparer.OrdinalIgnoreCase);

    // 上下淡出遮罩（默认不创建，原 FullLyricsFragment 已禁用此效果）
    private View? _fadeTop;
    private View? _fadeBottom;

    // ── 构造函数 ──

    public LyricRendererView(Context context) : base(context)
    {
        _scrollView = new ScrollView(context) { FillViewport = true };
        _lyricsContainer = new LinearLayout(context) { Orientation = Orientation.Vertical };
        _scrollView.AddView(_lyricsContainer);
        AddView(_scrollView, new LayoutParams(LayoutParams.MatchParent, LayoutParams.MatchParent));
    }

    public LyricRendererView(Context context, IAttributeSet? attrs) : this(context) { }

    public LyricRendererView(IntPtr handle, JniHandleOwnership ownership) : base(handle, ownership)
    {
        // 仅满足 Android 运行时要求；实际使用应通过 Context 构造。
        _scrollView = null!;
        _lyricsContainer = null!;
    }

    // ── 公开配置属性 ──

    /// <summary>歌词字体大小（sp）</summary>
    public int LyricFontSize { get; set; } = 20;

    /// <summary>歌词对齐方式：0=左，1=中，2=右</summary>
    public int LyricAlignment { get; set; } = 1;

    /// <summary>歌词样式：0=逐行，1=逐字</summary>
    public int LyricStyle { get; set; }

    /// <summary>对唱显示模式：0=标准，1=聚焦当前歌手，2=按角色分栏</summary>
    public int DuetMode { get; set; }

    /// <summary>歌词是否加粗</summary>
    public bool LyricBold { get; set; } = true;

    /// <summary>歌词高亮（已唱）颜色</summary>
    public Color LyricActiveColor { get; set; } = Color.White;

    /// <summary>歌词非高亮（未唱）颜色</summary>
    public Color LyricInactiveColor { get; set; } = Color.ParseColor("#CCBBBBBB");

    /// <summary>歌词颜色模式：0=自适应，1=自定义</summary>
    public int LyricColorMode { get; set; }

    /// <summary>当前背景亮度（0~1），用于透明遮罩下的歌词自适应</summary>
    public float CurrentBgLuminance { get; set; } = 0.3f;

    /// <summary>背景遮罩 View，用于自适应亮度计算（由外部 Fragment 注入）</summary>
    public View? BgDimOverlay { get; set; }

    /// <summary>是否启用自动滚动（播放页设 false）</summary>
    public bool EnableScroll { get; set; } = true;

    private bool _enableDragSeek = true;
    /// <summary>是否启用拖拽跳转（播放页设 false）</summary>
    public bool EnableDragSeek
    {
        get => _enableDragSeek;
        set
        {
            _enableDragSeek = value;
            if (_scrollView != null)
                _scrollView.SetOnTouchListener(value ? new DragTouchListener(this) : null);
        }
    }

    /// <summary>是否创建上下淡出遮罩（默认 false）</summary>
    public bool EnableFadeMasks { get; set; }

    // ── 回调属性 ──

    private Action? _onClickCallback;
    private bool _clickWired;
    /// <summary>点击回调（播放页跳转全屏页）</summary>
    public Action? OnClickCallback
    {
        get => _onClickCallback;
        set
        {
            _onClickCallback = value;
            if (_scrollView == null) return;
            if (value != null && !_clickWired)
            {
                _scrollView.Click += OnScrollViewClick;
                _clickWired = true;
            }
            _scrollView.Clickable = value != null;
        }
    }

    /// <summary>拖拽跳转回调，参数为目标时间戳</summary>
    public Action<TimeSpan>? OnDragSeek { get; set; }

    // ── 数据属性 ──

    /// <summary>绑定的播放 ViewModel</summary>
    public NowPlayingViewModel? ViewModel { get; set; }

    /// <summary>歌词设置 SharedPreferences（lyric_settings）</summary>
    public ISharedPreferences? Prefs { get; set; }

    // ── 公开方法 ──

    /// <summary>初始化：绑定 ViewModel/prefs，加载设置，挂载滚动/布局监听</summary>
    public void Init(NowPlayingViewModel viewModel, ISharedPreferences? prefs)
    {
        ViewModel = viewModel;
        Prefs = prefs;
        LoadSettings();
        SetupScrollListener();
        _scrollView.ViewTreeObserver.AddOnGlobalLayoutListener(new OnGlobalLayoutListener(this));
    }

    /// <summary>从 SharedPreferences 读取字号/颜色/对齐等设置</summary>
    public void LoadSettings()
    {
        if (Prefs == null) return;
        EnableDragSeek = Prefs.GetBoolean("allow_drag_seek", true);
        LyricFontSize = Prefs.GetInt("lyric_font_size", 20);
        LyricAlignment = Prefs.GetInt("lyric_alignment", 1);
        _lyricBgColorIndex = Prefs.GetInt("lyric_bg_color", 0);
        LyricBold = Prefs.GetBoolean("lyric_bold", true);

        // 读取 ARGB 颜色值（向后兼容旧版索引存储）
        if (Prefs.Contains("lyric_active_argb"))
        {
            _lyricActiveArgb = Prefs.GetInt("lyric_active_argb", unchecked((int)0xFFFFFFFF));
        }
        else
        {
            var activeIdx = Prefs.GetInt("lyric_active_color", 0);
            _lyricActiveArgb = (int)Color.ParseColor(PresetColorHex[Math.Clamp(activeIdx, 0, PresetColorHex.Length - 1)]).ToArgb();
        }
        if (Prefs.Contains("lyric_inactive_argb"))
        {
            _lyricInactiveArgb = Prefs.GetInt("lyric_inactive_argb", unchecked((int)0xCCBBBBBB));
        }
        else
        {
            var inactiveIdx = Prefs.GetInt("lyric_inactive_color", 0);
            _lyricInactiveArgb = (int)Color.ParseColor(InactivePresetHex[Math.Clamp(inactiveIdx, 0, InactivePresetHex.Length - 1)]).ToArgb();
        }

        LyricActiveColor = new Color(_lyricActiveArgb);
        LyricInactiveColor = new Color(_lyricInactiveArgb);
        LyricColorMode = Prefs.GetInt("lyric_color_mode", 0);
        DuetMode = Prefs.GetInt("duet_mode", 0);

        var catclawPrefs = Context?.GetSharedPreferences("catclaw_prefs", FileCreationMode.Private);
        LyricStyle = catclawPrefs?.GetInt("lyric_style", 0) ?? 0;
    }

    /// <summary>重建歌词视图：清空容器并按当前歌词重新创建所有行</summary>
    public void RebuildLyrics()
    {
        _lyricsContainer.RemoveAllViews();
        _lyricViews.Clear();
        _translationViews.Clear();
        _lastLyricIndex = -1;
        _lastDuetPartnerIndex = -1;

        // 自适应模式：根据背景自动设置歌词颜色
        if (LyricColorMode == 0)
            ApplyAdaptiveColors();

        var lyrics = ViewModel?.CurrentLyrics;
        if (lyrics?.Lines == null || lyrics.Lines.Count == 0)
        {
            // 显示"暂无歌词"
            var empty = new TextView(Context) { Text = "暂无歌词" };
            empty.SetTextSize(ComplexUnitType.Sp, LyricFontSize);
            empty.SetTextColor(new Color(UiHelper.ResolveThemeColor(Context!, Resource.Attribute.catClawTextHint, Color.ParseColor("#B0A8BA").ToArgb())));
            empty.Gravity = GetLyricGravity();
            var lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
            lp.TopMargin = 80;
            empty.LayoutParameters = lp;
            _lyricsContainer.AddView(empty);
            return;
        }

        // 分栏模式：构建角色→侧边映射
        _roleSideMap = BuildRoleSideMap(lyrics.Lines);

        // 为每一行歌词创建视图
        for (int i = 0; i < lyrics.Lines.Count; i++)
        {
            var line = lyrics.Lines[i];
            var item = CreateLyricViewItem(line, lyrics.HasPerLineAlignment, i);
            _lyricsContainer.AddView(item.Container);
            _lyricViews.Add(item);
        }

        HighlightCurrentLine();
        UpdateBgOverlay();
    }

    /// <summary>高亮当前播放的歌词行（含合唱伙伴行同时着色）</summary>
    public void HighlightCurrentLine()
    {
        var idx = ViewModel?.CurrentLyricIndex ?? -1;
        var partnerIdx = ViewModel?.DuetPartnerIndex ?? -1;

        // 自适应模式：根据背景自动设置歌词颜色
        if (LyricColorMode == 0)
            ApplyAdaptiveColors();

        // 聚焦当前歌手模式走独立分支
        if (DuetMode == 1)
        {
            ApplyFocusModeHighlight(idx);
            _lastLyricIndex = idx;
            _lastDuetPartnerIndex = partnerIdx;
            if (!_userScrolling && !_isDragging)
                ScrollToCurrentLyric();
            return;
        }

        // 只在当前行和合唱伙伴都未变化时跳过
        if (idx == _lastLyricIndex && partnerIdx == _lastDuetPartnerIndex) return;

        // 重置上一行（当前行）
        if (_lastLyricIndex >= 0 && _lastLyricIndex < _lyricViews.Count)
        {
            ResetLineHighlight(_lastLyricIndex);
        }

        // 重置上一轮的合唱伙伴行
        if (_lastDuetPartnerIndex >= 0 && _lastDuetPartnerIndex < _lyricViews.Count
            && _lastDuetPartnerIndex != idx && _lastDuetPartnerIndex != _lastLyricIndex)
        {
            ResetLineHighlight(_lastDuetPartnerIndex);
        }

        // 设置当前行高亮
        if (idx >= 0 && idx < _lyricViews.Count)
        {
            HighlightLine(idx, LyricFontSize + 4, LyricActiveColor, ViewModel?.CurrentLyricProgress ?? 0f);
        }

        // 设置合唱伙伴行高亮（同时着色，使用稍小字号区分）
        if (partnerIdx >= 0 && partnerIdx < _lyricViews.Count && partnerIdx != idx)
        {
            HighlightLine(partnerIdx, LyricFontSize + 2, LyricActiveColor, ViewModel?.DuetPartnerProgress ?? 0f);
        }

        _lastLyricIndex = idx;
        _lastDuetPartnerIndex = partnerIdx;

        if (!_userScrolling && !_isDragging)
            ScrollToCurrentLyric();
    }

    /// <summary>更新当前行和合唱伙伴的逐字渐变进度</summary>
    public void UpdateCurrentLineGradient()
    {
        if (ViewModel == null) return;
        var idx = ViewModel.CurrentLyricIndex;
        if (idx >= 0 && idx < _lyricViews.Count)
        {
            var item = _lyricViews[idx];
            item.Primary.LyricProgress = ViewModel.CurrentLyricProgress;
            if (item.Secondary != null)
                item.Secondary.LyricProgress = ViewModel.CurrentLyricProgress;
        }

        // 同时更新合唱伙伴行的渐变进度
        var partnerIdx = ViewModel.DuetPartnerIndex;
        if (partnerIdx >= 0 && partnerIdx < _lyricViews.Count && partnerIdx != idx)
        {
            var partnerItem = _lyricViews[partnerIdx];
            partnerItem.Primary.LyricProgress = ViewModel.DuetPartnerProgress;
            if (partnerItem.Secondary != null)
                partnerItem.Secondary.LyricProgress = ViewModel.DuetPartnerProgress;
        }
    }

    /// <summary>滚动到当前播放的歌词位置（居中）</summary>
    /// <param name="instant">true=立即跳转（seek 后用），false=平滑滚动（正常播放用）</param>
    public void ScrollToCurrentLyric(bool instant = false)
    {
        if (!EnableScroll) return;
        if (_scrollView == null || ViewModel == null) return;

        var idx = ViewModel.CurrentLyricIndex;
        if (idx < 0 || idx >= _lyricViews.Count) return;

        var item = _lyricViews[idx];
        if (item?.Container == null) return;
        var wrapper = item.Container;

        Post(() =>
        {
            try
            {
                var scrollViewHeight = _scrollView.Height;
                if (scrollViewHeight <= 0) return;

                var lyricCenterY = wrapper.Top + wrapper.Height / 2;
                // 计算目标滚动位置，使歌词中心与ScrollView中心对齐
                var targetScrollY = lyricCenterY - scrollViewHeight / 2;
                if (instant)
                    _scrollView.ScrollTo(0, Math.Max(0, targetScrollY));
                else
                    _scrollView.SmoothScrollTo(0, Math.Max(0, targetScrollY));
            }
            catch (System.Exception)
            {
                // 捕获异常，避免崩溃
            }
        });
    }

    /// <summary>设置容器上下 padding 为 ScrollView 高度一半，使歌词可居中滚动</summary>
    public void UpdateLyricsContainerPadding()
    {
        if (_scrollView == null || _lyricsContainer == null) return;
        var scrollViewHeight = _scrollView.Height;
        if (scrollViewHeight <= 0) return;
        var padding = scrollViewHeight / 2;
        _lyricsContainer.SetPadding(
            _lyricsContainer.PaddingLeft,
            padding,
            _lyricsContainer.PaddingRight,
            padding
        );
    }

    /// <summary>刷新所有歌词视图的颜色（不重置进度）</summary>
    public void RefreshLyricColors()
    {
        var idx = _lastLyricIndex;
        foreach (var item in _lyricViews)
        {
            ResetLyricItemColors(item);
        }

        // 当前行使用活跃色
        if (idx >= 0 && idx < _lyricViews.Count)
        {
            HighlightLyricItem(_lyricViews[idx], LyricActiveColor, true);
        }

        // 更新译文颜色
        UpdateTranslationColors();
    }

    /// <summary>更新已有歌词视图的对齐方式（不重建）</summary>
    public void RefreshLyricAlignment()
    {
        var gravity = GetLyricGravity();
        var lyrics = ViewModel?.CurrentLyrics;
        var hasPerLine = lyrics?.HasPerLineAlignment == true;
        var density = Context?.Resources?.DisplayMetrics?.Density ?? 1f;
        var edgePadding = (int)(12 * density);
        var centerPadding = (int)(16 * density);
        var innerPadding = (int)(8 * density);

        for (int i = 0; i < _lyricViews.Count; i++)
        {
            var item = _lyricViews[i];
            var lineLayout = item.Container as LinearLayout;
            if (lineLayout == null) continue;

            var line = lyrics!.Lines[i];
            var lineGravity = hasPerLine ? GetGravityFromAlignment(line.Alignment) : gravity;
            lineLayout.SetGravity(lineGravity);
            item.Primary.Gravity = item.Secondary == null ? lineGravity : GravityFlags.Start | GravityFlags.CenterVertical;
            if (item.Secondary != null)
                item.Secondary.Gravity = GravityFlags.End | GravityFlags.CenterVertical;

            if (hasPerLine)
            {
                if (line.Alignment == 0)
                    lineLayout.SetPadding(edgePadding, 0, innerPadding, 0);
                else if (line.Alignment == 2)
                    lineLayout.SetPadding(innerPadding, 0, edgePadding, 0);
                else
                    lineLayout.SetPadding(centerPadding, 0, centerPadding, 0);
            }
            else
            {
                lineLayout.SetPadding(0, 0, 0, 0);
            }
        }

        if (GetEmptyLyricView() is { } emptyView)
            emptyView.Gravity = gravity;
    }

    /// <summary>根据遮罩背景的实际颜色计算自适应歌词颜色（深色遮罩用白字，浅色遮罩用黑字）</summary>
    public void ApplyAdaptiveColors()
    {
        // 透明/半透明遮罩用封面亮度，不透明遮罩用遮罩自身亮度
        var overlayAlpha = 0f;
        if (BgDimOverlay?.Background is ColorDrawable cd)
            overlayAlpha = cd.Color.A / 255f;
        float lum = overlayAlpha < 0.5f ? CurrentBgLuminance : GetBgOverlayLuminance();

        if (lum >= 0.5f)
        {
            LyricActiveColor = Color.Black;
            LyricInactiveColor = Color.ParseColor("#AA333333");
        }
        else
        {
            LyricActiveColor = Color.White;
            LyricInactiveColor = Color.ParseColor("#EEBBBBBB");
        }

        // 同步更新译文颜色
        UpdateTranslationColors();
    }

    /// <summary>设置当前背景亮度（外部封面亮度变化时调用）</summary>
    public void SetBgLuminance(float luminance)
    {
        CurrentBgLuminance = luminance;
    }

    /// <summary>强制滚动到当前歌词（Tab 切换/seek 后用，忽略用户滚动状态，立即跳转）</summary>
    public void ForceScrollToCurrent()
    {
        _userScrolling = false;
        _isDragging = false;
        _draggedLyricIndex = -1;
        _scrollResumeHandler.RemoveCallbacksAndMessages(null);
        _dragResumeHandler.RemoveCallbacksAndMessages(null);
        ScrollToCurrentLyric(instant: true);
    }

    // ── 私有实现方法（逻辑与 FullLyricsFragment 保持一致） ──

    /// <summary>获取背景遮罩的实际亮度</summary>
    private float GetBgOverlayLuminance()
    {
        if (BgDimOverlay?.Background is ColorDrawable cd)
        {
            var c = cd.Color;
            return (0.299f * c.R + 0.587f * c.G + 0.114f * c.B) / 255f;
        }
        return 0.1f; // 默认深色
    }

    /// <summary>根据当前活跃/非活跃颜色更新所有译文的字体颜色</summary>
    private void UpdateTranslationColors()
    {
        // 译文色 = 非活跃色的更低透明度版本
        var transColor = new Color(
            (byte)(LyricInactiveColor.A * 3 / 4),
            LyricInactiveColor.R,
            LyricInactiveColor.G,
            LyricInactiveColor.B);

        for (int i = _translationViews.Count - 1; i >= 0; i--)
        {
            if (_translationViews[i].TryGetTarget(out var tv))
                tv.SetTextColor(transColor);
            else
                _translationViews.RemoveAt(i);
        }
    }

    /// <summary>
    /// 分栏模式下按角色出现频率分配侧边：最多的角色居左，第二多的居右，其余居中
    /// </summary>
    private Dictionary<string, int> BuildRoleSideMap(List<LrcLyricLine> lines)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (DuetMode != 2) return map;

        var roles = lines
            .Where(l => !string.IsNullOrEmpty(l.Role))
            .GroupBy(l => l.Role!)
            .Select(g => new { Role = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToList();

        if (roles.Count >= 1) map[roles[0].Role] = 0; // 左
        if (roles.Count >= 2) map[roles[1].Role] = 2; // 右
        // 其余角色默认居中（1）
        return map;
    }

    /// <summary>
    /// 创建一行歌词的视图项。对唱行会生成左右双文本布局，普通行保持单行。
    /// </summary>
    private LyricViewItem CreateLyricViewItem(LrcLyricLine line, bool hasPerLineAlignment, int index)
    {
        int effectiveAlignment;
        if (DuetMode == 2 && !string.IsNullOrEmpty(line.Role) && _roleSideMap.TryGetValue(line.Role, out var side))
            effectiveAlignment = side;
        else if (hasPerLineAlignment)
            effectiveAlignment = line.Alignment;
        else
            effectiveAlignment = LyricAlignment;

        var lineGravity = GetGravityFromAlignment(effectiveAlignment);

        var lineLayout = new LinearLayout(Context) { Orientation = Orientation.Vertical };
        lineLayout.SetGravity(lineGravity);
        lineLayout.Tag = index;

        // 对唱歌词左/右对齐时添加不对称 padding，使演唱1靠左、演唱2靠右
        if (DuetMode == 2 || hasPerLineAlignment)
        {
            int edgePadding = (int)(12 * Context!.Resources!.DisplayMetrics!.Density);
            int centerPadding = (int)(16 * Context.Resources.DisplayMetrics.Density);
            int innerPadding = (int)(8 * Context.Resources.DisplayMetrics.Density);
            if (effectiveAlignment == 0)
                lineLayout.SetPadding(edgePadding, 0, innerPadding, 0);
            else if (effectiveAlignment == 2)
                lineLayout.SetPadding(innerPadding, 0, edgePadding, 0);
            else
                lineLayout.SetPadding(centerPadding, 0, centerPadding, 0);
        }

        StrokeTextView primaryTv;
        StrokeTextView? secondaryTv = null;

        if (string.IsNullOrEmpty(line.SecondaryText))
        {
            primaryTv = CreateLyricTextView(line.Text, lineGravity, line.IsBackingVocal);
            lineLayout.AddView(primaryTv);
        }
        else
        {
            // 对唱行：左右分栏
            var duetRow = new LinearLayout(Context) { Orientation = Orientation.Horizontal };
            duetRow.SetGravity(GravityFlags.CenterVertical);
            duetRow.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);

            primaryTv = CreateLyricTextView(line.Text, GravityFlags.Start | GravityFlags.CenterVertical, line.IsBackingVocal);
            secondaryTv = CreateLyricTextView(line.SecondaryText, GravityFlags.End | GravityFlags.CenterVertical, false);

            var leftLp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 };
            var rightLp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 };
            primaryTv.LayoutParameters = leftLp;
            secondaryTv.LayoutParameters = rightLp;

            duetRow.AddView(primaryTv);
            duetRow.AddView(secondaryTv);
            lineLayout.AddView(duetRow);
        }

        TextView? transTv = null;
        if (!string.IsNullOrEmpty(line.Translation))
        {
            var transColor = new Color(
                (byte)(LyricInactiveColor.A * 3 / 4),
                LyricInactiveColor.R,
                LyricInactiveColor.G,
                LyricInactiveColor.B);
            transTv = new TextView(Context) { Text = line.Translation };
            transTv.SetTextSize(ComplexUnitType.Sp, LyricFontSize - 2);
            transTv.SetTextColor(transColor);
            transTv.SetTypeface(null, TypefaceStyle.Normal);
            transTv.Gravity = lineGravity;
            transTv.SetLineSpacing(0, 1.3f);
            var transLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
            transLp.TopMargin = 4;
            transTv.LayoutParameters = transLp;
            lineLayout.AddView(transTv);
            _translationViews.Add(new WeakReference<TextView>(transTv));
        }

        var lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        // 增大歌词行间距：首行无额外间距，后续行按字体大小留出呼吸感
        lp.TopMargin = index > 0 ? (int)(Context!.Resources!.DisplayMetrics!.Density * (LyricFontSize >= 24 ? 32 : 24)) : 0;
        lp.BottomMargin = 0;
        lineLayout.LayoutParameters = lp;

        return new LyricViewItem
        {
            Container = lineLayout,
            Primary = primaryTv,
            Secondary = secondaryTv,
            Translation = transTv
        };
    }

    /// <summary>创建一行歌词的主/次文本 StrokeTextView</summary>
    private StrokeTextView CreateLyricTextView(string text, GravityFlags gravity, bool isBackingVocal)
    {
        var tv = new StrokeTextView(Context) { Text = text };
        tv.SetTextSize(ComplexUnitType.Sp, isBackingVocal ? LyricFontSize - 3 : LyricFontSize);
        tv.SetTypeface(null, LyricBold ? TypefaceStyle.Bold : TypefaceStyle.Normal);
        tv.Gravity = gravity;
        tv.SetLineSpacing(0, 1.4f);
        tv.StrokeEnabled = false;
        tv.UnsungColor = LyricInactiveColor;
        tv.SungColor = LyricActiveColor;
        tv.SetTextColor(LyricInactiveColor);
        return tv;
    }

    /// <summary>更新背景遮罩颜色（由 RebuildLyrics 调用，保持与原实现一致）</summary>
    private void UpdateBgOverlay()
    {
        if (BgDimOverlay == null) return;
        var hex = BgColorHex[Math.Clamp(_lyricBgColorIndex, 0, BgColorHex.Length - 1)];
        var color = Color.ParseColor(hex);
        BgDimOverlay.SetBackgroundColor(color);
        // 同步更新上下歌词淡出遮罩，使其与背景遮罩颜色一致
        UpdateFadeMasks(color);
        // 遮罩变化后重新计算自适应歌词颜色
        if (LyricColorMode == 0)
            ApplyAdaptiveColors();
        // 刷新所有歌词视图的颜色
        RefreshLyricColors();
    }

    /// <summary>根据当前背景遮罩颜色更新歌词上下边缘淡出遮罩</summary>
    private void UpdateFadeMasks(Color baseColor)
    {
        // 已禁用上下边缘模糊淡出效果
        if (_fadeTop == null || _fadeBottom == null) return;
        _fadeTop.Visibility = ViewStates.Gone;
        _fadeBottom.Visibility = ViewStates.Gone;
    }

    /// <summary>重置单个歌词项为非高亮颜色</summary>
    private void ResetLyricItemColors(LyricViewItem item)
    {
        item.Primary.SetTextColor(LyricInactiveColor);
        item.Primary.UnsungColor = LyricInactiveColor;
        item.Primary.SungColor = LyricActiveColor;
        item.Secondary?.SetTextColor(LyricInactiveColor);
        item.Secondary?.SetTypeface(null, LyricBold ? TypefaceStyle.Bold : TypefaceStyle.Normal);

        if (item.Translation != null)
        {
            var transNormalColor = new Color(
                (byte)(LyricInactiveColor.A * 3 / 4),
                LyricInactiveColor.R,
                LyricInactiveColor.G,
                LyricInactiveColor.B);
            item.Translation.SetTextColor(transNormalColor);
        }
    }

    /// <summary>高亮单个歌词项</summary>
    private void HighlightLyricItem(LyricViewItem item, Color activeColor, bool includeTranslation)
    {
        item.Primary.SetTextColor(activeColor);
        item.Primary.UnsungColor = LyricInactiveColor;
        item.Primary.SungColor = activeColor;
        item.Secondary?.SetTextColor(activeColor);

        if (includeTranslation && item.Translation != null)
        {
            var transHighlightColor = new Color(
                (byte)Math.Min(activeColor.A + 40, 255),
                activeColor.R,
                activeColor.G,
                activeColor.B);
            item.Translation.SetTextColor(transHighlightColor);
        }
    }

    /// <summary>获取空歌词提示视图（暂无歌词）</summary>
    private TextView? GetEmptyLyricView()
        => _lyricViews.Count == 0 && _lyricsContainer.ChildCount > 0
            ? _lyricsContainer.GetChildAt(0) as TextView
            : null;

    /// <summary>获取歌词对齐方式的 GravityFlags</summary>
    private GravityFlags GetLyricGravity()
    {
        return LyricAlignment switch
        {
            0 => GravityFlags.Start | GravityFlags.CenterVertical,    // 左对齐
            2 => GravityFlags.End | GravityFlags.CenterVertical,      // 右对齐
            _ => GravityFlags.Center                                  // 居中（默认）
        };
    }

    /// <summary>根据指定对齐值获取 GravityFlags（用于逐行对齐）</summary>
    private static GravityFlags GetGravityFromAlignment(int alignment)
    {
        return alignment switch
        {
            0 => GravityFlags.Start | GravityFlags.CenterVertical,    // 左对齐
            2 => GravityFlags.End | GravityFlags.CenterVertical,      // 右对齐
            _ => GravityFlags.Center                                  // 居中（默认）
        };
    }

    /// <summary>
    /// 获取当前时间点上正在演唱的角色集合（包括当前行和与之时间重叠的其他角色）
    /// </summary>
    private HashSet<string?> GetCurrentActiveRoles(int currentIdx)
    {
        var activeRoles = new HashSet<string?>();
        var lines = ViewModel?.CurrentLyrics?.Lines;
        if (lines == null || currentIdx < 0 || currentIdx >= lines.Count) return activeRoles;

        var currentLine = lines[currentIdx];
        var currentStart = currentLine.Timestamp;
        var currentEnd = currentIdx + 1 < lines.Count
            ? lines[currentIdx + 1].Timestamp
            : currentStart + TimeSpan.FromSeconds(5);

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var start = line.Timestamp;
            var end = i + 1 < lines.Count ? lines[i + 1].Timestamp : start + TimeSpan.FromSeconds(5);
            if (start < currentEnd && currentStart < end)
                activeRoles.Add(line.Role);
        }
        return activeRoles;
    }

    /// <summary>聚焦当前歌手模式：当前角色正常高亮，其他角色变淡/缩小</summary>
    private void ApplyFocusModeHighlight(int currentIdx)
    {
        var activeRoles = GetCurrentActiveRoles(currentIdx);
        var dimColor = new Color(
            (byte)(LyricInactiveColor.A / 2),
            LyricInactiveColor.R,
            LyricInactiveColor.G,
            LyricInactiveColor.B);

        for (int i = 0; i < _lyricViews.Count; i++)
        {
            var item = _lyricViews[i];
            var line = ViewModel!.CurrentLyrics!.Lines[i];
            var isActive = activeRoles.Contains(line.Role);

            if (i == currentIdx)
            {
                HighlightLine(i, LyricFontSize + 4, LyricActiveColor, ViewModel.CurrentLyricProgress);
            }
            else if (isActive)
            {
                ResetLineHighlight(i);
                item.Primary.SetTextSize(ComplexUnitType.Sp, LyricFontSize);
            }
            else
            {
                ResetLineHighlight(i);
                item.Primary.SetTextColor(dimColor);
                item.Primary.SetTextSize(ComplexUnitType.Sp, LyricFontSize - 3);
                item.Secondary?.SetTextColor(dimColor);
                item.Secondary?.SetTextSize(ComplexUnitType.Sp, LyricFontSize - 3);
            }
        }
    }

    /// <summary>将指定行重置为非高亮状态</summary>
    private void ResetLineHighlight(int lineIdx)
    {
        if (lineIdx < 0 || lineIdx >= _lyricViews.Count) return;
        var item = _lyricViews[lineIdx];
        var isBacking = ViewModel?.CurrentLyrics?.Lines[lineIdx].IsBackingVocal == true;

        item.Primary.SetTextSize(ComplexUnitType.Sp, isBacking ? LyricFontSize - 3 : LyricFontSize);
        item.Primary.SetTextColor(LyricInactiveColor);
        item.Primary.SetTypeface(null, LyricBold ? TypefaceStyle.Bold : TypefaceStyle.Normal);
        item.Primary.ResetLyricProgress();
        item.Primary.UnsungColor = LyricInactiveColor;
        item.Primary.SungColor = LyricActiveColor;
        item.Primary.StrokeEnabled = false;

        if (item.Secondary != null)
        {
            item.Secondary.SetTextSize(ComplexUnitType.Sp, LyricFontSize);
            item.Secondary.SetTextColor(LyricInactiveColor);
            item.Secondary.SetTypeface(null, LyricBold ? TypefaceStyle.Bold : TypefaceStyle.Normal);
            item.Secondary.ResetLyricProgress();
            item.Secondary.UnsungColor = LyricInactiveColor;
            item.Secondary.SungColor = LyricActiveColor;
            item.Secondary.StrokeEnabled = false;
        }

        // 重置译文颜色
        if (item.Translation != null)
        {
            item.Translation.SetTextSize(ComplexUnitType.Sp, LyricFontSize - 2);
            var transNormalColor = new Color(
                (byte)(LyricInactiveColor.A * 3 / 4),
                LyricInactiveColor.R,
                LyricInactiveColor.G,
                LyricInactiveColor.B);
            item.Translation.SetTextColor(transNormalColor);
        }
    }

    /// <summary>将指定行设置高亮状态（含渐变进度）</summary>
    private void HighlightLine(int lineIdx, float fontSize, Color activeColor, float progress)
    {
        if (lineIdx < 0 || lineIdx >= _lyricViews.Count) return;
        var item = _lyricViews[lineIdx];

        item.Primary.SetTextSize(ComplexUnitType.Sp, fontSize);
        item.Primary.SetTextColor(activeColor);
        item.Primary.UnsungColor = LyricInactiveColor;
        item.Primary.SungColor = activeColor;
        if (LyricStyle == 1)
        {
            item.Primary.StrokeEnabled = false;
            item.Primary.LyricProgress = progress;
        }

        if (item.Secondary != null)
        {
            // 对唱行的对方文本使用稍小字号区分
            item.Secondary.SetTextSize(ComplexUnitType.Sp, fontSize - 2);
            item.Secondary.SetTextColor(activeColor);
            item.Secondary.UnsungColor = LyricInactiveColor;
            item.Secondary.SungColor = activeColor;
            if (LyricStyle == 1)
            {
                item.Secondary.StrokeEnabled = false;
                item.Secondary.LyricProgress = progress;
            }
        }

        // 高亮译文
        if (item.Translation != null)
        {
            item.Translation.SetTextSize(ComplexUnitType.Sp, LyricFontSize - 2);
            var transHighlightColor = new Color(
                (byte)Math.Min(activeColor.A + 40, 255),
                activeColor.R,
                activeColor.G,
                activeColor.B);
            item.Translation.SetTextColor(transHighlightColor);
        }
    }

    // ── 滚动/拖拽监听与手势处理 ──

    /// <summary>设置滚动监听器</summary>
    private void SetupScrollListener()
    {
        // 监听 ScrollView 滚动变化
        _scrollView.ViewTreeObserver.ScrollChanged += (s, e) =>
        {
            if (!_isDragging && !_userScrolling) return;

            if (_isDragging)
            {
                // 拖拽模式：更新拖拽选中的歌词索引
                UpdateDraggedLyricIndex();
            }
            else if (_userScrolling)
            {
                // 用户手动滚动：3秒后恢复自动滚动
                _scrollResumeHandler.RemoveCallbacksAndMessages(null);
                _scrollResumeHandler.PostDelayed(() =>
                {
                    _userScrolling = false;
                    ScrollToCurrentLyric();
                }, 3000);
            }
        };

        // 拖拽触摸监听器由 EnableDragSeek 属性 setter 管理
        if (EnableDragSeek)
            _scrollView.SetOnTouchListener(new DragTouchListener(this));
    }

    /// <summary>更新拖拽时选中的歌词索引：计算当前屏幕中央位置对应的歌词</summary>
    private void UpdateDraggedLyricIndex()
    {
        if (_scrollView == null || _lyricViews.Count == 0) return;

        try
        {
            // 计算 ScrollView 当前可见区域的中心点 Y 坐标
            var scrollCenterY = _scrollView.ScrollY + _scrollView.Height / 2;
            int closestIndex = -1;
            int closestDistance = int.MaxValue;

            // 遍历所有歌词，找到离中心点最近的那个
            for (int i = 0; i < _lyricViews.Count; i++)
            {
                var lyricItem = _lyricViews[i];
                if (lyricItem?.Container == null) continue;

                var wrapper = lyricItem.Container;
                var lyricCenterY = wrapper.Top + wrapper.Height / 2;
                // 计算歌词中心到 ScrollView 中心的距离
                var distance = Math.Abs(scrollCenterY - lyricCenterY);

                // 记录距离最近的歌词索引
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestIndex = i;
                }
            }

            // 更新拖拽选中的索引
            if (closestIndex != _draggedLyricIndex && closestIndex >= 0)
            {
                _draggedLyricIndex = closestIndex;
            }
        }
        catch (System.Exception)
        {
            // 捕获异常，避免崩溃
        }
    }

    /// <summary>触摸开始回调</summary>
    private void OnTouchStart()
    {
        _userScrolling = true;
        _scrollResumeHandler.RemoveCallbacksAndMessages(null);
        _dragResumeHandler.RemoveCallbacksAndMessages(null);
    }

    /// <summary>拖拽中回调</summary>
    private void OnDragging()
    {
        // 如果不允许拖拽，直接返回
        if (!EnableDragSeek) return;

        // 首次检测到拖拽时
        if (!_isDragging)
        {
            _isDragging = true;
            _dragResumeHandler.RemoveCallbacksAndMessages(null);
        }
        // 更新拖拽选中的歌词
        UpdateDraggedLyricIndex();
    }

    /// <summary>触摸结束回调</summary>
    private void OnTouchEnd()
    {
        if (_isDragging)
        {
            // 拖拽结束：触发跳转回调
            var draggedIdx = _draggedLyricIndex;
            _isDragging = false;
            _draggedLyricIndex = -1;
            _dragResumeHandler.RemoveCallbacksAndMessages(null);

            if (OnDragSeek != null && draggedIdx >= 0)
            {
                var lyrics = ViewModel?.CurrentLyrics;
                if (lyrics?.Lines != null && draggedIdx < lyrics.Lines.Count)
                    OnDragSeek.Invoke(lyrics.Lines[draggedIdx].Timestamp);
            }
            // 拖拽结束后停留当前位置2秒，无其他操作再滚动回当前播放行
            _dragResumeHandler.PostDelayed(() =>
            {
                _userScrolling = false;
                ScrollToCurrentLyric();
            }, 2000);
        }
        else
        {
            // 普通滚动：3秒后恢复自动滚动
            _scrollResumeHandler.RemoveCallbacksAndMessages(null);
            _scrollResumeHandler.PostDelayed(() =>
            {
                _userScrolling = false;
                ScrollToCurrentLyric();
            }, 3000);
        }
    }

    /// <summary>ScrollView 点击处理</summary>
    private void OnScrollViewClick(object? sender, EventArgs e)
        => _onClickCallback?.Invoke();

    /// <summary>视图从窗口分离时清理 Handler 回调，避免内存泄漏</summary>
    protected override void OnDetachedFromWindow()
    {
        _scrollResumeHandler.RemoveCallbacksAndMessages(null);
        _dragResumeHandler.RemoveCallbacksAndMessages(null);
        base.OnDetachedFromWindow();
    }

    // ── 嵌套类 ──

    /// <summary>
    /// 布局监听器，用于在 ScrollView 首次布局完成后设置歌词容器的顶部 padding
    /// </summary>
    private class OnGlobalLayoutListener : Java.Lang.Object, ViewTreeObserver.IOnGlobalLayoutListener
    {
        private readonly LyricRendererView _owner;

        public OnGlobalLayoutListener(LyricRendererView owner)
        {
            _owner = owner;
        }

        public void OnGlobalLayout()
        {
            try
            {
                _owner._scrollView.ViewTreeObserver.RemoveOnGlobalLayoutListener(this);
                _owner.UpdateLyricsContainerPadding();
                // 只更新歌词高亮，不完整重建，避免递归
                _owner.HighlightCurrentLine();
            }
            catch (System.Exception)
            {
                // 捕获异常，避免崩溃
            }
        }
    }

    /// <summary>
    /// 拖拽触摸监听器：检测用户的拖拽手势并回调宿主视图
    /// </summary>
    private class DragTouchListener : Java.Lang.Object, View.IOnTouchListener
    {
        private readonly LyricRendererView _owner;
        private float _startY = 0;
        private bool _hasDragged = false;

        public DragTouchListener(LyricRendererView owner)
        {
            _owner = owner;
        }

        public bool OnTouch(View? v, MotionEvent? e)
        {
            if (e == null) return false;

            switch (e.Action)
            {
                case MotionEventActions.Down:
                    // 按下时记录起始位置
                    _owner.OnTouchStart();
                    _startY = e.GetY();
                    _hasDragged = false;
                    break;

                case MotionEventActions.Move:
                    // 移动时检测是否超过拖拽阈值
                    var dy = Math.Abs(e.GetY() - _startY);
                    if (!_hasDragged && dy > 20)
                    {
                        _hasDragged = true;
                        _owner.OnDragging();
                    }
                    else if (_hasDragged)
                    {
                        _owner.OnDragging();
                    }
                    break;

                case MotionEventActions.Up:
                case MotionEventActions.Cancel:
                    // 触摸结束时
                    _owner.OnTouchEnd();
                    break;
            }
            return false;
        }
    }

    /// <summary>
    /// 歌词行视图项：包含主文本、对唱对方文本、译文及容器，便于统一高亮处理。
    /// </summary>
    public class LyricViewItem
    {
        public View Container { get; set; } = null!;
        public StrokeTextView Primary { get; set; } = null!;
        public StrokeTextView? Secondary { get; set; }
        public TextView? Translation { get; set; }
    }
}
