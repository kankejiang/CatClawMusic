using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using Android.Widget;
using CatClawMusic.UI.Helpers;
using CatClawMusic.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

/// <summary>
/// 全屏歌词页面Fragment
/// 功能：显示歌词、高亮当前行、支持拖拽调整播放位置
/// 歌词渲染逻辑由 <see cref="LyricRendererView"/> 封装，本类仅负责背景、设置对话框等特有 UI。
/// </summary>
public class FullLyricsFragment : Fragment
{
    // 颜色预设
    // 快速预设色（用于取色盘下方的快捷按钮）
    private static readonly string[] PresetColorNames = { "白色", "黑色", "黄色", "薄荷绿", "粉色", "天蓝", "橙色", "珊瑚", "薰衣草", "青色" };
    private static readonly string[] PresetColorHex = LyricConstants.PresetColorHex;
    private static readonly string[] InactivePresetNames = { "灰色", "深灰", "黑色", "浅灰", "蓝灰", "淡紫", "暖灰", "石板" };
    private static readonly string[] InactivePresetHex = LyricConstants.InactivePresetHex;
    private static readonly string[] BgColorNames = { "浅色", "深色", "透明" };
    private static readonly string[] BgColorHex = LyricConstants.FullScreenBgColorHex;

    // ViewModel
    private NowPlayingViewModel _viewModel = null!;
    // 背景封面ImageView
    private ImageView _bgCover = null!;
    // 顶部封面缩略图ImageView
    private ImageView _coverThumbnail = null!;
    // 歌词渲染视图（封装 ScrollView + LinearLayout + 高亮/滚动/拖拽逻辑）
    private LyricRendererView? _lyricRenderer;
    // 歌曲标题TextView
    private TextView _songTitle = null!;
    // 歌手TextView
    private TextView _songArtist = null!;
    // 播放进度TextView
    private TextView _progressText = null!;
    // 设置按钮ImageButton
    private ImageButton _btnSettings = null!;
    // 拖拽指示器容器
    private RelativeLayout _dragIndicator = null!;
    // 跳转按钮
    private Button _btnJump = null!;
    // 上一次的封面路径
    private string? _lastCoverSource;

    // SharedPreferences用于保存用户设置
    private ISharedPreferences? _prefs;
    // 是否允许拖拽调整进度（Fragment 侧用于设置对话框状态）
    private bool _allowDragSeek;
    // 背景遮罩View
    private View? _bgDimOverlay;
    // 顶部/底部歌词淡出遮罩
    private View? _fadeTop;
    private View? _fadeBottom;
    // 拖拽跳转确认超时 Handler
    private readonly Handler _dragSeekTimeoutHandler = new(Looper.MainLooper!);

    /// <summary>
    /// 创建Fragment视图
    /// </summary>
    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
        => inflater.Inflate(Resource.Layout.fragment_full_lyrics, container, false)!;

    /// <summary>
    /// 视图创建完成后初始化
    /// </summary>
    public override void OnViewCreated(View view, Bundle? state)
    {
        base.OnViewCreated(view, state);
        // 获取ViewModel
        _viewModel = MainApplication.Services.GetRequiredService<NowPlayingViewModel>();
        // 初始化SharedPreferences
        _prefs = Activity?.GetSharedPreferences("lyric_settings", FileCreationMode.Private);

        // 初始化控件引用
        _bgCover = view.FindViewById<ImageView>(Resource.Id.lyric_bg_cover)!;
        _coverThumbnail = view.FindViewById<ImageView>(Resource.Id.lyric_cover_thumbnail)!;
        _lyricRenderer = view.FindViewById<LyricRendererView>(Resource.Id.lyric_renderer)!;
        _songTitle = view.FindViewById<TextView>(Resource.Id.lyric_song_title)!;
        _songArtist = view.FindViewById<TextView>(Resource.Id.lyric_song_artist)!;
        _progressText = view.FindViewById<TextView>(Resource.Id.lyric_progress_text)!;
        _btnSettings = view.FindViewById<ImageButton>(Resource.Id.btn_lyric_settings)!;
        _dragIndicator = view.FindViewById<RelativeLayout>(Resource.Id.drag_indicator)!;
        _btnJump = view.FindViewById<Button>(Resource.Id.btn_jump)!;
        _bgDimOverlay = view.FindViewById(Resource.Id.lyric_bg_dim);


        // 初始化歌词渲染视图
        _lyricRenderer.Init(_viewModel, _prefs);
        _lyricRenderer.LoadSettings();
        _lyricRenderer.BgDimOverlay = _bgDimOverlay;

        _lyricRenderer.OnDragSeek = pos =>
        {
            _viewModel.CurrentPositionSeconds = pos.TotalSeconds;
            // 确认跳转后隐藏指示器
            HideDragIndicator();
        };
        _lyricRenderer.OnDragSeekRequested = pos =>
        {
            // 拖拽结束立即显示跳转按钮，等待惯性滚动停止后再开始确认超时
            ShowDragIndicator();
        };
        _lyricRenderer.OnScrollStopped = () =>
        {
            // 滚动停止后，若仍有待确认的跳转，3 秒内点击按钮才执行
            if (_lyricRenderer?.PendingDragSeekTime == null) return;
            _dragSeekTimeoutHandler.RemoveCallbacksAndMessages(null);
            _dragSeekTimeoutHandler.PostDelayed(() =>
            {
                _lyricRenderer?.CancelDragSeek();
                HideDragIndicator();
            }, 3000);
        };
        _lyricRenderer.OnDragSeekCancelled = () =>
        {
            HideDragIndicator();
        };

        // 加载 Fragment 侧设置（_allowDragSeek 等）
        LoadSettings();

        // 应用毛玻璃模糊效果（仅背景封面）
        ApplyBlur();
        // 设置按钮点击事件
        _btnSettings.Click += (s, e) => ShowSettingsDialog();
        // 跳转按钮点击事件：2 秒内点击才执行跳转
        _btnJump.Click += (s, e) =>
        {
            _dragSeekTimeoutHandler.RemoveCallbacksAndMessages(null);
            _lyricRenderer?.PerformDragSeek();
        };

        var topBar = view.FindViewById<RelativeLayout>(Resource.Id.lyric_top_bar);
        if (topBar != null)
        {
            var origTop = topBar.PaddingTop;
            topBar.SetPadding(topBar.PaddingLeft, MainActivity.StatusBarHeight + (int)(16 * Resources?.DisplayMetrics?.Density ?? 1), topBar.PaddingRight, topBar.PaddingBottom);
        }

        // 绑定ViewModel
        BindViewModel();
        // 同步UI状态
        SyncUI();
    }

    /// <summary>
    /// 应用毛玻璃模糊效果
    /// 仅Android 12+支持
    /// </summary>
    private void ApplyBlur()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
            _bgCover.SetRenderEffect(RenderEffect.CreateBlurEffect(120f, 120f, Shader.TileMode.Clamp));
    }

    /// <summary>
    /// 更新背景封面与顶部缩略图
    /// </summary>
    private void UpdateBackground()
    {
        var cover = _viewModel.CoverSource;
        if (cover == _lastCoverSource) return;
        _lastCoverSource = cover;

        try
        {
            var oldDrawable = _bgCover.Drawable as Android.Graphics.Drawables.BitmapDrawable;
            if (oldDrawable?.Bitmap != null && oldDrawable.Bitmap.IsRecycled)
                _bgCover.SetImageResource(Resource.Drawable.cover_default);

            if (!string.IsNullOrEmpty(cover) && System.IO.File.Exists(cover))
            {
                var drawable = Drawable.CreateFromPath(cover);
                if (drawable != null)
                {
                    _bgCover.SetImageDrawable(drawable);
                    _coverThumbnail.SetImageDrawable(drawable);
                    // 从封面提取亮度，用于透明遮罩下的自适应歌词颜色
                    ComputeCoverLuminance(drawable);
                    return;
                }
            }
        }
        catch { }

        _bgCover.SetImageResource(Resource.Drawable.cover_default);
        _coverThumbnail.SetImageResource(Resource.Drawable.cover_default);
        _lyricRenderer?.SetBgLuminance(0.3f);
    }

    /// <summary>从封面 Drawable 提取平均亮度并同步到歌词渲染视图</summary>
    private void ComputeCoverLuminance(Android.Graphics.Drawables.Drawable drawable)
    {
        try
        {
            var bd = drawable as Android.Graphics.Drawables.BitmapDrawable;
            var bitmap = bd?.Bitmap;
            if (bitmap == null || bitmap.IsRecycled) { _lyricRenderer?.SetBgLuminance(0.3f); return; }

            // 缩放到 1×1 取平均色
            var scaled = Android.Graphics.Bitmap.CreateScaledBitmap(bitmap, 1, 1, false);
            if (scaled == null) { _lyricRenderer?.SetBgLuminance(0.3f); return; }

            var pixel = new int[1];
            scaled.GetPixels(pixel, 0, 1, 0, 0, 1, 1);
            var c = new Android.Graphics.Color(pixel[0]);
            var luminance = (0.299f * c.R + 0.587f * c.G + 0.114f * c.B) / 255f;
            _lyricRenderer?.SetBgLuminance(luminance);

            if (!ReferenceEquals(scaled, bitmap)) scaled.Recycle();
        }
        catch { _lyricRenderer?.SetBgLuminance(0.3f); }
    }

    /// <summary>
    /// 绑定ViewModel属性变化事件
    /// </summary>
    private void BindViewModel()
    {
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>
    /// 解绑ViewModel属性变化事件
    /// </summary>
    private void UnbindViewModel()
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
    {
        Activity?.RunOnUiThread(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(_viewModel.CurrentLyrics):
                    _lyricRenderer?.RebuildLyrics();
                    break;
                case nameof(_viewModel.CurrentLyricIndex):
                    _lyricRenderer?.HighlightCurrentLine();
                    UpdateCurrentSinger();
                    break;
                case nameof(_viewModel.CurrentPosition):
                    UpdateProgress();
                    break;
                case nameof(_viewModel.CurrentLyricProgress):
                    if (_lyricRenderer?.LyricStyle == 1)
                        _lyricRenderer?.UpdateCurrentLineGradient();
                    break;
                case nameof(_viewModel.DuetPartnerIndex):
                    _lyricRenderer?.HighlightCurrentLine();
                    UpdateCurrentSinger();
                    break;
                case nameof(_viewModel.DuetPartnerProgress):
                    if (_lyricRenderer?.LyricStyle == 1)
                        _lyricRenderer?.UpdateCurrentLineGradient();
                    break;
                case nameof(_viewModel.TotalDuration):
                    UpdateProgress();
                    break;
                case nameof(_viewModel.CurrentSong):
                    _songTitle.Text = _viewModel.CurrentSong?.Title ?? "";
                    UpdateCurrentSinger();
                    break;
                case nameof(_viewModel.CoverSource):
                    UpdateBackground();
                    break;
            }
        });
    }

    /// <summary>
    /// 同步UI状态
    /// </summary>
    private void SyncUI()
    {
        _songTitle.Text = _viewModel.CurrentSong?.Title ?? "";
        UpdateCurrentSinger();
        UpdateBackground();
        _lyricRenderer?.RebuildLyrics();
        UpdateProgress();
    }

    /// <summary>
    /// 从SharedPreferences加载用户设置
    /// </summary>
    private void LoadSettings()
    {
        if (_prefs == null) return;
        _allowDragSeek = _prefs.GetBoolean("allow_drag_seek", false);
        _lyricRenderer?.LoadSettings();
        if (_lyricRenderer != null)
            _lyricRenderer.EnableDragSeek = _allowDragSeek;
    }

    /// <summary>
    /// 保存用户设置到SharedPreferences
    /// </summary>
    private void SaveSettings()
    {
        if (_prefs == null || _lyricRenderer == null) return;
        var e = _prefs.Edit();
        e.PutBoolean("allow_drag_seek", _allowDragSeek);
        e.PutInt("lyric_font_size", _lyricRenderer.LyricFontSize);
        e.PutInt("lyric_alignment", _lyricRenderer.LyricAlignment);
        e.PutInt("lyric_active_argb", _lyricRenderer.LyricActiveColor.ToArgb());
        e.PutInt("lyric_inactive_argb", _lyricRenderer.LyricInactiveColor.ToArgb());
        e.PutInt("lyric_color_mode", _lyricRenderer.LyricColorMode);
        e.PutBoolean("lyric_bold", _lyricRenderer.LyricBold);
        e.PutInt("duet_mode", _lyricRenderer.DuetMode);
        e.Apply();
    }

    /// <summary>
    /// 更新背景遮罩颜色
    /// </summary>
    private void UpdateBgOverlay()
    {
        if (_bgDimOverlay == null) return;
        var bgIdx = _prefs?.GetInt("lyric_bg_color", 0) ?? 0;
        var hex = BgColorHex[Math.Clamp(bgIdx, 0, BgColorHex.Length - 1)];
        var color = Color.ParseColor(hex);
        _bgDimOverlay.SetBackgroundColor(color);
        // 同步更新上下歌词淡出遮罩，使其与背景遮罩颜色一致
        UpdateFadeMasks(color);
        // 同步遮罩引用并重新计算自适应歌词颜色
        if (_lyricRenderer != null)
        {
            _lyricRenderer.BgDimOverlay = _bgDimOverlay;
            _lyricRenderer.ApplyAdaptiveColors();
            _lyricRenderer.RefreshLyricColors();
        }
    }

    /// <summary>根据当前背景遮罩颜色更新歌词上下边缘淡出遮罩</summary>
    private void UpdateFadeMasks(Color baseColor)
    {
        // 已禁用上下边缘模糊淡出效果
        if (_fadeTop == null || _fadeBottom == null) return;
        _fadeTop.Visibility = ViewStates.Gone;
        _fadeBottom.Visibility = ViewStates.Gone;
    }

    /// <summary>
    /// 显示歌词设置对话框
    /// </summary>
    private void ShowSettingsDialog()
    {
        if (Context == null || Activity == null || _lyricRenderer == null) return;

        var dp = (int)Resources!.DisplayMetrics!.Density;
        var tv = new Android.Util.TypedValue();
        var themeColor = Activity.Theme?.ResolveAttribute(global::Android.Resource.Attribute.ColorPrimary, tv, true) == true
            ? new Color(tv.Data) : Color.ParseColor("#9B7ED8");

        var content = new LinearLayout(Context) { Orientation = Orientation.Vertical };
        content.SetPadding(dp * 14, dp * 8, dp * 14, dp * 8);

        var cbDragSeek = new CheckBox(Context) { Text = "允许拖拽调整进度", Checked = _allowDragSeek };
        cbDragSeek.SetTextColor(Color.ParseColor("#DDFFFFFF"));
        cbDragSeek.SetTextSize(Android.Util.ComplexUnitType.Sp, 13f);
        cbDragSeek.ButtonTintList = Android.Content.Res.ColorStateList.ValueOf(themeColor);
        content.AddView(cbDragSeek);

        var catclawPrefs = Activity.GetSharedPreferences("catclaw_prefs", FileCreationMode.Private);
        var currentLyricsMode = catclawPrefs?.GetInt("lyrics_mode", 3) ?? 3; // 默认自动选择
        var currentLyricStyle = catclawPrefs?.GetInt("lyric_style", 0) ?? 0;

        var styleLabel = new TextView(Context) { Text = "歌词样式" };
        styleLabel.SetTextColor(Color.ParseColor("#B0FFFFFF"));
        styleLabel.SetTextSize(Android.Util.ComplexUnitType.Sp, 12f);
        styleLabel.SetPadding(0, dp * 12, 0, dp * 4);
        content.AddView(styleLabel);

        var rgLyricStyle = new RadioGroup(Context) { Orientation = Orientation.Horizontal };
        var rbLineByLine = new RadioButton(Context) { Text = "逐行" };
        var rbWordByWord = new RadioButton(Context) { Text = "逐字" };
        rbLineByLine.SetTextColor(Color.ParseColor("#DDFFFFFF"));
        rbWordByWord.SetTextColor(Color.ParseColor("#DDFFFFFF"));
        rbLineByLine.ButtonTintList = Android.Content.Res.ColorStateList.ValueOf(themeColor);
        rbWordByWord.ButtonTintList = Android.Content.Res.ColorStateList.ValueOf(themeColor);
        rgLyricStyle.AddView(rbLineByLine);
        rgLyricStyle.AddView(rbWordByWord);
        rgLyricStyle.Check(currentLyricStyle == 1 ? rbWordByWord.Id : rbLineByLine.Id);
        content.AddView(rgLyricStyle);

        var modeLabel = new TextView(Context) { Text = "歌词模式" };
        modeLabel.SetTextColor(Color.ParseColor("#B0FFFFFF"));
        modeLabel.SetTextSize(Android.Util.ComplexUnitType.Sp, 12f);
        modeLabel.SetPadding(0, dp * 12, 0, dp * 4);
        content.AddView(modeLabel);

        var rgLyricsMode = new RadioGroup(Context) { Orientation = Orientation.Vertical };
        // UI 顺序：自动选择放最上；存储值保持 0=外挂,1=内嵌,2=关闭,3=自动
        var modeOptions = new[] { "自动选择", "外挂歌词（.lrc 文件优先）", "内嵌歌词（音频标签优先）", "关闭歌词" };
        for (int i = 0; i < modeOptions.Length; i++)
        {
            var rb = new RadioButton(Context) { Text = modeOptions[i] };
            rb.SetTextSize(Android.Util.ComplexUnitType.Sp, 13f);
            rb.SetTextColor(Color.ParseColor("#DDFFFFFF"));
            rb.ButtonTintList = Android.Content.Res.ColorStateList.ValueOf(themeColor);
            rb.SetPadding(0, dp * 4, 0, dp * 4);
            rgLyricsMode.AddView(rb);
        }
        var initialModeUiIndex = currentLyricsMode switch { 0 => 1, 1 => 2, 2 => 3, _ => 0 };
        var initialModeRb = rgLyricsMode.GetChildAt(initialModeUiIndex) as RadioButton;
        if (initialModeRb != null)
            rgLyricsMode.Check(initialModeRb.Id);
        content.AddView(rgLyricsMode);

        var fontLabel = new TextView(Context) { Text = "字体大小" };
        fontLabel.SetTextColor(Color.ParseColor("#B0FFFFFF"));
        fontLabel.SetTextSize(Android.Util.ComplexUnitType.Sp, 12f);
        fontLabel.SetPadding(0, dp * 12, 0, dp * 4);
        content.AddView(fontLabel);

        var fontRow = new LinearLayout(Context) { Orientation = Orientation.Horizontal };
        fontRow.SetGravity(GravityFlags.CenterVertical);
        var sbFontSize = new SeekBar(Context);
        sbFontSize.Max = 28;
        sbFontSize.Progress = _lyricRenderer.LyricFontSize;
        sbFontSize.LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 };
        var tvFontSizeValue = new TextView(Context) { Text = $"{_lyricRenderer.LyricFontSize}sp" };
        tvFontSizeValue.SetTextColor(Color.White);
        tvFontSizeValue.SetTextSize(Android.Util.ComplexUnitType.Sp, 13f);
        tvFontSizeValue.SetPadding(dp * 8, 0, 0, 0);
        fontRow.AddView(sbFontSize);
        fontRow.AddView(tvFontSizeValue);
        content.AddView(fontRow);

        var cbBold = new CheckBox(Context) { Text = "字体加粗", Checked = _lyricRenderer.LyricBold };
        cbBold.SetTextColor(Color.ParseColor("#DDFFFFFF"));
        cbBold.SetTextSize(Android.Util.ComplexUnitType.Sp, 13f);
        cbBold.ButtonTintList = Android.Content.Res.ColorStateList.ValueOf(themeColor);
        cbBold.SetPadding(0, dp * 6, 0, 0);
        content.AddView(cbBold);

        var duetLabel = new TextView(Context) { Text = "对唱显示模式" };
        duetLabel.SetTextColor(Color.ParseColor("#B0FFFFFF"));
        duetLabel.SetTextSize(Android.Util.ComplexUnitType.Sp, 12f);
        duetLabel.SetPadding(0, dp * 12, 0, dp * 4);
        content.AddView(duetLabel);

        var rgDuetMode = new RadioGroup(Context) { Orientation = Orientation.Vertical };
        var rbDuetStandard = new RadioButton(Context) { Text = "标准" };
        var rbDuetFocus = new RadioButton(Context) { Text = "聚焦当前歌手" };
        var rbDuetSplit = new RadioButton(Context) { Text = "按角色分栏" };
        rbDuetStandard.SetTextColor(Color.ParseColor("#DDFFFFFF"));
        rbDuetFocus.SetTextColor(Color.ParseColor("#DDFFFFFF"));
        rbDuetSplit.SetTextColor(Color.ParseColor("#DDFFFFFF"));
        rbDuetStandard.ButtonTintList = Android.Content.Res.ColorStateList.ValueOf(themeColor);
        rbDuetFocus.ButtonTintList = Android.Content.Res.ColorStateList.ValueOf(themeColor);
        rbDuetSplit.ButtonTintList = Android.Content.Res.ColorStateList.ValueOf(themeColor);
        rgDuetMode.AddView(rbDuetStandard);
        rgDuetMode.AddView(rbDuetFocus);
        rgDuetMode.AddView(rbDuetSplit);
        rgDuetMode.Check(_lyricRenderer.DuetMode switch { 1 => rbDuetFocus.Id, 2 => rbDuetSplit.Id, _ => rbDuetStandard.Id });
        content.AddView(rgDuetMode);

        var alignLabel = new TextView(Context) { Text = "对齐方式" };
        alignLabel.SetTextColor(Color.ParseColor("#B0FFFFFF"));
        alignLabel.SetTextSize(Android.Util.ComplexUnitType.Sp, 12f);
        alignLabel.SetPadding(0, dp * 12, 0, dp * 4);
        content.AddView(alignLabel);

        var rgAlignment = new RadioGroup(Context) { Orientation = Orientation.Horizontal };
        var rbLeft = new RadioButton(Context) { Text = "左" };
        var rbCenter = new RadioButton(Context) { Text = "中" };
        var rbRight = new RadioButton(Context) { Text = "右" };
        rbLeft.SetTextColor(Color.ParseColor("#DDFFFFFF"));
        rbCenter.SetTextColor(Color.ParseColor("#DDFFFFFF"));
        rbRight.SetTextColor(Color.ParseColor("#DDFFFFFF"));
        rbLeft.ButtonTintList = Android.Content.Res.ColorStateList.ValueOf(themeColor);
        rbCenter.ButtonTintList = Android.Content.Res.ColorStateList.ValueOf(themeColor);
        rbRight.ButtonTintList = Android.Content.Res.ColorStateList.ValueOf(themeColor);
        rgAlignment.AddView(rbLeft);
        rgAlignment.AddView(rbCenter);
        rgAlignment.AddView(rbRight);
        rgAlignment.Check(_lyricRenderer.LyricAlignment switch { 0 => rbLeft.Id, 2 => rbRight.Id, _ => rbCenter.Id });
        content.AddView(rgAlignment);

        // ---- 字体颜色模式（自适应 / 自定义） ----
        var colorModeLabel = new TextView(Context) { Text = "字体颜色" };
        colorModeLabel.SetTextColor(Color.ParseColor("#B0FFFFFF"));
        colorModeLabel.SetTextSize(Android.Util.ComplexUnitType.Sp, 12f);
        colorModeLabel.SetPadding(0, dp * 12, 0, dp * 4);
        content.AddView(colorModeLabel);

        var rgColorMode = new RadioGroup(Context) { Orientation = Orientation.Horizontal };
        var rbAdaptive = new RadioButton(Context) { Text = "自适应" };
        var rbCustom = new RadioButton(Context) { Text = "自定义" };
        rbAdaptive.SetTextColor(Color.ParseColor("#DDFFFFFF"));
        rbCustom.SetTextColor(Color.ParseColor("#DDFFFFFF"));
        rbAdaptive.ButtonTintList = Android.Content.Res.ColorStateList.ValueOf(themeColor);
        rbCustom.ButtonTintList = Android.Content.Res.ColorStateList.ValueOf(themeColor);
        rgColorMode.AddView(rbAdaptive);
        rgColorMode.AddView(rbCustom);
        rgColorMode.Check(_lyricRenderer.LyricColorMode == 0 ? rbAdaptive.Id : rbCustom.Id);
        content.AddView(rgColorMode);

        // 自定义颜色容器（自适应模式下隐藏）
        var customColorContainer = new LinearLayout(Context) { Orientation = Orientation.Vertical };
        customColorContainer.Visibility = _lyricRenderer.LyricColorMode == 0 ? ViewStates.Gone : ViewStates.Visible;

        // ---- 当前行颜色（取色盘） ----
        var activeColorLabel = new TextView(Context) { Text = "当前行颜色" };
        activeColorLabel.SetTextColor(Color.ParseColor("#B0FFFFFF"));
        activeColorLabel.SetTextSize(Android.Util.ComplexUnitType.Sp, 12f);
        activeColorLabel.SetPadding(0, dp * 12, 0, dp * 4);
        customColorContainer.AddView(activeColorLabel);

        var activePicker = new ColorPickerView(Context);
        activePicker.SetColor(_lyricRenderer.LyricActiveColor);
        customColorContainer.AddView(activePicker);

        // 快速预设色按钮
        var activePresetRow = new LinearLayout(Context) { Orientation = Orientation.Horizontal };
        for (int i = 0; i < PresetColorNames.Length; i++)
        {
            var btn = new View(Context);
            var c = Color.ParseColor(PresetColorHex[i]);
            var gd = new GradientDrawable();
            gd.SetShape(ShapeType.Oval);
            gd.SetColor(c);
            gd.SetStroke(dp, Color.ParseColor("#33FFFFFF"));
            btn.Background = gd;
            var bLp = new LinearLayout.LayoutParams(dp * 26, dp * 26);
            bLp.SetMargins(dp * 3, dp * 3, dp * 3, dp * 3);
            btn.LayoutParameters = bLp;
            btn.Clickable = true;
            btn.Focusable = true;
            var presetColor = c;
            btn.Click += (s, e) => { activePicker.SetColor(presetColor); };
            activePresetRow.AddView(btn);
        }
        var activePresetScroll = new HorizontalScrollView(Context);
        activePresetScroll.AddView(activePresetRow);
        activePresetScroll.HorizontalScrollBarEnabled = false;
        customColorContainer.AddView(activePresetScroll);

        // ---- 非当前行颜色（取色盘） ----
        var inactiveColorLabel = new TextView(Context) { Text = "非当前行颜色" };
        inactiveColorLabel.SetTextColor(Color.ParseColor("#B0FFFFFF"));
        inactiveColorLabel.SetTextSize(Android.Util.ComplexUnitType.Sp, 12f);
        inactiveColorLabel.SetPadding(0, dp * 12, 0, dp * 4);
        customColorContainer.AddView(inactiveColorLabel);

        var inactivePicker = new ColorPickerView(Context);
        inactivePicker.SetColor(_lyricRenderer.LyricInactiveColor);
        customColorContainer.AddView(inactivePicker);

        // 快速预设色按钮
        var inactivePresetRow = new LinearLayout(Context) { Orientation = Orientation.Horizontal };
        for (int i = 0; i < InactivePresetNames.Length; i++)
        {
            var btn = new View(Context);
            var c = Color.ParseColor(InactivePresetHex[i]);
            var gd = new GradientDrawable();
            gd.SetShape(ShapeType.Oval);
            gd.SetColor(c);
            gd.SetStroke(dp, Color.ParseColor("#33FFFFFF"));
            btn.Background = gd;
            var bLp = new LinearLayout.LayoutParams(dp * 26, dp * 26);
            bLp.SetMargins(dp * 3, dp * 3, dp * 3, dp * 3);
            btn.LayoutParameters = bLp;
            btn.Clickable = true;
            btn.Focusable = true;
            var presetColor = c;
            btn.Click += (s, e) => { inactivePicker.SetColor(presetColor); };
            inactivePresetRow.AddView(btn);
        }
        var inactivePresetScroll = new HorizontalScrollView(Context);
        inactivePresetScroll.AddView(inactivePresetRow);
        inactivePresetScroll.HorizontalScrollBarEnabled = false;
        customColorContainer.AddView(inactivePresetScroll);
        content.AddView(customColorContainer);

        // ---- 背景遮罩 ----
        var bgColorLabel = new TextView(Context) { Text = "背景遮罩" };
        bgColorLabel.SetTextColor(Color.ParseColor("#B0FFFFFF"));
        bgColorLabel.SetTextSize(Android.Util.ComplexUnitType.Sp, 12f);
        bgColorLabel.SetPadding(0, dp * 12, 0, dp * 4);
        content.AddView(bgColorLabel);

        var currentBgColorIndex = _prefs?.GetInt("lyric_bg_color", 0) ?? 0;
        var rgBgColor = new RadioGroup(Context) { Orientation = Orientation.Horizontal };
        for (int i = 0; i < BgColorNames.Length; i++)
        {
            var rb = new RadioButton(Context) { Text = $"\u25CF {BgColorNames[i]}" };
            rb.SetTextSize(Android.Util.ComplexUnitType.Sp, 12f);
            rb.SetTextColor(Color.ParseColor("#DDFFFFFF"));
            rb.ButtonTintList = Android.Content.Res.ColorStateList.ValueOf(themeColor);
            rgBgColor.AddView(rb);
        }
        var initBgRb = rgBgColor.GetChildAt(Math.Clamp(currentBgColorIndex, 0, BgColorNames.Length - 1)) as RadioButton;
        if (initBgRb != null) rgBgColor.Check(initBgRb.Id);
        content.AddView(rgBgColor);

        cbDragSeek.CheckedChange += (s, e) =>
        {
            _allowDragSeek = e.IsChecked;
            _lyricRenderer!.EnableDragSeek = e.IsChecked;
            SaveSettings();
        };
        rgColorMode.CheckedChange += (s, e) =>
        {
            var newMode = e.CheckedId == rbAdaptive.Id ? 0 : 1;
            if (newMode == _lyricRenderer.LyricColorMode) return;
            _lyricRenderer.LyricColorMode = newMode;
            customColorContainer.Visibility = newMode == 0 ? ViewStates.Gone : ViewStates.Visible;
            SaveSettings();
            if (newMode == 0)
                _lyricRenderer.ApplyAdaptiveColors();
            _lyricRenderer.RefreshLyricColors();
        };
        rgLyricStyle.CheckedChange += (s, e) =>
        {
            var newStyle = e.CheckedId == rbWordByWord.Id ? 1 : 0;
            if (newStyle == currentLyricStyle) return;
            currentLyricStyle = newStyle;
            _lyricRenderer.LyricStyle = newStyle;
            catclawPrefs?.Edit().PutInt("lyric_style", newStyle).Apply();
            var viewModel = MainApplication.Services.GetRequiredService<NowPlayingViewModel>();
            viewModel.LyricStyle = newStyle;
            viewModel.UpdateLyricSpannable();
            _lyricRenderer.RebuildLyrics();
        };
        rgLyricsMode.CheckedChange += (s, e) =>
        {
            int uiIndex = -1;
            for (int i = 0; i < rgLyricsMode.ChildCount; i++)
            {
                if (rgLyricsMode.GetChildAt(i).Id == e.CheckedId)
                { uiIndex = i; break; }
            }
            if (uiIndex < 0) return;
            var newMode = uiIndex switch { 1 => 0, 2 => 1, 3 => 2, _ => 3 };
            if (newMode == currentLyricsMode) return;
            currentLyricsMode = newMode;
            catclawPrefs?.Edit().PutInt("lyrics_mode", newMode).Apply();
            var viewModel = MainApplication.Services.GetRequiredService<NowPlayingViewModel>();
            viewModel.LyricsMode = newMode;
            // 切换歌词来源时保持对唱模式设置不被重置
            _lyricRenderer.DuetMode = _prefs?.GetInt("duet_mode", _lyricRenderer.DuetMode) ?? _lyricRenderer.DuetMode;
            _ = viewModel.LoadLyricsAsync(viewModel.CurrentSong);
        };
        sbFontSize.ProgressChanged += (s, e) =>
        {
            _lyricRenderer.LyricFontSize = e.Progress;
            tvFontSizeValue.Text = $"{e.Progress}sp";
        };
        sbFontSize.StopTrackingTouch += (s, e) => { SaveSettings(); _lyricRenderer.RebuildLyrics(); };
        cbBold.CheckedChange += (s, e) =>
        {
            _lyricRenderer.LyricBold = e.IsChecked;
            SaveSettings();
            _lyricRenderer.RebuildLyrics();
        };
        rgDuetMode.CheckedChange += (s, e) =>
        {
            int newMode = -1;
            if (e.CheckedId == rbDuetStandard.Id) newMode = 0;
            else if (e.CheckedId == rbDuetFocus.Id) newMode = 1;
            else if (e.CheckedId == rbDuetSplit.Id) newMode = 2;
            if (newMode < 0 || newMode == _lyricRenderer.DuetMode) return;
            _lyricRenderer.DuetMode = newMode;
            SaveSettings();
            _lyricRenderer.RebuildLyrics();
        };
        rgAlignment.CheckedChange += (s, e) =>
        {
            var newAlign = e.CheckedId == rbLeft.Id ? 0 : e.CheckedId == rbRight.Id ? 2 : 1;
            if (newAlign == _lyricRenderer.LyricAlignment) return;
            _lyricRenderer.LyricAlignment = newAlign;
            SaveSettings();
            _lyricRenderer.RefreshLyricAlignment();
        };

        // 取色盘事件（仅自定义模式下生效）
        activePicker.ColorChanged += (c) =>
        {
            if (_lyricRenderer.LyricColorMode != 1) return;
            _lyricRenderer.LyricActiveColor = c;
            SaveSettings();
            _lyricRenderer.RefreshLyricColors();
        };
        inactivePicker.ColorChanged += (c) =>
        {
            if (_lyricRenderer.LyricColorMode != 1) return;
            _lyricRenderer.LyricInactiveColor = c;
            SaveSettings();
            _lyricRenderer.RefreshLyricColors();
        };
        rgBgColor.CheckedChange += (s, e) =>
        {
            int newIdx = -1;
            for (int i = 0; i < rgBgColor.ChildCount; i++)
            {
                if (rgBgColor.GetChildAt(i).Id == e.CheckedId)
                { newIdx = i; break; }
            }
            if (newIdx < 0) return;
            _prefs?.Edit().PutInt("lyric_bg_color", newIdx).Apply();
            // 刷新渲染视图内部的 bg color 索引缓存
            _lyricRenderer.LoadSettings();
            UpdateBgOverlay();
        };

        new GlassDialog(Context)
            .SetTitle("歌词设置")
            .AddCustomView(content)
            .AddNegativeButton("关闭")
            .Show();
    }

    /// <summary>
    /// 更新播放进度显示
    /// </summary>
    private void UpdateProgress()
    {
        var p = _viewModel.CurrentPosition;
        var d = _viewModel.TotalDuration;
        _progressText.Text = $"{p.Minutes}:{p.Seconds:D2} / {d.Minutes}:{d.Seconds:D2}";
    }

    /// <summary>
    /// 更新顶部歌手显示为当前正在演唱的歌手
    /// </summary>
    private void UpdateCurrentSinger()
    {
        var idx = _viewModel.CurrentLyricIndex;
        string? currentSinger = null;
        if (_viewModel.CurrentLyrics?.Lines != null && idx >= 0 && idx < _viewModel.CurrentLyrics.Lines.Count)
        {
            currentSinger = _viewModel.CurrentLyrics.Lines[idx].SingerName;
        }
        _songArtist.Text = !string.IsNullOrEmpty(currentSinger)
            ? $"🎤{currentSinger}"
            : _viewModel.CurrentSong?.Artist ?? "";
    }

    /// <summary>
    /// Fragment恢复可见时调用
    /// </summary>
    public override void OnResume()
    {
        base.OnResume();
        _lyricRenderer?.UpdateLyricsContainerPadding();
        UpdateProgress();
        _songTitle.Text = _viewModel.CurrentSong?.Title ?? "";
        UpdateCurrentSinger();
        _lyricRenderer?.RebuildLyrics();
        View?.Post(() => UpdateBackground());
    }

    /// <summary>
    /// 从外部通知 Fragment 需要滚动到当前歌词位置。
    /// 用于 Tab 切换时立即定位到当前播放位置，忽略用户之前的滚动状态。
    /// </summary>
    public void ScrollToCurrentPosition()
    {
        // 隐藏拖拽指示器（如果正在显示）
        if (_dragIndicator != null)
            _dragIndicator.Visibility = ViewStates.Gone;

        // 强制滚动到当前歌词（忽略用户滚动状态）
        _lyricRenderer?.ForceScrollToCurrent();

        // 延迟执行：ViewPager2 切换页面时视图可能尚未完成布局，
        // ScrollView.Height 可能为 0 或 SmoothScrollTo 被后续布局覆盖。
        // 等待 300ms 让布局稳定后再滚动。
        new Handler(Looper.MainLooper!).PostDelayed(() =>
        {
            _lyricRenderer?.ForceScrollToCurrent();
        }, 300);
    }

    /// <summary>显示拖拽跳转指示器</summary>
    private void ShowDragIndicator()
    {
        if (_dragIndicator != null)
            _dragIndicator.Visibility = ViewStates.Visible;
    }

    /// <summary>隐藏拖拽跳转指示器并清除超时</summary>
    private void HideDragIndicator()
    {
        _dragSeekTimeoutHandler.RemoveCallbacksAndMessages(null);
        if (_dragIndicator != null)
            _dragIndicator.Visibility = ViewStates.Gone;
    }

    /// <summary>
    /// Fragment销毁时清理资源
    /// </summary>
    public override void OnDestroyView()
    {
        _dragSeekTimeoutHandler.RemoveCallbacksAndMessages(null);
        UnbindViewModel();
        base.OnDestroyView();
    }
}
