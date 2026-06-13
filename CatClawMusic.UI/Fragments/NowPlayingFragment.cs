using Android.App;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using Android.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.UI.Helpers;
using CatClawMusic.UI.Services;
using CatClawMusic.UI.ViewModels;
using CatClawMusic.UI.Platforms.Android;
using Microsoft.Extensions.DependencyInjection;
using AndroidX.Activity.Result;
using AndroidX.Activity.Result.Contract;
using AndroidX.Core.View;
using GoogleSlider = Google.Android.Material.Slider.Slider;

namespace CatClawMusic.UI.Fragments;

/// <summary>
/// 正在播放Fragment，显示专辑封面、歌曲信息、歌词预览和播放控制
/// </summary>
public class NowPlayingFragment : Fragment
{
    private NowPlayingViewModel _viewModel = null!;
    private ImageView _albumCover = null!;
    private LinearLayout.LayoutParams? _coverLayoutParams;
    private TextView _songTitle = null!, _songArtist = null!;
    private StrokeTextView _lyricPrev2 = null!, _lyricPrev = null!, _lyricCurrent = null!, _lyricNext = null!, _lyricNext2 = null!;
    private TextView _timeCurrent = null!, _timeTotal = null!;
    private ImageButton _btnPlayPause = null!, _btnNext = null!, _btnPrev = null!;
    private ImageButton _btnLike = null!, _btnModeCycle = null!, _btnPlaylist = null!;
    private ImageButton _btnVisualizerToggle = null!;
    private ImageButton _btnEq = null!;
    private ImageButton _btnSleepTimer = null!;
    private ImageButton _btnLandscape = null!;
    private GoogleSlider _progressSlider = null!;
    private ImageView _bgCover = null!;
    private FlowLightView _flowLight = null!;
    private Google.Android.Material.Card.MaterialCardView _controlsCard = null!;
    private AudioVisualizerView _audioVisualizer = null!;
    private VisualizerHelper? _visualizerHelper;
    private Android.OS.Handler? _mainHandler;
    private int _spectrumUpdateQueued;
    private float[] _latestSpectrum = Array.Empty<float>();
    private ActivityResultLauncher? _recordAudioLauncher;
    private bool _visualizerEnabled = false;
    private bool _recordAudioDenied;
    private string? _lastCoverSource;
    private CancellationTokenSource? _sleepCts;
    private int _sleepRemainingSeconds;
    private bool _sleepFinishSong;
    private readonly Android.Views.Animations.DecelerateInterpolator _lyricInterpolator = new(1.5f);

    // --- 歌词自定义设置（与 FullLyricsFragment 共享） ---
    private static readonly string[] LyricActiveColorHex   = { "#FFFFFFFF", "#FFFFEB3B", "#FF69F0AE", "#FFFF80AB", "#FF64B5F6", "#FFFFAB40", "#FFFF6E6E", "#FFCE93D8", "#FF4DD0E1", "#FFFFD54F" };
    private static readonly string[] LyricInactiveColorHex = { "#CCBBBBBB", "#DDDDDDDD", "#CC90A4AE", "#CCB39DDB", "#CCBDBDBD", "#CCA8B8C8", "#CC78909C", "#CCD7CCC8" };
    /// <summary>背景遮罩颜色预设（与 FullLyricsFragment 共享）</summary>
    private static readonly string[] BgColorHex = { "#CCF0EBE3", "#CC0F0D16", "#00000000" };

    private Color _npLyricActiveColor = Color.White;
    private Color _npLyricInactiveColor = Color.ParseColor("#CCBBBBBB");
    private int _npLyricFontSize = 16;
    private bool _npLyricBold = true;
    private int _npLyricColorMode = 0; // 0=自适应, 1=自定义
    private float _currentBgLuminance = 0.3f; // 当前背景亮度（用于自适应模式）
    private int _lyricBgColorIndex = 0; // 背景遮罩颜色索引
    private View? _bgDimOverlay;

    public override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _recordAudioLauncher = RegisterForActivityResult(
            new ActivityResultContracts.RequestPermission(),
            new RecordAudioCallback(granted =>
            {
                if (granted)
                {
                    _recordAudioDenied = false;
                    var playerService = MainApplication.Services.GetRequiredService<IAudioPlayerService>();
                    var sessionId = playerService.AudioSessionId;
                    if (sessionId != 0)
                        StartVisualizerWithSession(sessionId);
                }
                else
                {
                    _recordAudioDenied = true;
                }
            }));
    }

    /// <summary>
    /// 创建正在播放视图
    /// </summary>
    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
        => inflater.Inflate(Resource.Layout.fragment_now_playing, container, false)!;

    /// <summary>
    /// 视图创建完成后初始化所有控件引用、绑定事件和ViewModel
    /// </summary>
    public override void OnViewCreated(View view, Bundle? state)
    {
        base.OnViewCreated(view, state);
        _viewModel = MainApplication.Services.GetRequiredService<NowPlayingViewModel>();
        var player = MainApplication.Services.GetRequiredService<IAudioPlayerService>();

        _albumCover = view.FindViewById<ImageView>(Resource.Id.album_cover)!;
        var coverContainer = (ViewGroup?)_albumCover.Parent?.Parent?.Parent;
        if (coverContainer?.LayoutParameters is LinearLayout.LayoutParams clp)
            _coverLayoutParams = clp;
        _songTitle = view.FindViewById<TextView>(Resource.Id.song_title)!;
        _songArtist = view.FindViewById<TextView>(Resource.Id.song_artist)!;
        _lyricPrev2 = (StrokeTextView)view.FindViewById(Resource.Id.lyric_prev2)!;
        _lyricPrev = (StrokeTextView)view.FindViewById(Resource.Id.lyric_prev)!;
        _lyricCurrent = (StrokeTextView)view.FindViewById(Resource.Id.lyric_current)!;
        _lyricNext = (StrokeTextView)view.FindViewById(Resource.Id.lyric_next)!;
        _lyricNext2 = (StrokeTextView)view.FindViewById(Resource.Id.lyric_next2)!;
        _lyricPrev2.ImportantForAutofill = Android.Views.ImportantForAutofill.No;
        _lyricPrev.ImportantForAutofill = Android.Views.ImportantForAutofill.No;
        _lyricCurrent.ImportantForAutofill = Android.Views.ImportantForAutofill.No;
        _lyricNext.ImportantForAutofill = Android.Views.ImportantForAutofill.No;
        _lyricNext2.ImportantForAutofill = Android.Views.ImportantForAutofill.No;
        _songTitle.ImportantForAutofill = Android.Views.ImportantForAutofill.No;
        _songArtist.ImportantForAutofill = Android.Views.ImportantForAutofill.No;
        _timeCurrent = view.FindViewById<TextView>(Resource.Id.time_current)!;
        _timeTotal = view.FindViewById<TextView>(Resource.Id.time_total)!;
        _timeCurrent.ImportantForAutofill = Android.Views.ImportantForAutofill.No;
        _timeTotal.ImportantForAutofill = Android.Views.ImportantForAutofill.No;
        _btnPlayPause = view.FindViewById<ImageButton>(Resource.Id.btn_play_pause)!;
        _btnNext = view.FindViewById<ImageButton>(Resource.Id.btn_next)!;
        _btnPrev = view.FindViewById<ImageButton>(Resource.Id.btn_prev)!;
        _btnLike = view.FindViewById<ImageButton>(Resource.Id.btn_like)!;
        _btnModeCycle = view.FindViewById<ImageButton>(Resource.Id.btn_mode_cycle)!;
        _btnPlaylist = view.FindViewById<ImageButton>(Resource.Id.btn_playlist)!;
        _btnVisualizerToggle = view.FindViewById<ImageButton>(Resource.Id.btn_visualizer_toggle)!;
        _btnEq = view.FindViewById<ImageButton>(Resource.Id.btn_eq)!;
        _btnSleepTimer = view.FindViewById<ImageButton>(Resource.Id.btn_sleep_timer)!;
        _btnLandscape = view.FindViewById<ImageButton>(Resource.Id.btn_landscape)!;
        _progressSlider = view.FindViewById<GoogleSlider>(Resource.Id.progress_slider)!;
        _controlsCard = view.FindViewById<Google.Android.Material.Card.MaterialCardView>(Resource.Id.controls_card)!;
        _audioVisualizer = view.FindViewById<AudioVisualizerView>(Resource.Id.audio_visualizer)!;
        _bgDimOverlay = view.FindViewById<View>(Resource.Id.bg_dim_overlay);
        _bgCover = view.FindViewById<ImageView>(Resource.Id.bg_cover)!;
        ApplyBlur();
        _flowLight = view.FindViewById<FlowLightView>(Resource.Id.flow_light)!;
        InitFlowLight();

        _progressSlider.ImportantForAutofill = Android.Views.ImportantForAutofill.No;
        _progressSlider.TickVisible = false;
        _progressSlider.ThumbRadius = 8;
        // 隐藏 Material Slider 右端黑色 stop indicator（改为白色透明不可见）
        try
        {
            _progressSlider.ThumbTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.White);
        }
        catch { }
        try
        {
            // 通过反射/内部 API 隐藏 stop indicator
            var sliderClass = Java.Lang.Class.FromType(typeof(GoogleSlider));
            var setStopIndicatorMethod = sliderClass.GetDeclaredMethod("setTrackStopIndicatorSize", Java.Lang.Integer.Type);
            if (setStopIndicatorMethod != null)
            {
                setStopIndicatorMethod.Accessible = true;
                setStopIndicatorMethod.Invoke(_progressSlider, 0);
            }
        }
        catch { }
        _controlsCard.ImportantForAutofill = Android.Views.ImportantForAutofill.No;
        _audioVisualizer.ImportantForAutofill = Android.Views.ImportantForAutofill.No;
        _btnPlayPause.ImportantForAutofill = Android.Views.ImportantForAutofill.No;
        _btnNext.ImportantForAutofill = Android.Views.ImportantForAutofill.No;
        _btnPrev.ImportantForAutofill = Android.Views.ImportantForAutofill.No;
        _btnLike.ImportantForAutofill = Android.Views.ImportantForAutofill.No;
        _btnModeCycle.ImportantForAutofill = Android.Views.ImportantForAutofill.No;
        _btnPlaylist.ImportantForAutofill = Android.Views.ImportantForAutofill.No;
        _btnVisualizerToggle.ImportantForAutofill = Android.Views.ImportantForAutofill.No;
        _btnEq.ImportantForAutofill = Android.Views.ImportantForAutofill.No;
        _btnSleepTimer.ImportantForAutofill = Android.Views.ImportantForAutofill.No;
        _btnLandscape.ImportantForAutofill = Android.Views.ImportantForAutofill.No;

        _audioVisualizer.Visibility = ViewStates.Invisible;
        var visPrefs = Activity?.GetSharedPreferences("catclaw_prefs", Android.Content.FileCreationMode.Private);
        var visEnabled = visPrefs?.GetBoolean("visualizer_enabled", false) ?? false;
        if (visEnabled)
            ApplyVisualizerState(true);

        // 加载歌词颜色和字号设置
        LoadLyricSettings();
        ApplyLyricFontSize();

        // 歌词区点击 → 跳转全屏歌词页 (Tab 0)
        // 用自定义触摸监听：短按跳转，水平滑动交给 ViewPager2
        var lyricsArea = view.FindViewById<View>(Resource.Id.lyrics_area);
        if (lyricsArea != null)
        {
            // 设置父控件可点击和可聚焦
            lyricsArea.Clickable = true;
            lyricsArea.Focusable = true;
            // 设置点击事件作为备用方案
            lyricsArea.Click += (s, e) => MainActivity.Instance?.SwitchTab(0);
            // 设置触摸监听器处理更复杂的手势
            lyricsArea.SetOnTouchListener(new LyricTapListener(() => MainActivity.Instance?.SwitchTab(0)));
        }

        // 同时设置子文本视图的点击事件，确保点击任意文本都能触发
        var lyricViews = new[] { _lyricPrev2, _lyricPrev, _lyricCurrent, _lyricNext, _lyricNext2 };
        foreach (var lyricView in lyricViews)
        {
            if (lyricView != null)
            {
                lyricView.Clickable = true;
                lyricView.Click += (s, e) => MainActivity.Instance?.SwitchTab(0);
            }
        }

        // 控制区域拦截 ViewPager2 的横向滑动
        var controlsArea = view.FindViewById<View>(Resource.Id.controls_area)!;
        controlsArea.SetOnTouchListener(new ControlsTouchListener());

        // 播放控制（Click -=/+= 防止 ViewPager 重建时重复绑定）
        _btnPlayPause.Click -= OnPlayPause; _btnPlayPause.Click += OnPlayPause;
        _btnNext.Click -= OnNext; _btnNext.Click += OnNext;
        _btnPrev.Click -= OnPrev; _btnPrev.Click += OnPrev;
        _btnLike.Click -= OnLikeClick; _btnLike.Click += OnLikeClick;
        _btnModeCycle.Click -= OnModeClick; _btnModeCycle.Click += OnModeClick;
        _btnPlaylist.Click -= OnPlaylistClick; _btnPlaylist.Click += OnPlaylistClick;
        _btnVisualizerToggle.Click -= OnVisualizerToggleClick; _btnVisualizerToggle.Click += OnVisualizerToggleClick;
        _btnEq.Click -= OnEqClick; _btnEq.Click += OnEqClick;
        _btnSleepTimer.Click -= OnSleepTimerClick; _btnSleepTimer.Click += OnSleepTimerClick;
        _btnLandscape.Click -= OnLandscapeClick; _btnLandscape.Click += OnLandscapeClick;

        // 进度条：Touch 松开时 seek（SetOnTouchListener 不影响原生拖动）
        _progressSlider.SetOnTouchListener(new SliderTouchListener(v => _viewModel.CurrentPositionSeconds = v));

        SyncUIFromViewModel();
        BindViewModel();

        var playerSvc = MainApplication.Services.GetRequiredService<IAudioPlayerService>() as AudioPlayerService;
        if (playerSvc != null)
            playerSvc.AudioSessionIdChanged += OnAudioSessionIdChanged;
    }

    private void AnimateCoverChange(string newCoverPath)
    {
        if (_albumCover == null) return;

        _albumCover.Animate().Cancel();

        try
        {
            var oldDrawable = _albumCover.Drawable as Android.Graphics.Drawables.BitmapDrawable;
            if (oldDrawable?.Bitmap != null && oldDrawable.Bitmap.IsRecycled)
            {
                _albumCover.SetImageResource(Resource.Drawable.cover_default);
            }
        }
        catch
        {
            try { _albumCover.SetImageResource(Resource.Drawable.cover_default); } catch { }
        }

        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            Bitmap? bitmap = null;
            try
            {
                if (!string.IsNullOrEmpty(newCoverPath) && File.Exists(newCoverPath))
                {
                    var options = new BitmapFactory.Options { InJustDecodeBounds = true };
                    BitmapFactory.DecodeFile(newCoverPath, options);
                    var targetSize = 960;
                    if (options.OutWidth > targetSize || options.OutHeight > targetSize)
                    {
                        var sampleSize = Math.Max(options.OutWidth, options.OutHeight) / targetSize;
                        if (sampleSize < 1) sampleSize = 1;
                        var shift = 0;
                        while ((1 << (shift + 1)) <= sampleSize) shift++;
                        bitmap = BitmapFactory.DecodeFile(newCoverPath, new BitmapFactory.Options { InSampleSize = 1 << shift });
                    }
                    else
                    {
                        bitmap = BitmapFactory.DecodeFile(newCoverPath);
                    }
                }
            }
            catch { }

            Activity?.RunOnUiThread(() =>
            {
                if (_albumCover == null)
                {
                    bitmap?.Recycle();
                    return;
                }

                _albumCover.SetLayerType(global::Android.Views.LayerType.Hardware, null);
                _albumCover.Alpha = 0.3f;
                _albumCover.ScaleX = 0.92f;
                _albumCover.ScaleY = 0.92f;

                try
                {
                    if (bitmap != null)
                        _albumCover.SetImageBitmap(bitmap);
                    else
                        _albumCover.SetImageResource(Resource.Drawable.cover_default);
                }
                catch
                {
                    try { _albumCover.SetImageResource(Resource.Drawable.cover_default); } catch { }
                }

                _albumCover.Animate()
                    .Alpha(1f)
                    .ScaleX(1f)
                    .ScaleY(1f)
                    .SetDuration(500)
                    .SetInterpolator(new Android.Views.Animations.OvershootInterpolator(0.8f))
                    .WithEndAction(new Java.Lang.Runnable(() =>
                    {
                        try { _albumCover.SetLayerType(global::Android.Views.LayerType.None, null); } catch { }
                    }))
                    .Start();
            });
        });
    }

    /// <summary>应用毛玻璃模糊效果（Android 12+）</summary>
    private void ApplyBlur()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
            _bgCover.SetRenderEffect(RenderEffect.CreateBlurEffect(120f, 120f, Shader.TileMode.Clamp));
    }

    /// <summary>更新背景封面图片</summary>
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
                    ComputeCoverLuminance(drawable);
                    UpdateFlowLightColors();
                    return;
                }
            }
        }
        catch { }

        _bgCover.SetImageResource(Resource.Drawable.cover_default);
        _currentBgLuminance = 0.3f;
        UpdateFlowLightColors();
    }

    /// <summary>从封面 Drawable 提取平均亮度</summary>
    private void ComputeCoverLuminance(Android.Graphics.Drawables.Drawable drawable)
    {
        try
        {
            var bd = drawable as Android.Graphics.Drawables.BitmapDrawable;
            var bitmap = bd?.Bitmap;
            if (bitmap == null || bitmap.IsRecycled) { _currentBgLuminance = 0.3f; return; }

            var scaled = Bitmap.CreateScaledBitmap(bitmap, 1, 1, false);
            if (scaled == null) { _currentBgLuminance = 0.3f; return; }

            var pixel = new int[1];
            scaled.GetPixels(pixel, 0, 1, 0, 0, 1, 1);
            var c = new Color(pixel[0]);
            _currentBgLuminance = (0.299f * c.R + 0.587f * c.G + 0.114f * c.B) / 255f;

            if (!ReferenceEquals(scaled, bitmap)) scaled.Recycle();
        }
        catch { _currentBgLuminance = 0.3f; }
    }

    /// <summary>初始化流光背景动画</summary>
    private void InitFlowLight()
    {
        if (_flowLight == null) return;
        var prefs = Activity?.GetSharedPreferences("catclaw_prefs", Android.Content.FileCreationMode.Private);
        bool enabled = prefs?.GetBoolean("bg_animation_enabled", false) ?? false;
        if (enabled)
        {
            _flowLight.Visibility = ViewStates.Visible;
            _flowLight.SetDefaultColors();
            _flowLight.Start();
            if (_viewModel.PlayPauseIcon != "▶")
                _flowLight.Pause();
            UpdateFlowLightColors();
        }
        else
        {
            _flowLight.Visibility = ViewStates.Gone;
        }
    }

    /// <summary>从封面提取主色调并更新流光颜色</summary>
    private void UpdateFlowLightColors()
    {
        if (_flowLight == null || _flowLight.Visibility != ViewStates.Visible) return;
        var coverPath = _viewModel.CoverSource;
        if (string.IsNullOrEmpty(coverPath) || !System.IO.File.Exists(coverPath))
        {
            _flowLight.SetDefaultColors();
            return;
        }
        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var colors = CoverColorExtractor.ExtractFromFile(coverPath);
                if (colors.Count == 0) return;
                var topColors = colors.OrderByDescending(e => e.Weight).Take(3).Select(e => e.Color).ToArray();
                Activity?.RunOnUiThread(() => _flowLight.SetCoverColors(topColors));
            }
            catch { }
        });
    }

    private void StartFlowLight()
    {
        if (_flowLight == null || _flowLight.Visibility != ViewStates.Visible) return;
        _flowLight.Start();
    }

    private void PauseFlowLight()
    {
        if (_flowLight == null || _flowLight.Visibility != ViewStates.Visible) return;
        _flowLight.Pause();
    }

    private void ResumeFlowLight()
    {
        if (_flowLight == null || _flowLight.Visibility != ViewStates.Visible) return;
        _flowLight.Resume();
    }

    private void StopFlowLight()
    {
        _flowLight?.Stop();
    }

    /// <summary>
    /// 从 SharedPreferences 加载歌词颜色和字号设置（与 FullLyricsFragment 共享）
    /// </summary>
    private void LoadLyricSettings()
    {
        var prefs = Activity?.GetSharedPreferences("lyric_settings", Android.Content.FileCreationMode.Private);
        if (prefs == null) return;

        _npLyricFontSize = prefs.GetInt("lyric_font_size", 16);
        _npLyricBold = prefs.GetBoolean("lyric_bold", true);
        _npLyricColorMode = prefs.GetInt("lyric_color_mode", 0);
        _lyricBgColorIndex = prefs.GetInt("lyric_bg_color", 0);
        UpdateBgOverlay();

        // 读取 ARGB 颜色值（向后兼容旧版索引存储）
        if (prefs.Contains("lyric_active_argb"))
        {
            _npLyricActiveColor = new Color(prefs.GetInt("lyric_active_argb", unchecked((int)0xFFFFFFFF)));
        }
        else
        {
            var activeIdx = prefs.GetInt("lyric_active_color", 0);
            _npLyricActiveColor = Color.ParseColor(LyricActiveColorHex[Math.Clamp(activeIdx, 0, LyricActiveColorHex.Length - 1)]);
        }
        if (prefs.Contains("lyric_inactive_argb"))
        {
            _npLyricInactiveColor = new Color(prefs.GetInt("lyric_inactive_argb", unchecked((int)0xCCBBBBBB)));
        }
        else
        {
            var inactiveIdx = prefs.GetInt("lyric_inactive_color", 0);
            _npLyricInactiveColor = Color.ParseColor(LyricInactiveColorHex[Math.Clamp(inactiveIdx, 0, LyricInactiveColorHex.Length - 1)]);
        }
    }

    /// <summary>
    /// 将歌词字号和加粗设置应用到5个歌词视图（按比例缩放）
    /// </summary>
    private void ApplyLyricFontSize()
    {
        float baseSp = _npLyricFontSize;
        var boldStyle = _npLyricBold ? TypefaceStyle.Bold : TypefaceStyle.Normal;
        // 保持与 XML 默认值相同的比例: 11:13:16
        _lyricCurrent.TextSize = baseSp;
        _lyricPrev.TextSize = baseSp * 0.8125f;   // 13/16
        _lyricNext.TextSize = baseSp * 0.8125f;
        _lyricPrev2.TextSize = baseSp * 0.6875f;  // 11/16
        _lyricNext2.TextSize = baseSp * 0.6875f;
        // 应用加粗设置
        _lyricCurrent.SetTypeface(null, boldStyle);
        _lyricPrev.SetTypeface(null, boldStyle);
        _lyricNext.SetTypeface(null, boldStyle);
        _lyricPrev2.SetTypeface(null, boldStyle);
        _lyricNext2.SetTypeface(null, boldStyle);
    }

    private void ApplyLyricColors()
    {
        // 自适应模式：透明/半透明遮罩用封面亮度，不透明遮罩用遮罩自身亮度
        if (_npLyricColorMode == 0)
        {
            var overlayAlpha = 0f;
            if (_bgDimOverlay?.Background is ColorDrawable cd)
                overlayAlpha = cd.Color.A / 255f;
            var luminance = overlayAlpha < 0.5f ? _currentBgLuminance : GetBgOverlayLuminance();

            if (luminance >= 0.5f)
            {
                // 浅色封面/遮罩：黑字，深灰未唱
                _npLyricActiveColor = Color.Black;
                _npLyricInactiveColor = Color.ParseColor("#AA333333");
            }
            else
            {
                // 深色封面/遮罩：白字，浅灰未唱
                _npLyricActiveColor = Color.White;
                _npLyricInactiveColor = Color.ParseColor("#EEBBBBBB");
            }
        }

        var sungColor = _npLyricActiveColor;
        var nearUnsungColor = _npLyricInactiveColor;
        // 远端歌词颜色在近端基础上降低透明度
        var farUnsungColor = Color.Argb(
            (byte)Math.Max(_npLyricInactiveColor.A * 2 / 3, 100),
            _npLyricInactiveColor.R,
            _npLyricInactiveColor.G,
            _npLyricInactiveColor.B);

        _lyricCurrent.SetTextColor(sungColor);
        _lyricPrev.SetTextColor(nearUnsungColor);
        _lyricNext.SetTextColor(nearUnsungColor);
        _lyricPrev2.SetTextColor(farUnsungColor);
        _lyricNext2.SetTextColor(farUnsungColor);

        _lyricCurrent.SungColor = sungColor;
        _lyricCurrent.UnsungColor = nearUnsungColor;

        // 当前行不用描边
        _lyricCurrent.StrokeEnabled = false;
        _lyricCurrent.ResetLyricProgress();
        // 未唱行启用描边增强可读性
        var strokeColor = sungColor == Color.Black
            ? Color.Argb(0x50, 0xFF, 0xFF, 0xFF)  // 浅色背景：淡白描边
            : Color.Argb(0x50, 0x00, 0x00, 0x00);  // 深色背景：淡黑描边
        _lyricPrev.StrokeEnabled = true; _lyricPrev.StrokeColor = strokeColor; _lyricPrev.StrokeWidth = 1f;
        _lyricNext.StrokeEnabled = true; _lyricNext.StrokeColor = strokeColor; _lyricNext.StrokeWidth = 1f;
        _lyricPrev2.StrokeEnabled = true; _lyricPrev2.StrokeColor = strokeColor; _lyricPrev2.StrokeWidth = 1f;
        _lyricNext2.StrokeEnabled = true; _lyricNext2.StrokeColor = strokeColor; _lyricNext2.StrokeWidth = 1f;
    }

    /// <summary>更新背景遮罩颜色</summary>
    private void UpdateBgOverlay()
    {
        if (_bgDimOverlay == null) return;
        _bgDimOverlay.SetBackgroundColor(Color.ParseColor(BgColorHex[Math.Clamp(_lyricBgColorIndex, 0, BgColorHex.Length - 1)]));
    }

    /// <summary>获取背景遮罩的实际亮度</summary>
    private float GetBgOverlayLuminance()
    {
        if (_bgDimOverlay?.Background is ColorDrawable cd)
        {
            var c = cd.Color;
            return (0.299f * c.R + 0.587f * c.G + 0.114f * c.B) / 255f;
        }
        return 0.1f; // 默认深色
    }

    private void SyncUIFromViewModel()
    {
        try
        {
            if (_albumCover == null) return;

            var coverSource = _viewModel.CoverSource;
            var coverChanged = coverSource != _lastCoverSource;

            if (!string.IsNullOrEmpty(coverSource))
            {
                if (coverChanged)
                {
                    AnimateCoverChange(coverSource);
                    UpdateBackground();
                }
            }
            else
            {
                _albumCover.SetImageResource(Resource.Drawable.cover_default);
            }
            _songTitle.Text = _viewModel.CurrentSong?.Title ?? "选择歌曲";
            if ((_viewModel.CurrentSong?.Source == SongSource.WebDAV || _viewModel.CurrentSong?.Source == SongSource.SMB) && _viewModel.CurrentSong.Artist == "未知艺术家")
                _songArtist.Text = "正在加载...";
            else
                _songArtist.Text = string.IsNullOrEmpty(_viewModel.CurrentSong?.Artist) ? "未知艺术家" : _viewModel.CurrentSong!.Artist;
            var prefs = Activity?.GetSharedPreferences("catclaw_prefs", Android.Content.FileCreationMode.Private);
            var visualizerEnabled = prefs?.GetBoolean("visualizer_enabled", false) ?? false;
            if (visualizerEnabled != _visualizerEnabled)
                ApplyVisualizerState(visualizerEnabled);
            _viewModel.LyricStyle = prefs?.GetInt("lyric_style", 0) ?? 0;
            _viewModel.LyricsMode = prefs?.GetInt("lyrics_mode", 0) ?? 0;
            UpdateTimeDisplay();
            UpdateSlider();
            UpdatePlayPauseIcon();
            UpdateModeIcon();
            UpdateLikeIcon();
            // 确保 Spannable 在 UpdateLyrics 之前已创建，避免整行闪烁
            if (_viewModel.LyricStyle == 1 && _viewModel.CurrentLyricSpannable == null)
                _viewModel.UpdateLyricSpannable();
            UpdateLyrics();
        }
        catch { }
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

    /// <summary>
    /// ViewModel属性变化时更新对应的UI控件
    /// </summary>
    private void OnViewModelPropertyChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
    {
        var act = Activity;
        if (act == null) return;
        act.RunOnUiThread(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(_viewModel.CoverSource):
                    var cover = _viewModel.CoverSource;
                    if (cover != _lastCoverSource && !string.IsNullOrEmpty(cover))
                    {
                        Activity?.RunOnUiThread(() =>
                        {
                            AnimateCoverChange(cover);
                            UpdateBackground();
                        });
                    }
                    else if (string.IsNullOrEmpty(cover))
                    {
                        _lastCoverSource = null;
                        _albumCover.SetImageResource(Resource.Drawable.cover_default);
                    }
                    break;
                case nameof(_viewModel.CurrentPosition):
                    UpdateTimeDisplay();
                    UpdateSlider();
                    break;
                case nameof(_viewModel.TotalDuration):
                    UpdateSlider();
                    break;
                case nameof(_viewModel.PlayPauseIcon):
                    UpdatePlayPauseIcon();
                    if (_viewModel.PlayPauseIcon == "▶")
                        PauseFlowLight();
                    else
                        ResumeFlowLight();
                    break;
                case nameof(_viewModel.PlayModeIcon):
                    UpdateModeIcon();
                    break;
                case nameof(_viewModel.LikeIcon):
                    UpdateLikeIcon();
                    break;
                case nameof(_viewModel.CurrentSong):
                    _songTitle.Text = _viewModel.CurrentSong?.Title ?? "选择歌曲";
                    if ((_viewModel.CurrentSong?.Source == SongSource.WebDAV || _viewModel.CurrentSong?.Source == SongSource.SMB) && _viewModel.CurrentSong.Artist == "未知艺术家")
                        _songArtist.Text = "正在加载...";
                    else
                        _songArtist.Text = string.IsNullOrEmpty(_viewModel.CurrentSong?.Artist) ? "未知艺术家" : _viewModel.CurrentSong!.Artist;
                    // 切歌时无需重启 Visualizer：ExoPlayer 复用同一 SessionId，
                    // 已绑定的 Visualizer 会自动接收新轨道的 FFT 数据
                    break;
                case nameof(_viewModel.CurrentLyricLine):
                    UpdateLyrics();
                    break;
                case nameof(_viewModel.CurrentLyricSpannable):
                    if (_viewModel.LyricStyle == 1 && _viewModel.CurrentLyricSpannable != null && _lyricCurrent != null)
                        _lyricCurrent.SetText(_viewModel.CurrentLyricSpannable, TextView.BufferType.Spannable);
                    else if (_lyricCurrent != null)
                        _lyricCurrent.Text = _viewModel.CurrentLyricLine ?? "";
                    break;
                case nameof(_viewModel.CurrentLyricProgress):
                    if (_lyricCurrent != null && _viewModel.LyricStyle == 1)
                        _lyricCurrent.LyricProgress = _viewModel.CurrentLyricProgress;
                    break;
            }
        });
    }

    private int _lastLyricIdx = -1;

    private void UpdateLyrics()
    {
        var prev2 = _viewModel.PrevLyricLine2;
        var prev = _viewModel.PrevLyricLine;
        var curr = _viewModel.CurrentLyricLine;
        var next = _viewModel.NextLyricLine;
        var next2 = _viewModel.NextLyricLine2;

        var idx = _viewModel.CurrentLyricIndex;
        var isLineChanged = idx != _lastLyricIdx && _lastLyricIdx != -1 && !string.IsNullOrEmpty(curr);
        _lastLyricIdx = idx;

        if (!isLineChanged)
        {
            _lyricPrev2.Text = prev2;  _lyricPrev2.TranslationY = 0f;
            _lyricPrev.Text = prev;    _lyricPrev.TranslationY = 0f;
            _lyricNext.Text = next;    _lyricNext.TranslationY = 0f;
            _lyricNext2.Text = next2;  _lyricNext2.TranslationY = 0f;
            ApplyCurrentLineWithSpannable(curr);
            return;
        }

        _lyricPrev2.Text = prev2;
        _lyricPrev.Text = prev;
        _lyricNext.Text = next;
        _lyricNext2.Text = next2;
        ApplyCurrentLineWithSpannable(curr);

        var views = new[] { _lyricPrev2, _lyricPrev, _lyricCurrent, _lyricNext, _lyricNext2 };
        var density = Context?.Resources?.DisplayMetrics?.Density ?? 1f;
        var lineH = 40f * density;

        foreach (var v in views)
        {
            v.Animate().Cancel();
            v.TranslationY = lineH;
            v.Animate()
                .TranslationY(0f)
                .SetDuration(300)
                .SetInterpolator(_lyricInterpolator)
                .Start();
        }
    }

    private void ApplyCurrentLineWithSpannable(string? plainText)
    {
        if (_lyricCurrent == null) return;
        if (_viewModel.LyricStyle == 1 && _viewModel.CurrentLyricSpannable != null)
        {
            _lyricCurrent.SetSpannableWithProgress(_viewModel.CurrentLyricSpannable, _viewModel.CurrentLyricProgress);
            _lyricCurrent.Alpha = 1f;
        }
        else
        {
            if (plainText != null)
            {
                _lyricCurrent.SetPlainTextNoGradient(plainText);
                _lyricCurrent.Alpha = 1f;
            }
            else
            {
                _lyricCurrent.ResetLyricProgress();
            }
        }
        _lyricCurrent.TranslationY = 0f;
    }

    /// <summary>
    /// 更新当前播放时间和总时长显示
    /// </summary>
    private void UpdateTimeDisplay()
    {
        _timeCurrent.Text = FormatTime(_viewModel.CurrentPosition);
        _timeTotal.Text = FormatTime(_viewModel.TotalDuration);
    }

    [ThreadStatic]
    private static char[]? _timeBuf;

    private static string FormatTime(TimeSpan t)
    {
        var buf = _timeBuf ??= new char[8];
        int m = t.Minutes;
        int s = t.Seconds;
        buf[0] = (char)('0' + m / 10);
        buf[1] = (char)('0' + m % 10);
        buf[2] = ':';
        buf[3] = (char)('0' + s / 10);
        buf[4] = (char)('0' + s % 10);
        return new string(buf, 0, 5);
    }

    /// <summary>
    /// 更新进度条滑块位置
    /// </summary>
    private void UpdateSlider()
    {
        var dur = (float)_viewModel.TotalDurationSeconds;
        if (dur > 0)
        {
            _progressSlider.ValueTo = dur;
            if (!_progressSlider.Pressed) // 拖动时不覆盖用户操作
                _progressSlider.Value = Math.Min((float)_viewModel.CurrentPositionSeconds, dur);
        }
    }

    /// <summary>
    /// 更新播放/暂停按钮图标
    /// </summary>
    private void UpdatePlayPauseIcon()
    {
        _btnPlayPause.SetImageResource(
            _viewModel.PlayPauseIcon == "⏸" ? Resource.Drawable.ic_pause : Resource.Drawable.ic_play);
    }

    /// <summary>
    /// 更新收藏按钮图标
    /// </summary>
    private void UpdateLikeIcon()
    {
        _btnLike.SetImageResource(
            _viewModel.LikeIcon == "❤️" ? Resource.Drawable.ic_favorite : Resource.Drawable.ic_favorite_border);
    }

    private void OnPlayPause(object? s, EventArgs e) => _viewModel.PlayPauseCommand.Execute(null);
    private void OnNext(object? s, EventArgs e) => _viewModel.NextCommand.Execute(null);
    private void OnPrev(object? s, EventArgs e) => _viewModel.PreviousCommand.Execute(null);
    private void OnLikeClick(object? s, EventArgs e) => _viewModel.ToggleLikeCommand.Execute(null);
    private void OnModeClick(object? s, EventArgs e) => _viewModel.CyclePlayModeCommand.Execute(null);
    private void OnPlaylistClick(object? s, EventArgs e) => ShowPlaylistDialog();

    private void OnVisualizerToggleClick(object? s, EventArgs e)
    {
        var prefs = Activity?.GetSharedPreferences("catclaw_prefs", Android.Content.FileCreationMode.Private);
        var enabled = !(prefs?.GetBoolean("visualizer_enabled", false) ?? false);
        prefs?.Edit().PutBoolean("visualizer_enabled", enabled).Apply();
        ApplyVisualizerState(enabled);
    }

    private void OnEqClick(object? s, EventArgs e)
    {
        try
        {
            var player = MainApplication.Services.GetRequiredService<IAudioPlayerService>();
            var dialog = new Services.SoundEffectDialog(Activity!, player);
            dialog.Show();
        }
        catch (Exception ex)
        {
            Android.Util.Log.Warn("CatClaw", $"打开音效失败: {ex.Message}");
        }
    }

    private void OnSleepTimerClick(object? s, EventArgs e)
    {
        if (_sleepCts != null)
        {
            StopSleepTimer();
            return;
        }
        ShowSleepTimerDialog();
    }

    private void OnLandscapeClick(object? s, EventArgs e)
    {
        var nav = MainApplication.Services.GetRequiredService<INavigationService>();
        nav.PushFragment("LandscapeNowPlaying");
    }

    private void CleanupViewResources()
    {
        StopFlowLight();
        var playerSvc = MainApplication.Services.GetService<IAudioPlayerService>() as AudioPlayerService;
        if (playerSvc != null)
            playerSvc.AudioSessionIdChanged -= OnAudioSessionIdChanged;
        _visualizerHelper?.Stop();
        _visualizerHelper = null;
        UnbindViewModel();
        _playlistDialog?.Dismiss();
        _playlistDialog = null;
        _lastLyricIdx = -1;
    }

    private void ShowSleepTimerDialog()
    {
        var act = Activity;
        if (act == null) return;

        var dp = (int)act.Resources!.DisplayMetrics!.Density;
        var tv = new Android.Util.TypedValue();
        var themeColor = act.Theme?.ResolveAttribute(global::Android.Resource.Attribute.ColorPrimary, tv, true) == true
            ? new Color(tv.Data) : Color.ParseColor("#9B7ED8");

        int selectedMinutes = 0;
        bool finishSong = false;

        var content = new LinearLayout(act) { Orientation = Orientation.Vertical };

        var timerToggleRow = new LinearLayout(act) { Orientation = Orientation.Horizontal };
        timerToggleRow.SetGravity(GravityFlags.CenterVertical);
        timerToggleRow.SetPadding(dp * 14, dp * 4, dp * 14, dp * 8);
        var timerLabel = new TextView(act) { Text = "定时关闭" };
        timerLabel.SetTextSize(Android.Util.ComplexUnitType.Sp, 13f);
        timerLabel.SetTextColor(Color.ParseColor("#DDFFFFFF"));
        timerLabel.LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 };
        var timerToggle = new Android.Widget.Switch(act) { Checked = true };
        timerToggle.TrackTintList = Android.Content.Res.ColorStateList.ValueOf(themeColor);
        timerToggle.ThumbTintList = Android.Content.Res.ColorStateList.ValueOf(Color.White);
        timerToggleRow.AddView(timerLabel);
        timerToggleRow.AddView(timerToggle);
        content.AddView(timerToggleRow);

        var timeGrid = new GridLayout(act);
        timeGrid.ColumnCount = 3;
        timeGrid.RowCount = 2;
        timeGrid.SetPadding(dp * 8, dp * 4, dp * 8, dp * 4);

        var timeButtons = new List<TextView>();
        foreach (var mins in new[] { 10, 20, 30, 45, 60, 90 })
        {
            var btn = new TextView(act) { Text = $"{mins}" };
            btn.SetTextSize(Android.Util.ComplexUnitType.Sp, 14f);
            btn.Gravity = GravityFlags.Center;
            btn.SetPadding(dp * 4, dp * 10, dp * 4, dp * 10);

            var btnSize = (int)(52 * dp);
            btn.LayoutParameters = new GridLayout.LayoutParams()
            {
                Width = btnSize,
                Height = btnSize,
                MarginStart = dp * 6,
                MarginEnd = dp * 6,
                TopMargin = dp * 4,
                BottomMargin = dp * 4,
                ColumnSpec = GridLayout.InvokeSpec(GridLayout.Undefined),
                RowSpec = GridLayout.InvokeSpec(GridLayout.Undefined)
            };

            var btnBg = new GradientDrawable();
            btnBg.SetShape(ShapeType.Rectangle);
            btnBg.SetCornerRadius(btnSize / 2f);
            btnBg.SetColor(Color.ParseColor("#1AFFFFFF"));
            btnBg.SetStroke(1, Color.ParseColor("#30FFFFFF"));
            btn.Background = btnBg;
            btn.SetTextColor(Color.ParseColor("#DDFFFFFF"));
            btn.Clickable = true;
            btn.Focusable = true;

            var capturedMins = mins;
            btn.Click += (s, e) =>
            {
                selectedMinutes = capturedMins;
                foreach (var b in timeButtons)
                {
                    b.SetTextColor(Color.ParseColor("#DDFFFFFF"));
                    var bg = new GradientDrawable();
                    bg.SetShape(ShapeType.Rectangle);
                    bg.SetCornerRadius(btnSize / 2f);
                    bg.SetColor(Color.ParseColor("#1AFFFFFF"));
                    bg.SetStroke(1, Color.ParseColor("#30FFFFFF"));
                    b.Background = bg;
                }
                var selBg = new GradientDrawable();
                selBg.SetShape(ShapeType.Rectangle);
                selBg.SetCornerRadius(btnSize / 2f);
                selBg.SetColor(themeColor);
                selBg.SetStroke(0, Color.Transparent);
                btn.Background = selBg;
                btn.SetTextColor(Color.White);
            };

            timeButtons.Add(btn);
            timeGrid.AddView(btn);
        }
        content.AddView(timeGrid);

        var customRow = new LinearLayout(act) { Orientation = Orientation.Horizontal };
        customRow.SetGravity(GravityFlags.Center);
        customRow.SetPadding(dp * 14, dp * 4, dp * 14, dp * 8);
        var customBtn = new TextView(act) { Text = "自定义" };
        customBtn.SetTextSize(Android.Util.ComplexUnitType.Sp, 13f);
        customBtn.SetTextColor(themeColor);
        customBtn.SetPadding(dp * 12, dp * 6, dp * 12, dp * 6);
        var customBg = new GradientDrawable();
        customBg.SetShape(ShapeType.Rectangle);
        customBg.SetCornerRadius(16 * dp);
        customBg.SetColor(Color.Transparent);
        customBg.SetStroke(1, themeColor);
        customBtn.Background = customBg;
        customBtn.Clickable = true;
        customBtn.Focusable = true;
        customRow.AddView(customBtn);
        content.AddView(customRow);

        var smartDivider = new View(act);
        smartDivider.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 1);
        smartDivider.SetBackgroundColor(Color.ParseColor("#20FFFFFF"));
        content.AddView(smartDivider);

        var smartCard = new LinearLayout(act) { Orientation = Orientation.Vertical };
        smartCard.SetPadding(dp * 14, dp * 12, dp * 14, dp * 8);
        var smartToggleRow = new LinearLayout(act) { Orientation = Orientation.Horizontal };
        smartToggleRow.SetGravity(GravityFlags.CenterVertical);
        var smartLabel = new TextView(act) { Text = "智能关闭" };
        smartLabel.SetTextSize(Android.Util.ComplexUnitType.Sp, 13f);
        smartLabel.SetTextColor(Color.ParseColor("#DDFFFFFF"));
        smartLabel.LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 };
        var smartToggle = new Android.Widget.Switch(act) { Checked = false };
        smartToggle.TrackTintList = Android.Content.Res.ColorStateList.ValueOf(themeColor);
        smartToggle.ThumbTintList = Android.Content.Res.ColorStateList.ValueOf(Color.White);
        smartToggleRow.AddView(smartLabel);
        smartToggleRow.AddView(smartToggle);
        smartCard.AddView(smartToggleRow);
        var smartDesc = new TextView(act) { Text = "根据睡眠状况动态调整定时" };
        smartDesc.SetTextSize(Android.Util.ComplexUnitType.Sp, 11f);
        smartDesc.SetTextColor(Color.ParseColor("#80FFFFFF"));
        smartDesc.SetPadding(0, dp * 4, 0, 0);
        smartCard.AddView(smartDesc);
        content.AddView(smartCard);

        var finishRow = new LinearLayout(act) { Orientation = Orientation.Horizontal };
        finishRow.SetGravity(GravityFlags.CenterVertical);
        finishRow.SetPadding(dp * 14, dp * 8, dp * 14, dp * 8);
        var finishCheck = new CheckBox(act) { Text = "播完整首歌再停止播放", Checked = false };
        finishCheck.SetTextColor(Color.ParseColor("#DDFFFFFF"));
        finishCheck.SetTextSize(Android.Util.ComplexUnitType.Sp, 13f);
        finishCheck.ButtonTintList = Android.Content.Res.ColorStateList.ValueOf(themeColor);
        finishCheck.CheckedChange += (s, e) => finishSong = e.IsChecked;
        finishRow.AddView(finishCheck);
        content.AddView(finishRow);

        var sleepDialog = new GlassDialog(act)
            .SetTitle("定时关闭")
            .AddCustomView(content)
            .AddPositiveButton("开始", (input) =>
            {
                if (!timerToggle.Checked) return;
                if (selectedMinutes > 0)
                    StartSleepTimer(selectedMinutes * 60, finishSong);
                else
                    ShowSleepCustomDialog(finishSong);
            })
            .AddNegativeButton("取消");
        sleepDialog.Show();

        customBtn.Click += (s, e) =>
        {
            sleepDialog.Dismiss();
            ShowSleepCustomDialog(finishSong);
        };
    }

    private void ShowSleepCustomDialog(bool finishSong)
    {
        var act = Activity;
        if (act == null) return;

        new GlassDialog(act)
            .SetTitle("自定义定时", "输入分钟数")
            .AddInput("请输入分钟数")
            .AddPositiveButton("确定", (input) =>
            {
                if (int.TryParse(input, out var mins) && mins > 0)
                    StartSleepTimer(mins * 60, finishSong);
            })
            .AddNegativeButton("取消")
            .Show();
    }

    private void StartSleepTimer(int totalSeconds, bool finishSong)
    {
        StopSleepTimer();
        _sleepCts = new CancellationTokenSource();
        _sleepRemainingSeconds = totalSeconds;
        _sleepFinishSong = finishSong;
        UpdateSleepDisplay();
        UpdateSleepButtonColor();

        var token = _sleepCts.Token;
        Task.Run(async () =>
        {
            try
            {
                while (_sleepRemainingSeconds > 0 && !token.IsCancellationRequested)
                {
                    await Task.Delay(1000, token);
                    _sleepRemainingSeconds--;
                    Activity?.RunOnUiThread(UpdateSleepDisplay);
                }

                if (!token.IsCancellationRequested)
                {
                    Activity?.RunOnUiThread(ExecuteSleepStop);
                }
            }
            catch (TaskCanceledException) { }
        }, token);
    }

    private void ExecuteSleepStop()
    {
        var player = MainApplication.Services.GetRequiredService<IAudioPlayerService>();
        if (_sleepFinishSong)
        {
            _viewModel.StopAfterCurrentSong = true;
            StopSleepTimer();
        }
        else
        {
            _ = player.PauseAsync();
            StopSleepTimer();
        }
    }

    private void StopSleepTimer()
    {
        _sleepCts?.Cancel();
        _sleepCts?.Dispose();
        _sleepCts = null;
        _sleepRemainingSeconds = 0;
        _sleepFinishSong = false;
        var textView = Activity?.FindViewById<TextView>(Resource.Id.sleep_timer_text);
        if (textView != null) textView.Visibility = ViewStates.Gone;
        UpdateSleepButtonColor();
    }

    private void UpdateSleepDisplay()
    {
        var textView = Activity?.FindViewById<TextView>(Resource.Id.sleep_timer_text);
        if (textView == null) return;
        if (_sleepRemainingSeconds > 0)
        {
            textView.Visibility = ViewStates.Visible;
            var ts = TimeSpan.FromSeconds(_sleepRemainingSeconds);
            textView.Text = ts.Hours > 0 ? $"{ts.Hours}:{ts.Minutes:D2}" : $"{ts.Minutes}";
        }
        else
        {
            textView.Visibility = ViewStates.Gone;
        }
    }

    private void UpdateSleepButtonColor()
    {
        var white = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.ParseColor("#FFFFFF"));
        var gray = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.ParseColor("#88FFFFFF"));
        _btnSleepTimer.ImageTintList = _sleepCts != null ? white : gray;
    }

    private void ApplyVisualizerState(bool enabled)
    {
        _visualizerEnabled = enabled;
        var white = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.ParseColor("#FFFFFF"));
        var gray = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.ParseColor("#88FFFFFF"));
        _btnVisualizerToggle.ImageTintList = enabled ? white : gray;
        if (enabled)
        {
            _audioVisualizer.Visibility = ViewStates.Visible;
            TryStartVisualizer();
        }
        else
        {
            _visualizerHelper?.Stop();
            _visualizerHelper = null;
            _audioVisualizer.Clear();
            _audioVisualizer.Visibility = ViewStates.Invisible;
        }
    }

    /// <summary>弹出当前播放列表悬浮窗（毛玻璃圆角卡片风格）</summary>
    private void ShowPlaylistDialog()
    {
        var act = Activity;
        if (act == null) return;

        var queue = MainApplication.Services.GetRequiredService<PlayQueue>();
        var allSongs = queue.GetSongs().ToList();
        if (allSongs.Count == 0) return;

        var currentSong = queue.CurrentSong;
        var dp = (int)act.Resources!.DisplayMetrics!.Density;
        var maxH = (int)(act.Resources!.DisplayMetrics!.HeightPixels * 0.5);

        var recyclerView = new AndroidX.RecyclerView.Widget.RecyclerView(act);
        recyclerView.SetLayoutManager(new AndroidX.RecyclerView.Widget.LinearLayoutManager(act));
        var itemHeight = (int)(72 * dp);
        var totalHeight = Math.Min(allSongs.Count * itemHeight, maxH);
        recyclerView.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, totalHeight);
        recyclerView.OverScrollMode = OverScrollMode.Never;

        var adapter = new PlaylistAdapter(allSongs, currentSong, dp, song =>
        {
            PlaySong(song);
            _playlistDialog?.Dismiss();
        });
        recyclerView.SetAdapter(adapter);

        recyclerView.Post(() =>
        {
            if (recyclerView.Height > maxH)
                recyclerView.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, maxH);
            if (currentSong != null)
            {
                var idx = allSongs.FindIndex(s => s.Id == currentSong.Id);
                if (idx >= 0)
                    recyclerView.ScrollToPosition(idx);
            }
        });

        var dialog = new GlassDialog(act)
            .SetTitle("播放列表", $"{allSongs.Count} 首歌曲")
            .AddCustomView(recyclerView)
            .AddNegativeButton("关闭");

        dialog.Show();
        _playlistDialog = null;
    }

    private Android.App.Dialog? _playlistDialog;

    /// <summary>选中并播放指定歌曲</summary>
    private void PlaySong(Song song)
    {
        var queue = MainApplication.Services.GetRequiredService<PlayQueue>();
        var player = MainApplication.Services.GetRequiredService<IAudioPlayerService>();

        queue.SelectSong(song.Id);
        _viewModel.CurrentSong = song;
        _ = player.PlayAsync(song.FilePath);

        // 延迟同步 UI，确保播放器状态更新
        Task.Delay(500).ContinueWith(_ => Activity?.RunOnUiThread(SyncUIFromViewModel));
    }

    /// <summary>
    /// 更新播放模式图标和颜色
    /// </summary>
    private void UpdateModeIcon()
    {
        _btnModeCycle.SetImageResource(
            _viewModel.PlayModeIcon switch
            {
                "🔀" => Resource.Drawable.ic_shuffle,
                "🔂" => Resource.Drawable.ic_repeat_one,
                "🔁" => Resource.Drawable.ic_repeat,
                _ => Resource.Drawable.ic_repeat // 顺序播放也用重复图标（灰色）
            });
        _btnModeCycle.SetColorFilter(
            _viewModel.PlayModeIcon is "🔀" or "🔂" or "🔁"
                ? Android.Graphics.Color.ParseColor("#FFFFFF")
                : Android.Graphics.Color.ParseColor("#88FFFFFF"));
    }

    /// <summary>
    /// Fragment显示/隐藏时同步UI状态
    /// </summary>
    public override void OnHiddenChanged(bool hidden)
    {
        base.OnHiddenChanged(hidden);
        if (!hidden)
        {
            LoadLyricSettings();
            ApplyLyricFontSize();
            InitFlowLight();
            // 强制刷新歌词颜色（用户可能在歌词页修改了设置）
            UpdateBackground();
            ApplyLyricColors();
            SyncUIFromViewModel();
        }
    }

    /// <summary>
    /// Fragment恢复可见时同步播放队列状态并刷新UI
    /// </summary>
    public override void OnResume()
    {
        base.OnResume();
        var visPrefs = Activity?.GetSharedPreferences("catclaw_prefs", Android.Content.FileCreationMode.Private);
        var visEnabled = visPrefs?.GetBoolean("visualizer_enabled", false) ?? false;
        ApplyVisualizerState(visEnabled);
        // 重新加载歌词设置（用户可能在歌词页修改了颜色/字号）
        LoadLyricSettings();
        ApplyLyricFontSize();
        var queue = MainApplication.Services.GetRequiredService<PlayQueue>();
        if (queue.CurrentSong != null)
        {
            _viewModel.SyncWithQueue();
            UpdatePlayPauseIcon();
            UpdateModeIcon();
            UpdateLikeIcon();
            _songTitle.Text = _viewModel.CurrentSong?.Title ?? "选择歌曲";
            _songArtist.Text = string.IsNullOrEmpty(_viewModel.CurrentSong?.Artist) ? "未知艺术家" : _viewModel.CurrentSong!.Artist;
            View?.Post(() =>
            {
                UpdateTimeDisplay();
                UpdateSlider();
                UpdateLyrics();
                InitFlowLight();
                // 确保歌词颜色反映最新设置
                UpdateBackground();
                ApplyLyricColors();
                var coverSource = _viewModel.CoverSource;
                if (!string.IsNullOrEmpty(coverSource) && coverSource != _lastCoverSource)
                {
                    AnimateCoverChange(coverSource);
                    UpdateBackground();
                }
                else if (string.IsNullOrEmpty(coverSource))
                {
                    _albumCover.SetImageResource(Resource.Drawable.cover_default);
                }
            });
        }
        if (_visualizerEnabled)
        {
            TryStartVisualizer();
            if (_visualizerHelper == null || !_visualizerHelper.IsEnabled)
            {
                View?.PostDelayed(() =>
                {
                    if (_visualizerEnabled && (_visualizerHelper == null || !_visualizerHelper.IsEnabled))
                        TryStartVisualizerWithRetry(0);
                }, 1500);
            }
        }
    }
    public override void OnPause()
    {
        base.OnPause();
    }

    /// <summary>
    /// Fragment销毁时清理资源，解绑事件，关闭对话框
    /// </summary>
    public override void OnDestroyView()
    {
        CleanupViewResources();
        base.OnDestroyView();
    }

    private int _lastVisualizerSessionId;

    /// <summary>
    /// 音频会话 ID 变化回调。
    /// ExoPlayer 切歌时通常会复用同一 SessionId，此时无需重建 Visualizer，
    /// 因为已绑定该 Session 的 Visualizer 会自动接收新轨道的 FFT 数据。
    /// 只在 SessionId 真正变化（如切换数据源重建 Player）时才重建。
    /// </summary>
    private void OnAudioSessionIdChanged(int newSessionId)
    {
        if (Activity == null) return;
        Activity.RunOnUiThread(() =>
        {
            if (!_visualizerEnabled) return;
            if (newSessionId == 0) return;

            // 同一 SessionId 且 Visualizer 已启用：无需重建，继续保持运作即可
            if (_visualizerHelper != null && _visualizerHelper.IsEnabled && newSessionId == _lastVisualizerSessionId)
                return;

            // SessionId 变化了，销毁旧的，延迟重建新的
            _visualizerHelper?.Stop();
            _visualizerHelper = null;
            _lastVisualizerSessionId = 0;
            View?.PostDelayed(() =>
            {
                if (!_visualizerEnabled) return;
                var playerService = MainApplication.Services.GetRequiredService<IAudioPlayerService>();
                if (playerService.AudioSessionId == newSessionId)
                    StartVisualizerWithSession(newSessionId);
            }, 600);
        });
    }

    private void TryStartVisualizer()
    {
        var playerService = MainApplication.Services.GetRequiredService<IAudioPlayerService>();
        var sessionId = playerService.AudioSessionId;
        if (sessionId == 0) return;

        if (_visualizerHelper != null && _visualizerHelper.IsEnabled && sessionId == _lastVisualizerSessionId)
            return;

        if (Activity?.CheckSelfPermission(Android.Manifest.Permission.RecordAudio) != Android.Content.PM.Permission.Granted)
        {
            if (!_recordAudioDenied)
                _recordAudioLauncher?.Launch(Android.Manifest.Permission.RecordAudio);
            return;
        }

        _lastVisualizerSessionId = sessionId;
        StartVisualizerWithSession(sessionId);
    }

    public void RestartVisualizer()
    {
        if (!_visualizerEnabled) return;
        _visualizerHelper?.Stop();
        _visualizerHelper = null;
        _lastVisualizerSessionId = 0;
        View?.PostDelayed(() =>
        {
            if (!_visualizerEnabled) return;
            var playerService = MainApplication.Services.GetRequiredService<IAudioPlayerService>();
            var sessionId = playerService.AudioSessionId;
            if (sessionId > 0 && (_visualizerHelper == null || !_visualizerHelper.IsEnabled))
                StartVisualizerWithSession(sessionId);
        }, 800);
    }

    private void TryStartVisualizerWithRetry(int attempt)
    {
        if (!_visualizerEnabled || attempt > 8) return;
        var playerService = MainApplication.Services.GetRequiredService<IAudioPlayerService>();
        var sessionId = playerService.AudioSessionId;
        if (sessionId > 0 && (_visualizerHelper == null || !_visualizerHelper.IsEnabled))
        {
            StartVisualizerWithSession(sessionId);
        }
        else if (_visualizerHelper == null || !_visualizerHelper.IsEnabled)
        {
            View?.PostDelayed(() => TryStartVisualizerWithRetry(attempt + 1), 400);
        }
    }

    private void StartVisualizerWithSession(int sessionId)
    {
        _visualizerHelper?.Stop();
        _visualizerHelper = new VisualizerHelper();
        _mainHandler ??= new Handler(Looper.MainLooper!);
        var spectrumCounter = 0;
        var lastUpdateTicks = 0L;
        _visualizerHelper.SpectrumUpdated += spectrum =>
        {
            var src = spectrum;
            if (_latestSpectrum.Length < src.Length)
                _latestSpectrum = new float[src.Length];
            Array.Copy(src, _latestSpectrum, src.Length);

            if (Interlocked.Exchange(ref _spectrumUpdateQueued, 1) == 1)
                return;

            var now = System.Environment.TickCount64;
            if (now - lastUpdateTicks < 50)
            {
                Interlocked.Exchange(ref _spectrumUpdateQueued, 0);
                return;
            }
            lastUpdateTicks = now;

            _mainHandler.Post(() =>
            {
                Interlocked.Exchange(ref _spectrumUpdateQueued, 0);
                _audioVisualizer?.UpdateSpectrum(_latestSpectrum);
                if (++spectrumCounter % 30 == 0)
                {
                    float sum = 0;
                    for (int i = 0; i < _latestSpectrum.Length; i++) sum += _latestSpectrum[i];
                    Android.Util.Log.Debug("CatClaw", $"[CatClaw] SpectrumUpdated x{spectrumCounter}, sum={sum:F2}");
                }
            });
        };
        _visualizerHelper.Start(sessionId);
    }

    internal class RecordAudioCallback : Java.Lang.Object, AndroidX.Activity.Result.IActivityResultCallback
    {
        private readonly Action<Java.Lang.Object> _callback;
        public RecordAudioCallback(Action<bool> callback) => _callback = result => callback((bool)result);
        public void OnActivityResult(Java.Lang.Object result) => _callback(result);
    }

    /// <summary>
    /// 进度条触摸监听器，在用户手指抬起时执行Seek操作
    /// </summary>
    internal class SliderTouchListener : Java.Lang.Object, View.IOnTouchListener
    {
        private readonly Action<float> _onEnd;
        /// <summary>
        /// 使用拖动结束回调初始化监听器
        /// </summary>
        public SliderTouchListener(Action<float> onEnd) => _onEnd = onEnd;
        /// <summary>
        /// 处理触摸事件，手指抬起时回调进度值
        /// </summary>
        public bool OnTouch(View? v, Android.Views.MotionEvent? e)
        {
            if (e?.Action == MotionEventActions.Up && v is Google.Android.Material.Slider.Slider slider)
                _onEnd(slider.Value);
            return false; // 不消费，让 Slider 原生拖动正常工作
        }
    }

    /// <summary>
    /// 控制区域触摸监听器，阻止父ViewPager2拦截控制区域的触摸事件
    /// </summary>
    internal class PlaylistAdapter : AndroidX.RecyclerView.Widget.RecyclerView.Adapter
    {
        private readonly List<Song> _songs;
        private readonly Song? _currentSong;
        private readonly int _dp;
        private readonly Action<Song> _onSongClick;

        public PlaylistAdapter(List<Song> songs, Song? currentSong, int dp, Action<Song> onSongClick)
        {
            _songs = songs;
            _currentSong = currentSong;
            _dp = dp;
            _onSongClick = onSongClick;
        }

        public override int ItemCount => _songs.Count;

        public override void OnBindViewHolder(AndroidX.RecyclerView.Widget.RecyclerView.ViewHolder holder, int position)
        {
            if (holder is not PlaylistViewHolder vh) return;
            var song = _songs[position];
            var isCurrent = _currentSong != null && song.Id == _currentSong.Id;
            vh.TextView.Text = $"{(isCurrent ? "▶  " : "    ")}{song.Title ?? "未知歌曲"} - {song.Artist ?? "未知艺术家"}";
            vh.TextView.SetTextColor(isCurrent ? Color.White : Color.ParseColor("#CCFFFFFF"));
            vh.TextView.SetPadding(_dp * 14, _dp * 8, _dp * 14, _dp * 8);
            vh.TextView.Click -= vh.Handler;
            vh.Handler = (s, e) => _onSongClick(song);
            vh.TextView.Click += vh.Handler;
        }

        public override AndroidX.RecyclerView.Widget.RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
        {
            var tv = new TextView(parent.Context!)
            {
                TextSize = 13f,
            };
            tv.SetSingleLine(true);
            tv.Ellipsize = Android.Text.TextUtils.TruncateAt.End;
            tv.Clickable = true;
            tv.Focusable = true;
            return new PlaylistViewHolder(tv);
        }
    }

    internal class PlaylistViewHolder : AndroidX.RecyclerView.Widget.RecyclerView.ViewHolder
    {
        public TextView TextView { get; }
        public EventHandler? Handler;
        public PlaylistViewHolder(TextView tv) : base(tv) => TextView = tv;
    }

    internal class ControlsTouchListener : Java.Lang.Object, View.IOnTouchListener
    {
        private float _downX, _downY;

        public bool OnTouch(View? v, Android.Views.MotionEvent? e)
        {
            if (e == null || v == null) return false;
            switch (e.Action)
            {
                case MotionEventActions.Down:
                    _downX = e.GetX();
                    _downY = e.GetY();
                    break;
                case MotionEventActions.Move:
                    {
                        float dx = Math.Abs(e.GetX() - _downX);
                        float dy = Math.Abs(e.GetY() - _downY);
                        if (dx > 20 && dx > dy * 1.5f)
                        {
                            var parent = v.Parent;
                            while (parent != null)
                            {
                                parent.RequestDisallowInterceptTouchEvent(false);
                                parent = parent.Parent;
                            }
                            return false;
                        }
                        var p = v.Parent;
                        while (p != null)
                        {
                            p.RequestDisallowInterceptTouchEvent(true);
                            p = p.Parent;
                        }
                    }
                    break;
                case MotionEventActions.Up:
                case MotionEventActions.Cancel:
                    {
                        var parent = v.Parent;
                        while (parent != null)
                        {
                            parent.RequestDisallowInterceptTouchEvent(false);
                            parent = parent.Parent;
                        }
                    }
                    break;
            }
            return false;
        }
    }

    /// <summary>播放列表弹窗适配器：高亮当前播放歌曲</summary>
    internal class PlaylistSongAdapter : ArrayAdapter<Song>
    {
        private readonly Song? _currentSong;
        public PlaylistSongAdapter(Android.Content.Context context, IList<Song> songs, Song? current)
            : base(context, 0, songs)
        {
            _currentSong = current;
        }

        public override View GetView(int position, View? convertView, ViewGroup? parent)
        {
            var song = GetItem(position);
            var view = convertView;
            if (view == null)
            {
                view = LayoutInflater.From(Context!)!.Inflate(
                    global::Android.Resource.Layout.SimpleListItem2, parent, false)!;
            }

            var text1 = view!.FindViewById<TextView>(global::Android.Resource.Id.Text1)!;
            var text2 = view.FindViewById<TextView>(global::Android.Resource.Id.Text2)!;

            bool isCurrent = _currentSong != null && song!.Id == _currentSong.Id;

            text1.Text = isCurrent ? $"♫ {song!.Title}" : $"    {song!.Title}";
            text2.Text = $"{song!.Artist} · {song!.Album}";
            text2.Visibility = ViewStates.Visible;

            // 高亮当前歌曲（从主题读取颜色）
            if (isCurrent)
            {
                var themeColor = UiHelper.ResolveThemeColor(Context!, Resource.Attribute.catClawPrimaryColor, Android.Graphics.Color.ParseColor("#9B7ED8"));
                var c = new Android.Graphics.Color(themeColor);
                view.SetBackgroundColor(Android.Graphics.Color.Argb(40, c.R, c.G, c.B));
                text1.SetTextColor(new Android.Graphics.Color(themeColor));
            }
            else
            {
                view.SetBackgroundColor(Android.Graphics.Color.Transparent);
                text1.SetTextColor(Android.Graphics.Color.ParseColor("#DDDDDD"));
            }

            text2.SetTextColor(Android.Graphics.Color.ParseColor("#999999"));
            return view;
        }
    }

    /// <summary>歌词区触摸监听：短按跳转全屏歌词，水平滑动交给 ViewPager2</summary>
    internal class LyricTapListener : Java.Lang.Object, View.IOnTouchListener
    {
        private readonly Action _onTap;
        private float _downX, _downY;
        private long _downTime;
        private bool _isDown;

        public LyricTapListener(Action onTap) => _onTap = onTap;

        public bool OnTouch(View? v, MotionEvent? e)
        {
            if (e == null || v == null) return false;

            switch (e.Action)
            {
                case MotionEventActions.Down:
                    _downX = e.GetX();
                    _downY = e.GetY();
                    _downTime = Java.Lang.JavaSystem.CurrentTimeMillis();
                    _isDown = true;
                    break;

                case MotionEventActions.Move:
                    if (_isDown)
                    {
                        float dx = Math.Abs(e.GetX() - _downX);
                        float dy = Math.Abs(e.GetY() - _downY);
                        if (dx > 15 && dx > dy * 1.2f)
                        {
                            v.Parent?.RequestDisallowInterceptTouchEvent(false);
                            _isDown = false;
                        }
                    }
                    break;

                case MotionEventActions.Up:
                case MotionEventActions.Cancel:
                    if (_isDown && e.Action == MotionEventActions.Up)
                    {
                        float dx = Math.Abs(e.GetX() - _downX);
                        float dy = Math.Abs(e.GetY() - _downY);
                        long dt = Java.Lang.JavaSystem.CurrentTimeMillis() - _downTime;
                        if (dx < 40 && dy < 40 && dt < 500)
                            _onTap();
                    }
                    _isDown = false;
                    v.Parent?.RequestDisallowInterceptTouchEvent(false);
                    break;
            }
            return false;
        }
    }
}