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
    private LyricRendererView? _lyricRenderer;
    private View _coverPanel = null!;
    private bool _isFullLyricMode;
    private TextView _timeCurrent = null!, _timeTotal = null!;
    private ImageButton _btnPlayPause = null!, _btnNext = null!, _btnPrev = null!;
    private ImageButton _btnLike = null!, _btnModeCycle = null!, _btnPlaylist = null!;
    private ImageButton _btnVisualizerToggle = null!;
    private ImageButton _btnEq = null!;
    private ImageButton _btnSleepTimer = null!;
    private ImageButton _btnBack = null!;
    private GoogleSlider _progressSlider = null!;
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
    private Android.App.Dialog? _playlistDialog;

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
        var player = MainApplication.Services.GetRequiredService<IAudioPlayerService>();

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
        _btnPlaylist = view.FindViewById<ImageButton>(Resource.Id.btn_playlist)!;
        _btnVisualizerToggle = view.FindViewById<ImageButton>(Resource.Id.btn_visualizer_toggle)!;
        _btnEq = view.FindViewById<ImageButton>(Resource.Id.btn_eq)!;
        _btnSleepTimer = view.FindViewById<ImageButton>(Resource.Id.btn_sleep_timer)!;
        _progressSlider = view.FindViewById<GoogleSlider>(Resource.Id.progress_slider)!;
        _progressSlider.TickVisible = false;
        _progressSlider.ThumbRadius = 8;
        _progressSlider.SetLabelFormatter(new SliderLabelFormatter());
        // 隐藏 Material Slider 右端黑色 stop indicator
        try
        {
            _progressSlider.ThumbTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.White);
        }
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

        var controlsArea = view.FindViewById<View>(Resource.Id.controls_area)!;
        controlsArea.SetOnTouchListener(new ControlsTouchListener());

        _btnPlayPause.Click += OnPlayPause;
        _btnNext.Click += OnNext;
        _btnPrev.Click += OnPrev;
        _btnLike.Click += OnLikeClick;
        _btnModeCycle.Click += OnModeClick;
        _btnPlaylist.Click += OnPlaylistClick;
        _btnVisualizerToggle.Click += OnVisualizerToggleClick;
        _btnEq.Click += OnEqClick;
        _btnSleepTimer.Click += OnSleepTimerClick;

        _progressSlider.SetOnTouchListener(new SliderTouchListener(v => _viewModel.CurrentPositionSeconds = v));

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
        _lyricRenderer.OnClickCallback = () => OnLyricsAreaClick(null, EventArgs.Empty);

        // 加载横屏页背景遮罩颜色
        LoadLyricSettings();

        // 若歌词已加载，立即渲染当前歌词
        if (_viewModel.CurrentLyrics?.Lines?.Count > 0)
        {
            _lyricRenderer.RebuildLyrics();
            _lyricRenderer.HighlightCurrentLine();
        }

        if (_audioPlayer != null)
            _audioPlayer.StateChanged += OnAudioPlayerStateChanged;

        SyncUIFromViewModel();
        BindViewModel();

        var playerSvc = MainApplication.Services.GetRequiredService<IAudioPlayerService>() as AudioPlayerService;
        if (playerSvc != null)
            playerSvc.AudioSessionIdChanged += OnAudioSessionIdChanged;
    }

    private IAudioPlayerService? _audioPlayer;

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

    private void OnLyricsAreaClick(object? s, EventArgs e)
    {
        _isFullLyricMode = !_isFullLyricMode;
        ApplyFullLyricMode();
    }

    private void ApplyFullLyricMode()
    {
        if (_coverPanel == null || _lyricRenderer == null) return;
        var duration = 300L;
        if (_isFullLyricMode)
        {
            // 全屏歌词模式：隐藏封面、可视化效果和控制区，歌词扩展至全屏
            _coverPanel.Animate().Alpha(0f).SetDuration(duration).WithEndAction(new Java.Lang.Runnable(() => _coverPanel.Visibility = ViewStates.Gone)).Start();
            _audioVisualizer.Animate().Alpha(0f).SetDuration(duration).WithEndAction(new Java.Lang.Runnable(() => _audioVisualizer.Visibility = ViewStates.Gone)).Start();
            _controlsCard.Animate().Alpha(0f).TranslationY(40f).SetDuration(duration).WithEndAction(new Java.Lang.Runnable(() => _controlsCard.Visibility = ViewStates.Gone)).Start();
        }
        else
        {
            // 退出全屏：恢复封面、可视化效果和控制区
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
    }

    public override void OnPause()
    {
        base.OnPause();
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
        StopFlowLight();
        _isFullLyricMode = false;
        var playerSvc = MainApplication.Services.GetService<IAudioPlayerService>() as AudioPlayerService;
        if (playerSvc != null)
            playerSvc.AudioSessionIdChanged -= OnAudioSessionIdChanged;
        if (_audioPlayer != null)
            _audioPlayer.StateChanged -= OnAudioPlayerStateChanged;
        _visualizerHelper?.Stop();
        _visualizerHelper = null;
        UnbindViewModel();
        _playlistDialog?.Dismiss();
        _playlistDialog = null;
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
        if (_sleepCts != null) { StopSleepTimer(); return; }
        ShowSleepTimerDialog();
    }

    private void ApplyVisualizerState(bool enabled)
    {
        _visualizerEnabled = enabled;
        var white = Android.Content.Res.ColorStateList.ValueOf(Color.ParseColor("#FFFFFF"));
        var gray = Android.Content.Res.ColorStateList.ValueOf(Color.ParseColor("#88FFFFFF"));
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
            _audioVisualizer.Visibility = ViewStates.Gone;
        }
    }

    private void OnAudioPlayerStateChanged(object? sender, PlaybackStateChangedEventArgs e)
    {
        Activity?.RunOnUiThread(UpdatePlayState);
    }

    private void UpdatePlayState()
    {
        var currentSong = MainApplication.Services.GetService<PlayQueue>()?.CurrentSong;
        int currentSongId = currentSong?.Id ?? -1;
        bool isPlaying = MainApplication.Services.GetService<IAudioPlayerService>()?.IsPlaying ?? false;
    }

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
        PlaylistAdapter? adapter = null;
        adapter = new PlaylistAdapter(allSongs, currentSong, dp, song =>
        {
            PlaySong(song);
            adapter?.SetCurrentSong(song);
            var newIdx = allSongs.FindIndex(s => s.Id == song.Id);
            if (newIdx >= 0)
                recyclerView.SmoothScrollToPosition(newIdx);
        });
        recyclerView.SetAdapter(adapter);
        recyclerView.Post(() =>
        {
            if (recyclerView.Height > maxH)
                recyclerView.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, maxH);
            if (currentSong != null)
            {
                var idx = allSongs.FindIndex(s => s.Id == currentSong.Id);
                if (idx >= 0) recyclerView.ScrollToPosition(idx);
            }
        });
        var dialog = new GlassDialog(act).SetTitle("播放列表", $"{allSongs.Count} 首歌曲").AddCustomView(recyclerView).AddNegativeButton("关闭");
        dialog.Show();
        _playlistDialog = null;
    }

    private void PlaySong(Song song)
    {
        var queue = MainApplication.Services.GetRequiredService<PlayQueue>();
        var player = MainApplication.Services.GetRequiredService<IAudioPlayerService>();
        queue.SelectSong(song.Id);
        _viewModel.CurrentSong = song;
        _ = player.PlayAsync(song.FilePath);
        Task.Delay(500).ContinueWith(_ => Activity?.RunOnUiThread(SyncUIFromViewModel));
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
            btn.LayoutParameters = new GridLayout.LayoutParams() { Width = btnSize, Height = btnSize, MarginStart = dp * 6, MarginEnd = dp * 6, TopMargin = dp * 4, BottomMargin = dp * 4 };
            var btnBg = new GradientDrawable(); btnBg.SetShape(ShapeType.Rectangle); btnBg.SetCornerRadius(btnSize / 2f); btnBg.SetColor(Color.ParseColor("#1AFFFFFF")); btnBg.SetStroke(1, Color.ParseColor("#30FFFFFF"));
            btn.Background = btnBg; btn.SetTextColor(Color.ParseColor("#DDFFFFFF")); btn.Clickable = true; btn.Focusable = true;
            var capturedMins = mins;
            btn.Click += (s, e) =>
            {
                selectedMinutes = capturedMins;
                foreach (var b in timeButtons) { b.SetTextColor(Color.ParseColor("#DDFFFFFF")); var bg = new GradientDrawable(); bg.SetShape(ShapeType.Rectangle); bg.SetCornerRadius(btnSize / 2f); bg.SetColor(Color.ParseColor("#1AFFFFFF")); bg.SetStroke(1, Color.ParseColor("#30FFFFFF")); b.Background = bg; }
                var selBg = new GradientDrawable(); selBg.SetShape(ShapeType.Rectangle); selBg.SetCornerRadius(btnSize / 2f); selBg.SetColor(themeColor); selBg.SetStroke(0, Color.Transparent); btn.Background = selBg; btn.SetTextColor(Color.White);
            };
            timeButtons.Add(btn);
            timeGrid.AddView(btn);
        }
        content.AddView(timeGrid);
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
        new GlassDialog(act).SetTitle("定时关闭").AddCustomView(content).AddPositiveButton("开始", (input) =>
        {
            if (!timerToggle.Checked) return;
            if (selectedMinutes > 0) StartSleepTimer(selectedMinutes * 60, finishSong);
        }).AddNegativeButton("取消").Show();
    }

    private void StartSleepTimer(int totalSeconds, bool finishSong)
    {
        StopSleepTimer();
        _sleepCts = new CancellationTokenSource();
        _sleepRemainingSeconds = totalSeconds;
        _sleepFinishSong = finishSong;
        var token = _sleepCts.Token;
        Task.Run(async () =>
        {
            try
            {
                while (_sleepRemainingSeconds > 0 && !token.IsCancellationRequested)
                {
                    await Task.Delay(1000, token);
                    _sleepRemainingSeconds--;
                }
                if (!token.IsCancellationRequested) Activity?.RunOnUiThread(ExecuteSleepStop);
            }
            catch (TaskCanceledException) { }
        }, token);
    }

    private void ExecuteSleepStop()
    {
        var player = MainApplication.Services.GetRequiredService<IAudioPlayerService>();
        if (_sleepFinishSong) { _viewModel.StopAfterCurrentSong = true; StopSleepTimer(); }
        else { _ = player.PauseAsync(); StopSleepTimer(); }
    }

    private void StopSleepTimer()
    {
        _sleepCts?.Cancel(); _sleepCts?.Dispose(); _sleepCts = null;
        _sleepRemainingSeconds = 0; _sleepFinishSong = false;
        var gray = Android.Content.Res.ColorStateList.ValueOf(Color.ParseColor("#88FFFFFF"));
        _btnSleepTimer.ImageTintList = gray;
    }

    private int _lastVisualizerSessionId;

    /// <summary>
    /// 音频会话 ID 变化回调。与 NowPlayingFragment 逻辑一致。
    /// 同一 SessionId 时无需重建，Visualizer 会自动接收新轨道 FFT 数据。
    /// </summary>
    private void OnAudioSessionIdChanged(int newSessionId)
    {
        if (Activity == null) return;
        Activity.RunOnUiThread(() =>
        {
            if (!_visualizerEnabled) return;
            if (newSessionId == 0) return;

            if (_visualizerHelper != null && _visualizerHelper.IsEnabled && newSessionId == _lastVisualizerSessionId)
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

    private void TryStartVisualizer()
    {
        var playerService = MainApplication.Services.GetRequiredService<IAudioPlayerService>();
        var sessionId = playerService.AudioSessionId;
        if (sessionId == 0) return;
        if (_visualizerHelper != null && _visualizerHelper.IsEnabled && sessionId == _lastVisualizerSessionId) return;
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
        var spectrumCounter = 0;
        var lastUpdateTicks = 0L;
        _visualizerHelper.SpectrumUpdated += spectrum =>
        {
            var src = spectrum;
            if (_latestSpectrum.Length < src.Length) _latestSpectrum = new float[src.Length];
            Array.Copy(src, _latestSpectrum, src.Length);
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

    internal class ControlsTouchListener : Java.Lang.Object, View.IOnTouchListener
    {
        private float _downX, _downY;
        public bool OnTouch(View? v, MotionEvent? e)
        {
            if (e == null || v == null) return false;
            switch (e.Action)
            {
                case MotionEventActions.Down: _downX = e.GetX(); _downY = e.GetY(); break;
                case MotionEventActions.Move:
                    {
                        float dx = Math.Abs(e.GetX() - _downX); float dy = Math.Abs(e.GetY() - _downY);
                        if (dx > 20 && dx > dy * 1.5f)
                        {
                            var parent = v.Parent; while (parent != null) { parent.RequestDisallowInterceptTouchEvent(false); parent = parent.Parent; }
                            return false;
                        }
                        var p = v.Parent; while (p != null) { p.RequestDisallowInterceptTouchEvent(true); p = p.Parent; }
                    }
                    break;
                case MotionEventActions.Up:
                case MotionEventActions.Cancel:
                    {
                        var parent = v.Parent; while (parent != null) { parent.RequestDisallowInterceptTouchEvent(false); parent = parent.Parent; }
                    }
                    break;
            }
            return false;
        }
    }

    internal class PlaylistAdapter : AndroidX.RecyclerView.Widget.RecyclerView.Adapter
    {
        private readonly List<Song> _songs;
        private Song? _currentSong;
        private readonly int _dp;
        private readonly Action<Song> _onSongClick;
        public PlaylistAdapter(List<Song> songs, Song? currentSong, int dp, Action<Song> onSongClick) { _songs = songs; _currentSong = currentSong; _dp = dp; _onSongClick = onSongClick; }
        public override int ItemCount => _songs.Count;

        /// <summary>更新当前播放歌曲并刷新列表</summary>
        public void SetCurrentSong(Song? song)
        {
            if (_currentSong != null && song != null && _currentSong.Id == song.Id) return;
            _currentSong = song;
            NotifyDataSetChanged();
        }

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
            var tv = new TextView(parent.Context!) { TextSize = 13f };
            tv.SetSingleLine(true); tv.Ellipsize = Android.Text.TextUtils.TruncateAt.End; tv.Clickable = true; tv.Focusable = true;
            return new PlaylistViewHolder(tv);
        }
    }

    internal class PlaylistViewHolder : AndroidX.RecyclerView.Widget.RecyclerView.ViewHolder
    {
        public TextView TextView { get; }
        public EventHandler? Handler;
        public PlaylistViewHolder(TextView tv) : base(tv) => TextView = tv;
    }
}
