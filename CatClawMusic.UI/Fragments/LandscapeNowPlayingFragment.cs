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

public class LandscapeNowPlayingFragment : Fragment
{
    private NowPlayingViewModel _viewModel = null!;
    private ImageView _albumCover = null!;
    private TextView _songTitle = null!, _songArtist = null!;
    private TextView _timeCurrent = null!, _timeTotal = null!;
    private LyricRendererView? _lyricRenderer;
    private View _coverPanel = null!;
    private bool _isFullLyricMode;
    private ImageButton _btnPlayPause = null!, _btnNext = null!, _btnPrev = null!;
    private ImageButton _btnLike = null!, _btnModeCycle = null!;
    private GoogleSlider _progressSlider = null!;
    private Google.Android.Material.Card.MaterialCardView _controlsCard = null!;
    private AudioVisualizerView _audioVisualizer = null!;
    private VisualizerHelper? _visualizerHelper;
    private Android.OS.Handler? _mainHandler;
    private Android.OS.Handler? _autoHideHandler;
    private Android.OS.Handler? _watchdogHandler;
    private bool _isControlsAutoHidden;
    private long _lastSpectrumTicks;
    private int _spectrumUpdateQueued;
    private float[] _latestSpectrum = Array.Empty<float>();
    private ActivityResultLauncher? _recordAudioLauncher;
    private bool _visualizerEnabled = false;
    private bool _recordAudioDenied;
    private string? _lastCoverSource;
    private readonly Android.Views.Animations.DecelerateInterpolator _lyricInterpolator = new(1.5f);

    /// <summary>背景遮罩颜色预设（与 FullLyricsFragment 共享）</summary>
    private static readonly string[] BgColorHex = { "#CCF0EBE3", "#CC0F0D16", "#00000000" };

    private float _currentBgLuminance = 0.3f;
    private int _lyricBgColorIndex = 0;
    private View? _bgDimOverlay;
    private ImageView _bgCover = null!;
    private FlowLightView _flowLight = null!;

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

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
        => inflater.Inflate(Resource.Layout.fragment_now_playing_land, container, false)!;

    public override void OnViewCreated(View view, Bundle? state)
    {
        base.OnViewCreated(view, state);
        _viewModel = MainApplication.Services.GetRequiredService<NowPlayingViewModel>();

        _albumCover = view.FindViewById<ImageView>(Resource.Id.album_cover)!;
        _songTitle = view.FindViewById<TextView>(Resource.Id.song_title)!;
        _songArtist = view.FindViewById<TextView>(Resource.Id.song_artist)!;
        _lyricRenderer = view.FindViewById<LyricRendererView>(Resource.Id.lyric_renderer)!;
        _coverPanel = view.FindViewById<View>(Resource.Id.cover_panel)!;
        _timeCurrent = view.FindViewById<TextView>(Resource.Id.time_current)!;
        _timeTotal = view.FindViewById<TextView>(Resource.Id.time_total)!;
        _btnPlayPause = view.FindViewById<ImageButton>(Resource.Id.btn_play_pause)!;
        _btnNext = view.FindViewById<ImageButton>(Resource.Id.btn_next)!;
        _btnPrev = view.FindViewById<ImageButton>(Resource.Id.btn_prev)!;
        _btnLike = view.FindViewById<ImageButton>(Resource.Id.btn_like)!;
        _btnModeCycle = view.FindViewById<ImageButton>(Resource.Id.btn_mode_cycle)!;
        _progressSlider = view.FindViewById<GoogleSlider>(Resource.Id.progress_slider)!;
        _progressSlider.TickVisible = false;
        _progressSlider.ThumbRadius = 8;
        _progressSlider.SetLabelFormatter(new SliderLabelFormatter());
        try { _progressSlider.ThumbTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.White); }
        catch { }
        try
        {
            var sliderClass = Java.Lang.Class.FromType(typeof(GoogleSlider));
            var setStopIndicatorMethod = sliderClass.GetDeclaredMethod("setTrackStopIndicatorSize", Java.Lang.Integer.Type);
            if (setStopIndicatorMethod != null)
            {
                setStopIndicatorMethod.Accessible = true;
                setStopIndicatorMethod.Invoke(_progressSlider, 0);
            }
        }
        catch { }
        _progressSlider.SetOnTouchListener(new SliderTouchListener(v => _viewModel.CurrentPositionSeconds = v));

        _controlsCard = view.FindViewById<Google.Android.Material.Card.MaterialCardView>(Resource.Id.controls_card)!;
        _audioVisualizer = view.FindViewById<AudioVisualizerView>(Resource.Id.audio_visualizer)!;
        _bgDimOverlay = view.FindViewById<View>(Resource.Id.bg_dim_overlay);
        _bgCover = view.FindViewById<ImageView>(Resource.Id.bg_cover)!;
        ApplyBlur();
        _flowLight = view.FindViewById<FlowLightView>(Resource.Id.flow_light)!;
        InitFlowLight();

        var btnLandscape = view.FindViewById<ImageButton>(Resource.Id.btn_landscape)!;
        btnLandscape.SetImageResource(Resource.Drawable.ic_landscape);
        var white = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.ParseColor("#FFFFFF"));
        btnLandscape.ImageTintList = white;
        btnLandscape.Click += OnExitLandscape;

        _btnPlayPause.Click += OnPlayPause;
        _btnNext.Click += OnNext;
        _btnPrev.Click += OnPrev;
        _btnLike.Click += OnLikeClick;
        _btnModeCycle.Click += OnModeClick;

        _audioVisualizer.Visibility = ViewStates.Gone;
        var visPrefs = Activity?.GetSharedPreferences("catclaw_prefs", Android.Content.FileCreationMode.Private);
        var visEnabled = visPrefs?.GetBoolean("visualizer_enabled", false) ?? false;
        if (visEnabled)
            ApplyVisualizerState(true);

        // 初始化歌词渲染视图（与 FullLyricsFragment 保持一致）
        var lyricPrefs = Activity?.GetSharedPreferences("lyric_settings", Android.Content.FileCreationMode.Private);
        _lyricRenderer.Init(_viewModel, lyricPrefs);
        _lyricRenderer.LoadSettings();
        _lyricRenderer.EnableScroll = true;
        _lyricRenderer.EnableDragSeek = false;
        _lyricRenderer.EnableRaindropWordBounce = false;
        _lyricRenderer.BgDimOverlay = _bgDimOverlay;
        // 点击歌词：控件隐藏时弹出控件；控件可见时进入全屏歌词；全屏时退出全屏
        _lyricRenderer.OnClickCallback = OnLyricsTapped;

        // 封面区域点击：控件隐藏时弹出控件；全屏时退出全屏
        _coverPanel.Clickable = true;
        _coverPanel.Click += (s, e) => OnScreenTouched();

        // 根视图触摸监听：作为兜底，子视图未消费事件时弹出控件
        view.SetOnTouchListener(new RootTouchListener(this));

        // 加载横屏页背景遮罩颜色
        LoadLyricSettings();

        // 若歌词已加载，立即渲染当前歌词
        if (_viewModel.CurrentLyrics?.Lines?.Count > 0)
        {
            _lyricRenderer.RebuildLyrics();
            _lyricRenderer.HighlightCurrentLine();
        }

        SyncUIFromViewModel();
        BindViewModel();

        var playerSvc = MainApplication.Services.GetRequiredService<IAudioPlayerService>() as AudioPlayerService;
        if (playerSvc != null)
            playerSvc.AudioSessionIdChanged += OnAudioSessionIdChanged;
    }

    private void HideStatusBar()
    {
        var act = Activity;
        if (act == null || act.Window == null) return;
        WindowCompat.SetDecorFitsSystemWindows(act.Window, false);
        var controller = WindowCompat.GetInsetsController(act.Window, act.Window.DecorView);
        controller.AppearanceLightStatusBars = true;
        controller.SystemBarsBehavior = WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;
        controller.Hide(WindowInsetsCompat.Type.StatusBars());
    }

    private void ShowStatusBar()
    {
        var act = Activity;
        if (act == null || act.Window == null) return;
        var controller = WindowCompat.GetInsetsController(act.Window, act.Window.DecorView);
        controller.AppearanceLightStatusBars = true;
        // 同时显示状态栏和导航栏，避免横屏切回竖屏后导航栏处于 transient 状态弹出
        controller.Show(WindowInsetsCompat.Type.StatusBars() | WindowInsetsCompat.Type.NavigationBars());
        // 恢复为 false，与 MainActivity 的 SetDecorFitsSystemWindows(false) 保持一致
        // MainActivity 通过 FitSystemBars() 的 WindowInsetsListener 手动处理 padding
        WindowCompat.SetDecorFitsSystemWindows(act.Window, false);
    }

    private void OnExitLandscape(object? s, EventArgs e)
    {
        var nav = MainApplication.Services.GetRequiredService<INavigationService>();
        nav.GoBack();
    }

    /// <summary>
    /// 歌词区域点击回调：根据当前状态切换全屏歌词模式或弹出控件。
    /// - 全屏歌词模式：退出全屏，恢复控件
    /// - 控件隐藏：弹出控件
    /// - 控件可见：进入全屏歌词模式
    /// </summary>
    private void OnLyricsTapped()
    {
        if (_isFullLyricMode)
        {
            _isFullLyricMode = false;
            ApplyFullLyricMode();
            StartAutoHideTimer();
        }
        else if (_isControlsAutoHidden || _controlsCard.Visibility != ViewStates.Visible)
        {
            // 控件隐藏时，点击歌词先弹出控件
            ShowControls();
        }
        else
        {
            // 控件可见时，点击歌词进入全屏歌词模式
            _isFullLyricMode = true;
            ApplyFullLyricMode();
            StopAutoHideTimer();
        }
    }

    /// <summary>
    /// 屏幕任意位置触摸回调：仅用于控件隐藏后弹出控件。
    /// </summary>
    internal void OnScreenTouched()
    {
        if (_isFullLyricMode)
        {
            _isFullLyricMode = false;
            ApplyFullLyricMode();
            StartAutoHideTimer();
        }
        else if (_isControlsAutoHidden || _controlsCard.Visibility != ViewStates.Visible)
        {
            ShowControls();
        }
    }

    /// <summary>显示播放控件并启动5秒自动隐藏倒计时</summary>
    private void ShowControls()
    {
        if (_controlsCard == null) return;
        if (_controlsCard.Visibility != ViewStates.Visible)
        {
            _controlsCard.Visibility = ViewStates.Visible;
            _controlsCard.Alpha = 0f;
            _controlsCard.TranslationY = 40f;
            _controlsCard.Animate().Alpha(1f).TranslationY(0f).SetDuration(300).Start();
        }
        _isControlsAutoHidden = false;
        StartAutoHideTimer();
    }

    /// <summary>隐藏播放控件（仅控件卡片，进度条和横竖屏按钮保持可见）</summary>
    private void HideControls()
    {
        if (_controlsCard == null) return;
        _controlsCard.Animate().Alpha(0f).TranslationY(40f).SetDuration(300)
            .WithEndAction(new Java.Lang.Runnable(() => _controlsCard.Visibility = ViewStates.Gone)).Start();
        _isControlsAutoHidden = true;
    }

    /// <summary>启动5秒后自动收起控件的定时器</summary>
    private void StartAutoHideTimer()
    {
        StopAutoHideTimer();
        _autoHideHandler ??= new Android.OS.Handler(Android.OS.Looper.MainLooper!);
        _autoHideHandler.PostDelayed(() =>
        {
            if (!_isFullLyricMode && _controlsCard != null && _controlsCard.Visibility == ViewStates.Visible)
                HideControls();
        }, 5000);
    }

    /// <summary>停止自动隐藏定时器</summary>
    private void StopAutoHideTimer()
    {
        _autoHideHandler?.RemoveCallbacksAndMessages(null);
    }

    private void ApplyFullLyricMode()
    {
        if (_coverPanel == null || _lyricRenderer == null) return;
        var duration = 300L;
        if (_isFullLyricMode)
        {
            // 全屏歌词模式：隐藏封面、可视化效果、进度条、控件卡片和横竖屏按钮，歌词扩展至全屏
            StopAutoHideTimer();
            _isControlsAutoHidden = false;
            _coverPanel.Animate().Alpha(0f).SetDuration(duration).WithEndAction(new Java.Lang.Runnable(() => _coverPanel.Visibility = ViewStates.Gone)).Start();
            _audioVisualizer.Animate().Alpha(0f).SetDuration(duration).WithEndAction(new Java.Lang.Runnable(() => _audioVisualizer.Visibility = ViewStates.Gone)).Start();
            _controlsCard.Animate().Alpha(0f).TranslationY(40f).SetDuration(duration).WithEndAction(new Java.Lang.Runnable(() => _controlsCard.Visibility = ViewStates.Gone)).Start();
        }
        else
        {
            // 退出全屏：恢复封面、可视化效果、进度条、控件卡片和横竖屏按钮
            _coverPanel.Alpha = 0f;
            _coverPanel.Visibility = ViewStates.Visible;
            _coverPanel.Animate().Alpha(1f).SetDuration(duration).Start();
            _audioVisualizer.Alpha = 0f;
            _audioVisualizer.Visibility = _visualizerEnabled ? ViewStates.Visible : ViewStates.Gone;
            if (_visualizerEnabled) _audioVisualizer.Animate().Alpha(1f).SetDuration(duration).Start();
            _controlsCard.Alpha = 0f;
            _controlsCard.TranslationY = 40f;
            _controlsCard.Visibility = ViewStates.Visible;
            _controlsCard.Animate().Alpha(1f).TranslationY(0f).SetDuration(duration).Start();
        }
    }

    public override void OnResume()
    {
        base.OnResume();
        Activity?.RequestedOrientation = Android.Content.PM.ScreenOrientation.SensorLandscape;
        HideStatusBar();
        var queue = MainApplication.Services.GetRequiredService<PlayQueue>();
        if (queue.CurrentSong != null)
        {
            _viewModel.SyncWithQueue();
            // 重新加载歌词设置（用户可能在歌词页修改了颜色/字号）
            _lyricRenderer?.LoadSettings();
            LoadLyricSettings();
            InitFlowLight();
            UpdateBackground();
            SyncUIFromViewModel();
            // 切到横屏时若歌词已存在，立即重建并高亮
            if (_lyricRenderer != null && _viewModel.CurrentLyrics?.Lines?.Count > 0)
            {
                _lyricRenderer.RebuildLyrics();
                _lyricRenderer.HighlightCurrentLine();
            }
        }
        var resumeVisPrefs = Activity?.GetSharedPreferences("catclaw_prefs", Android.Content.FileCreationMode.Private);
        var resumeVisEnabled = resumeVisPrefs?.GetBoolean("visualizer_enabled", false) ?? false;
        ApplyVisualizerState(resumeVisEnabled);
        if (_visualizerEnabled && (_visualizerHelper == null || !_visualizerHelper.IsEnabled))
        {
            View?.PostDelayed(() =>
            {
                if (_visualizerEnabled && (_visualizerHelper == null || !_visualizerHelper.IsEnabled))
                    TryStartVisualizer();
            }, 1500);
        }
        View?.PostDelayed(() =>
        {
            UpdateSlider();
            UpdatePlayPauseIcon();
        }, 800);

        if (Activity is MainActivity ma)
            ma.SetViewPagerSwipeEnabled(false);

        // 进入横屏5秒后自动收起播放控件
        StartAutoHideTimer();
    }

    public override void OnPause()
    {
        base.OnPause();
        StopAutoHideTimer();
        StopWatchdog();
        _visualizerHelper?.Stop();
        _visualizerHelper = null;
        _lastVisualizerSessionId = 0;
        Activity!.RequestedOrientation = Android.Content.PM.ScreenOrientation.Portrait;
        ShowStatusBar();
        if (Activity is MainActivity ma)
        {
            ma.SetViewPagerSwipeEnabled(true);
            var npFragment = ma.GetTabAdapter()?.NowPlayingFragment;
            if (npFragment != null)
            {
                _mainHandler ??= new Android.OS.Handler(Android.OS.Looper.MainLooper!);
                _mainHandler.PostDelayed(() => npFragment.RestartVisualizer(), 150);
            }
        }
    }

    public override void OnDestroyView()
    {
        StopAutoHideTimer();
        StopWatchdog();
        StopFlowLight();
        _isFullLyricMode = false;
        var playerSvc = MainApplication.Services.GetService<IAudioPlayerService>() as AudioPlayerService;
        if (playerSvc != null)
            playerSvc.AudioSessionIdChanged -= OnAudioSessionIdChanged;
        _visualizerHelper?.Stop();
        _visualizerHelper = null;
        UnbindViewModel();
        base.OnDestroyView();
    }

    private void SyncUIFromViewModel()
    {
        try
        {
            if (_albumCover == null) return;

            if (_viewModel.CurrentSong != null && string.IsNullOrEmpty(_viewModel.CoverSource))
            {
                _ = _viewModel.LoadCoverAsync(_viewModel.CurrentSong);
                _ = _viewModel.LoadLyricsAsync(_viewModel.CurrentSong);
            }

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
            _viewModel.LyricsMode = prefs?.GetInt("lyrics_mode", 3) ?? 3;
            UpdateTimeDisplay();
            UpdateSlider();
            UpdatePlayPauseIcon();
            UpdateModeIcon();
            UpdateLikeIcon();
            if (_viewModel.LyricStyle == 1 && _viewModel.CurrentLyricSpannable == null)
                _viewModel.UpdateLyricSpannable();
        }
        catch { }
    }

    private void BindViewModel() => _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    private void UnbindViewModel() => _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

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
                    // 切歌时无需重启 Visualizer：ExoPlayer 复用同一 SessionId，已绑定 Visualizer 自动继续工作
                    break;
                case nameof(_viewModel.CurrentLyricLine):
                case nameof(_viewModel.CurrentLyricIndex):
                    _lyricRenderer?.HighlightCurrentLine();
                    break;
                case nameof(_viewModel.CurrentLyrics):
                    _lyricRenderer?.RebuildLyrics();
                    break;
                case nameof(_viewModel.CurrentLyricSpannable):
                    // 逐字 spannable 由 LyricRendererView 内部处理
                    break;
                case nameof(_viewModel.CurrentLyricProgress):
                    if (_lyricRenderer?.LyricStyle == 1)
                        _lyricRenderer?.UpdateCurrentLineGradient();
                    break;
                case nameof(_viewModel.DuetPartnerIndex):
                    _lyricRenderer?.HighlightCurrentLine();
                    break;
                case nameof(_viewModel.DuetPartnerProgress):
                    if (_lyricRenderer?.LyricStyle == 1)
                        _lyricRenderer?.UpdateCurrentLineGradient();
                    break;

            }
        });
    }

    private void AnimateCoverChange(string newCoverPath)
    {
        if (_albumCover == null) return;
        _albumCover.Animate().Cancel();
        try
        {
            var oldDrawable = _albumCover.Drawable as BitmapDrawable;
            if (oldDrawable?.Bitmap != null && oldDrawable.Bitmap.IsRecycled)
                _albumCover.SetImageResource(Resource.Drawable.cover_default);
        }
        catch { try { _albumCover.SetImageResource(Resource.Drawable.cover_default); } catch { } }

        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            Bitmap? bitmap = null;
            try
            {
                if (!string.IsNullOrEmpty(newCoverPath) && File.Exists(newCoverPath))
                {
                    var opts = new BitmapFactory.Options { InJustDecodeBounds = true };
                    BitmapFactory.DecodeFile(newCoverPath, opts);
                    var targetSize = 960;
                    if (opts.OutWidth > targetSize || opts.OutHeight > targetSize)
                    {
                        var sampleSize = Math.Max(opts.OutWidth, opts.OutHeight) / targetSize;
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
                if (_albumCover == null) { bitmap?.Recycle(); return; }
                _albumCover.SetLayerType(LayerType.Hardware, null);
                _albumCover.Alpha = 0.3f;
                _albumCover.ScaleX = 0.92f;
                _albumCover.ScaleY = 0.92f;
                try
                {
                    if (bitmap != null) _albumCover.SetImageBitmap(bitmap);
                    else _albumCover.SetImageResource(Resource.Drawable.cover_default);
                }
                catch { try { _albumCover.SetImageResource(Resource.Drawable.cover_default); } catch { } }
                _albumCover.Animate()
                    .Alpha(1f).ScaleX(1f).ScaleY(1f)
                    .SetDuration(500)
                    .SetInterpolator(new Android.Views.Animations.OvershootInterpolator(0.8f))
                    .WithEndAction(new Java.Lang.Runnable(() => { try { _albumCover.SetLayerType(LayerType.None, null); } catch { } }))
                    .Start();
            });
        });
    }

    /// <summary>
    /// 从 SharedPreferences 加载横屏页背景遮罩颜色
    /// </summary>
    private void LoadLyricSettings()
    {
        var prefs = Activity?.GetSharedPreferences("lyric_settings", Android.Content.FileCreationMode.Private);
        if (prefs == null) return;

        _lyricBgColorIndex = prefs.GetInt("lyric_bg_color", 0);
        UpdateBgOverlay();
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
            if (_viewModel.PlayPauseIcon == "▶")
                _flowLight.Pause();
            UpdateFlowLightColors();
            if (_bgDimOverlay != null)
                _bgDimOverlay.Alpha = 0.4f;
        }
        else
        {
            _flowLight.Visibility = ViewStates.Gone;
            if (_bgDimOverlay != null)
                _bgDimOverlay.Alpha = 1.0f;
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

    private void UpdateTimeDisplay()
    {
        _timeCurrent.Text = $"{_viewModel.CurrentPosition.Minutes}:{_viewModel.CurrentPosition.Seconds:D2}";
        _timeTotal.Text = $"{_viewModel.TotalDuration.Minutes}:{_viewModel.TotalDuration.Seconds:D2}";
    }

    private void UpdateSlider()
    {
        var dur = (float)_viewModel.TotalDurationSeconds;
        if (dur > 0)
        {
            _progressSlider.ValueTo = dur;
            if (!_progressSlider.Pressed)
                _progressSlider.Value = Math.Min((float)_viewModel.CurrentPositionSeconds, dur);
        }
    }

    private void UpdatePlayPauseIcon()
    {
        _btnPlayPause.SetImageResource(_viewModel.PlayPauseIcon == "⏸" ? Resource.Drawable.ic_pause : Resource.Drawable.ic_play);
    }

    private void UpdateLikeIcon()
    {
        _btnLike.SetImageResource(_viewModel.LikeIcon == "❤️" ? Resource.Drawable.ic_favorite : Resource.Drawable.ic_favorite_border);
    }

    private void UpdateModeIcon()
    {
        _btnModeCycle.SetImageResource(
            _viewModel.PlayModeIcon switch
            {
                "🔀" => Resource.Drawable.ic_shuffle,
                "🔂" => Resource.Drawable.ic_repeat_one,
                "🔁" => Resource.Drawable.ic_repeat,
                _ => Resource.Drawable.ic_repeat
            });
        _btnModeCycle.SetColorFilter(
            _viewModel.PlayModeIcon is "🔀" or "🔂" or "🔁"
                ? Color.ParseColor("#FFFFFF")
                : Color.ParseColor("#88FFFFFF"));
    }

    private void OnPlayPause(object? s, EventArgs e) => _viewModel.PlayPauseCommand.Execute(null);
    private void OnNext(object? s, EventArgs e) => _viewModel.NextCommand.Execute(null);
    private void OnPrev(object? s, EventArgs e) => _viewModel.PreviousCommand.Execute(null);
    private void OnLikeClick(object? s, EventArgs e) => _viewModel.ToggleLikeCommand.Execute(null);
    private void OnModeClick(object? s, EventArgs e) => _viewModel.CyclePlayModeCommand.Execute(null);

    private void ApplyVisualizerState(bool enabled)
    {
        _visualizerEnabled = enabled;
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
            _audioVisualizer.Visibility = ViewStates.Gone;
        }
    }

    private int _lastVisualizerSessionId;

    /// <summary>
    /// 音频会话 ID 变化回调。使用 IsAlive 检查原生 Visualizer 真实状态，
    /// 避免 IsEnabled 僵尸标志导致无法重启。
    /// </summary>
    private void OnAudioSessionIdChanged(int newSessionId)
    {
        if (Activity == null) return;
        Activity.RunOnUiThread(() =>
        {
            if (!_visualizerEnabled) return;
            if (newSessionId == 0) return;

            // 使用 IsAlive 检查真实状态，而非 IsEnabled
            if (_visualizerHelper != null && _visualizerHelper.IsAlive(newSessionId))
                return;

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
            if (sessionId > 0 && (_visualizerHelper == null || !_visualizerHelper.IsAlive(sessionId)))
                StartVisualizerWithSession(sessionId);
        }, 800);
    }

    private void TryStartVisualizerWithRetry(int attempt)
    {
        if (!_visualizerEnabled || attempt > 8) return;
        var playerService = MainApplication.Services.GetRequiredService<IAudioPlayerService>();
        var sessionId = playerService.AudioSessionId;
        if (sessionId > 0 && (_visualizerHelper == null || !_visualizerHelper.IsAlive(sessionId)))
        {
            StartVisualizerWithSession(sessionId);
        }
        else if (_visualizerHelper == null || !_visualizerHelper.IsAlive(sessionId))
        {
            View?.PostDelayed(() => TryStartVisualizerWithRetry(attempt + 1), 400);
        }
    }

    private void TryStartVisualizer()
    {
        var playerService = MainApplication.Services.GetRequiredService<IAudioPlayerService>();
        var sessionId = playerService.AudioSessionId;
        if (sessionId == 0) return;
        if (_visualizerHelper != null && _visualizerHelper.IsAlive(sessionId)) return;
        if (Activity?.CheckSelfPermission(Android.Manifest.Permission.RecordAudio) != Android.Content.PM.Permission.Granted)
        {
            if (!_recordAudioDenied)
                _recordAudioLauncher?.Launch(Android.Manifest.Permission.RecordAudio);
            return;
        }
        _lastVisualizerSessionId = sessionId;
        StartVisualizerWithSession(sessionId);
    }

    private void StartVisualizerWithSession(int sessionId)
    {
        _visualizerHelper?.Stop();
        _visualizerHelper = new VisualizerHelper();
        _mainHandler ??= new Handler(Looper.MainLooper!);
        _lastSpectrumTicks = System.Environment.TickCount64;
        var lastUpdateTicks = 0L;
        _visualizerHelper.SpectrumUpdated += spectrum =>
        {
            var src = spectrum;
            if (_latestSpectrum.Length < src.Length) _latestSpectrum = new float[src.Length];
            Array.Copy(src, _latestSpectrum, src.Length);
            _lastSpectrumTicks = System.Environment.TickCount64;
            if (Interlocked.Exchange(ref _spectrumUpdateQueued, 1) == 1) return;

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
            });
        };
        _visualizerHelper.Start(sessionId);
        StartWatchdog();
    }

    /// <summary>启动频谱看门狗：每 3 秒检查一次 Visualizer 是否存活，死亡则重启</summary>
    private void StartWatchdog()
    {
        StopWatchdog();
        _watchdogHandler ??= new Android.OS.Handler(Android.OS.Looper.MainLooper!);
        _watchdogHandler.PostDelayed(new Java.Lang.Runnable(() =>
        {
            if (!_visualizerEnabled || _visualizerHelper == null) return;
            var playerService = MainApplication.Services.GetRequiredService<IAudioPlayerService>();
            var sessionId = playerService.AudioSessionId;
            if (sessionId == 0) { StartWatchdog(); return; }
            if (!_visualizerHelper.IsAlive(sessionId))
            {
                Android.Util.Log.Warn("CatClaw", "[CatClaw] Watchdog: Visualizer dead, restarting");
                _visualizerHelper?.Stop();
                _visualizerHelper = null;
                _lastVisualizerSessionId = 0;
                StartVisualizerWithSession(sessionId);
                return; // StartVisualizerWithSession 内部会重新启动看门狗
            }
            StartWatchdog();
        }), 3000);
    }

    /// <summary>停止频谱看门狗</summary>
    private void StopWatchdog()
    {
        _watchdogHandler?.RemoveCallbacksAndMessages(null);
    }

    internal class RecordAudioCallback : Java.Lang.Object, AndroidX.Activity.Result.IActivityResultCallback
    {
        private readonly Action<Java.Lang.Object> _callback;
        public RecordAudioCallback(Action<bool> callback) => _callback = result => callback((bool)result);
        public void OnActivityResult(Java.Lang.Object result) => _callback(result);
    }

    internal class SliderTouchListener : Java.Lang.Object, View.IOnTouchListener
    {
        private readonly Action<float> _onEnd;
        public SliderTouchListener(Action<float> onEnd) => _onEnd = onEnd;
        public bool OnTouch(View? v, MotionEvent? e)
        {
            if (e?.Action == MotionEventActions.Up && v is GoogleSlider slider) _onEnd(slider.Value);
            return false;
        }
    }

    /// <summary>
    /// 根视图触摸监听器：当播放控件隐藏后，触摸屏幕任意位置（ACTION_UP）弹出控件。
    /// 返回 false 不消费事件，保证子视图正常响应点击/滚动。
    /// </summary>
    internal class RootTouchListener : Java.Lang.Object, View.IOnTouchListener
    {
        private readonly LandscapeNowPlayingFragment _fragment;
        private float _startX, _startY;
        private bool _isClick;

        public RootTouchListener(LandscapeNowPlayingFragment fragment) => _fragment = fragment;

        public bool OnTouch(View? v, MotionEvent? e)
        {
            if (e == null || v == null) return false;
            var density = v.Resources?.DisplayMetrics?.Density ?? 1f;
            var slop = 16 * density;

            switch (e.Action)
            {
                case MotionEventActions.Down:
                    _startX = e.GetX();
                    _startY = e.GetY();
                    _isClick = true;
                    break;

                case MotionEventActions.Move:
                    if (_isClick)
                    {
                        var dx = Math.Abs(e.GetX() - _startX);
                        var dy = Math.Abs(e.GetY() - _startY);
                        if (dx > slop || dy > slop)
                            _isClick = false;
                    }
                    break;

                case MotionEventActions.Up:
                    if (_isClick)
                    {
                        var dx = Math.Abs(e.GetX() - _startX);
                        var dy = Math.Abs(e.GetY() - _startY);
                        if (dx <= slop && dy <= slop)
                            _fragment.OnScreenTouched();
                    }
                    _isClick = false;
                    break;

                case MotionEventActions.Cancel:
                    _isClick = false;
                    break;
            }
            return false;
        }
    }
}
