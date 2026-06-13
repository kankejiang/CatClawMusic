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
    private StrokeTextView _lyricPrev8 = null!, _lyricPrev7 = null!, _lyricPrev6 = null!, _lyricPrev5 = null!, _lyricPrev4 = null!, _lyricPrev3 = null!, _lyricPrev2 = null!, _lyricPrev = null!, _lyricCurrent = null!, _lyricNext = null!, _lyricNext2 = null!, _lyricNext3 = null!, _lyricNext4 = null!, _lyricNext5 = null!, _lyricNext6 = null!, _lyricNext7 = null!, _lyricNext8 = null!;
    private View _coverPanel = null!;
    private LinearLayout _lyricsArea = null!;
    private bool _isFullLyricMode;
    private TextView _timeCurrent = null!, _timeTotal = null!;
    private ImageButton _btnPlayPause = null!, _btnNext = null!, _btnPrev = null!;
    private ImageButton _btnLike = null!, _btnModeCycle = null!, _btnPlaylist = null!;
    private ImageButton _btnVisualizerToggle = null!;
    private ImageButton _btnEq = null!;
    private ImageButton _btnSleepTimer = null!;
    private ImageButton _btnBack = null!;
    private GoogleSlider _progressSlider = null!;
    private SweepGradientView _gradientBackground = null!;
    private View _reflectionMaskBottom = null!, _coverFog = null!, _coverGlow = null!;
    private Google.Android.Material.Card.MaterialCardView _controlsCard = null!;
    private AudioVisualizerView _audioVisualizer = null!;
    private VisualizerHelper? _visualizerHelper;
    private Android.OS.Handler? _mainHandler;
    private int _spectrumUpdateQueued;
    private float[] _latestSpectrum = Array.Empty<float>();
    private ActivityResultLauncher? _recordAudioLauncher;
    private int _modeActiveColor;
    private bool _visualizerEnabled = false;
    private bool _recordAudioDenied;
    private string? _lastCoverSource;
    private CancellationTokenSource? _sleepCts;
    private int _sleepRemainingSeconds;
    private bool _sleepFinishSong;
    private Android.Animation.ValueAnimator? _flowAnimator;
    private Android.Animation.ValueAnimator? _colorTransitionAnimator;
    private int _currentBackgroundColor;
    private int _targetBackgroundColor;
    private List<ColorEntry>? _currentEntries;
    private int[]? _flowColors;
    private float[]? _flowPositions;

    private bool _flowPaused;
    private long _flowPauseTime;
    private long _flowTimeOffset;
    private float _sweepAngle;
    private int _flowFrameSkip;
    private readonly Android.Views.Animations.DecelerateInterpolator _lyricInterpolator = new(1.5f);
    private Android.App.Dialog? _playlistDialog;

    // --- 歌词自定义设置（与 FullLyricsFragment 共享） ---
    private static readonly string[] LyricActiveColorHex   = { "#FFFFFFFF", "#FFFFEB3B", "#FF69F0AE", "#FFFF80AB", "#FF64B5F6", "#FFFFAB40", "#FFFF6E6E", "#FFCE93D8", "#FF4DD0E1", "#FFFFD54F" };
    private static readonly string[] LyricInactiveColorHex = { "#CCBBBBBB", "#DDDDDDDD", "#CC90A4AE", "#CCB39DDB", "#CCBDBDBD", "#CCA8B8C8", "#CC78909C", "#CCD7CCC8" };
    /// <summary>背景遮罩颜色预设（与 FullLyricsFragment 共享）</summary>
    private static readonly string[] BgColorHex = { "#CCF0EBE3", "#CC0F0D16", "#00000000" };

    private Color _lpLyricActiveColor = Color.White;
    private Color _lpLyricInactiveColor = Color.ParseColor("#CCBBBBBB");
    private int _lpLyricFontSize = 16;
    private bool _lpLyricBold = true;
    private int _lpLyricColorMode = 0; // 0=自适应, 1=自定义
    private float _currentBgLuminance = 0.3f;
    private int _lyricBgColorIndex = 0;
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
        _lyricPrev8 = (StrokeTextView)view.FindViewById(Resource.Id.lyric_prev8)!;
        _lyricPrev7 = (StrokeTextView)view.FindViewById(Resource.Id.lyric_prev7)!;
        _lyricPrev6 = (StrokeTextView)view.FindViewById(Resource.Id.lyric_prev6)!;
        _lyricPrev5 = (StrokeTextView)view.FindViewById(Resource.Id.lyric_prev5)!;
        _lyricPrev4 = (StrokeTextView)view.FindViewById(Resource.Id.lyric_prev4)!;
        _lyricPrev3 = (StrokeTextView)view.FindViewById(Resource.Id.lyric_prev3)!;
        _lyricPrev2 = (StrokeTextView)view.FindViewById(Resource.Id.lyric_prev2)!;
        _lyricPrev = (StrokeTextView)view.FindViewById(Resource.Id.lyric_prev)!;
        _lyricCurrent = (StrokeTextView)view.FindViewById(Resource.Id.lyric_current)!;
        _lyricNext = (StrokeTextView)view.FindViewById(Resource.Id.lyric_next)!;
        _lyricNext2 = (StrokeTextView)view.FindViewById(Resource.Id.lyric_next2)!;
        _lyricNext3 = (StrokeTextView)view.FindViewById(Resource.Id.lyric_next3)!;
        _lyricNext4 = (StrokeTextView)view.FindViewById(Resource.Id.lyric_next4)!;
        _lyricNext5 = (StrokeTextView)view.FindViewById(Resource.Id.lyric_next5)!;
        _lyricNext6 = (StrokeTextView)view.FindViewById(Resource.Id.lyric_next6)!;
        _lyricNext7 = (StrokeTextView)view.FindViewById(Resource.Id.lyric_next7)!;
        _lyricNext8 = (StrokeTextView)view.FindViewById(Resource.Id.lyric_next8)!;
        _coverPanel = view.FindViewById<View>(Resource.Id.cover_panel)!;
        _lyricsArea = (LinearLayout)view.FindViewById(Resource.Id.lyrics_area)!;
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

        _gradientBackground = view.FindViewById<SweepGradientView>(Resource.Id.gradient_background)!;
        _reflectionMaskBottom = view.FindViewById<View>(Resource.Id.reflection_mask_bottom)!;
        _coverFog = view.FindViewById<View>(Resource.Id.cover_fog)!;
        _coverGlow = view.FindViewById<View>(Resource.Id.cover_glow)!;
        _controlsCard = view.FindViewById<Google.Android.Material.Card.MaterialCardView>(Resource.Id.controls_card)!;
        _audioVisualizer = view.FindViewById<AudioVisualizerView>(Resource.Id.audio_visualizer)!;
        _bgDimOverlay = view.FindViewById<View>(Resource.Id.bg_dim_overlay);

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

        _lyricsArea.Click += OnLyricsAreaClick;

        _progressSlider.SetOnTouchListener(new SliderTouchListener(v => _viewModel.CurrentPositionSeconds = v));

        _audioVisualizer.Visibility = ViewStates.Gone;
        var visPrefs = Activity?.GetSharedPreferences("catclaw_prefs", Android.Content.FileCreationMode.Private);
        var visEnabled = visPrefs?.GetBoolean("visualizer_enabled", false) ?? false;
        if (visEnabled)
            ApplyVisualizerState(true);

        // 加载歌词颜色和字号设置
        LoadLyricSettings();
        ApplyLyricFontSize();

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
        controller.Show(WindowInsetsCompat.Type.StatusBars());
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
        if (_coverPanel == null) return;
        var duration = 300L;
        if (_isFullLyricMode)
        {
            _lyricsArea.SetGravity(GravityFlags.CenterVertical | GravityFlags.CenterHorizontal);
            _audioVisualizer.Animate().Alpha(0f).SetDuration(duration).WithEndAction(new Java.Lang.Runnable(() => _audioVisualizer.Visibility = ViewStates.Gone)).Start();
            _controlsCard.Animate().Alpha(0f).TranslationY(40f).SetDuration(duration).WithEndAction(new Java.Lang.Runnable(() => _controlsCard.Visibility = ViewStates.Gone)).Start();
            _lyricPrev8.Visibility = ViewStates.Visible;
            _lyricPrev7.Visibility = ViewStates.Visible;
            _lyricPrev6.Visibility = ViewStates.Visible;
            _lyricPrev5.Visibility = ViewStates.Visible;
            _lyricPrev4.Visibility = ViewStates.Visible;
            _lyricPrev3.Visibility = ViewStates.Visible;
            _lyricNext3.Visibility = ViewStates.Visible;
            _lyricNext4.Visibility = ViewStates.Visible;
            _lyricNext5.Visibility = ViewStates.Visible;
            _lyricNext6.Visibility = ViewStates.Visible;
            _lyricNext7.Visibility = ViewStates.Visible;
            _lyricNext8.Visibility = ViewStates.Visible;
            _lyricPrev8.Alpha = 0f; _lyricPrev8.Animate().Alpha(0.18f).SetDuration(duration).Start();
            _lyricPrev7.Alpha = 0f; _lyricPrev7.Animate().Alpha(0.22f).SetDuration(duration).Start();
            _lyricPrev6.Alpha = 0f; _lyricPrev6.Animate().Alpha(0.28f).SetDuration(duration).Start();
            _lyricPrev5.Alpha = 0f; _lyricPrev5.Animate().Alpha(0.33f).SetDuration(duration).Start();
            _lyricPrev4.Alpha = 0f; _lyricPrev4.Animate().Alpha(0.40f).SetDuration(duration).Start();
            _lyricPrev3.Alpha = 0f; _lyricPrev3.Animate().Alpha(0.50f).SetDuration(duration).Start();
            _lyricNext3.Alpha = 0f; _lyricNext3.Animate().Alpha(0.50f).SetDuration(duration).Start();
            _lyricNext4.Alpha = 0f; _lyricNext4.Animate().Alpha(0.40f).SetDuration(duration).Start();
            _lyricNext5.Alpha = 0f; _lyricNext5.Animate().Alpha(0.33f).SetDuration(duration).Start();
            _lyricNext6.Alpha = 0f; _lyricNext6.Animate().Alpha(0.28f).SetDuration(duration).Start();
            _lyricNext7.Alpha = 0f; _lyricNext7.Animate().Alpha(0.22f).SetDuration(duration).Start();
            _lyricNext8.Alpha = 0f; _lyricNext8.Animate().Alpha(0.18f).SetDuration(duration).Start();
        }
        else
        {
            _lyricsArea.SetGravity(GravityFlags.Bottom | GravityFlags.CenterHorizontal);
            _audioVisualizer.Alpha = 0f;
            _audioVisualizer.Visibility = _visualizerEnabled ? ViewStates.Visible : ViewStates.Gone;
            if (_visualizerEnabled) _audioVisualizer.Animate().Alpha(1f).SetDuration(duration).Start();
            _controlsCard.Alpha = 0f; _controlsCard.TranslationY = 40f;
            _controlsCard.Visibility = ViewStates.Visible;
            _controlsCard.Animate().Alpha(1f).TranslationY(0f).SetDuration(duration).Start();
            _lyricPrev8.Animate().Alpha(0f).SetDuration(duration).WithEndAction(new Java.Lang.Runnable(() => _lyricPrev8.Visibility = ViewStates.Gone)).Start();
            _lyricPrev7.Animate().Alpha(0f).SetDuration(duration).WithEndAction(new Java.Lang.Runnable(() => _lyricPrev7.Visibility = ViewStates.Gone)).Start();
            _lyricPrev6.Animate().Alpha(0f).SetDuration(duration).WithEndAction(new Java.Lang.Runnable(() => _lyricPrev6.Visibility = ViewStates.Gone)).Start();
            _lyricPrev5.Animate().Alpha(0f).SetDuration(duration).WithEndAction(new Java.Lang.Runnable(() => _lyricPrev5.Visibility = ViewStates.Gone)).Start();
            _lyricPrev4.Animate().Alpha(0f).SetDuration(duration).WithEndAction(new Java.Lang.Runnable(() => _lyricPrev4.Visibility = ViewStates.Gone)).Start();
            _lyricPrev3.Animate().Alpha(0f).SetDuration(duration).WithEndAction(new Java.Lang.Runnable(() => _lyricPrev3.Visibility = ViewStates.Gone)).Start();
            _lyricNext3.Animate().Alpha(0f).SetDuration(duration).WithEndAction(new Java.Lang.Runnable(() => _lyricNext3.Visibility = ViewStates.Gone)).Start();
            _lyricNext4.Animate().Alpha(0f).SetDuration(duration).WithEndAction(new Java.Lang.Runnable(() => _lyricNext4.Visibility = ViewStates.Gone)).Start();
            _lyricNext5.Animate().Alpha(0f).SetDuration(duration).WithEndAction(new Java.Lang.Runnable(() => _lyricNext5.Visibility = ViewStates.Gone)).Start();
            _lyricNext6.Animate().Alpha(0f).SetDuration(duration).WithEndAction(new Java.Lang.Runnable(() => _lyricNext6.Visibility = ViewStates.Gone)).Start();
            _lyricNext7.Animate().Alpha(0f).SetDuration(duration).WithEndAction(new Java.Lang.Runnable(() => _lyricNext7.Visibility = ViewStates.Gone)).Start();
            _lyricNext8.Animate().Alpha(0f).SetDuration(duration).WithEndAction(new Java.Lang.Runnable(() => _lyricNext8.Visibility = ViewStates.Gone)).Start();
        }
        UpdateLyrics();
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
            LoadLyricSettings();
            ApplyLyricFontSize();
            if (!string.IsNullOrEmpty(_viewModel.CoverSource))
                UpdateGradientBackground();
            else
                ApplyDefaultLyricColors();
            SyncUIFromViewModel();
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
            UpdateLyrics();
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
        _isFullLyricMode = false;
        StopFlowAnimation();
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
            _lastCoverSource = coverSource;

            if (!string.IsNullOrEmpty(coverSource))
            {
                if (coverChanged)
                {
                    AnimateCoverChange(coverSource);
                    UpdateGradientBackground();
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
            UpdateLyrics();
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
                        _lastCoverSource = cover;
                        Activity?.RunOnUiThread(() =>
                        {
                            AnimateCoverChange(cover);
                            UpdateGradientBackground();
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
                        PauseFlowAnimation();
                    else
                        ResumeFlowAnimation();
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
                    // 切歌时重启频谱图，防止 AudioSessionIdChanged 未触发导致频谱停止
                    RestartVisualizer();
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

    private void UpdateGradientBackground()
    {
        if (_gradientBackground == null) return;
        var coverPath = _viewModel.CoverSource;
        if (string.IsNullOrEmpty(coverPath) || !System.IO.File.Exists(coverPath))
        {
            ApplyDefaultBackground();
            return;
        }
        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var colors = CoverColorExtractor.ExtractFromFile(coverPath);
                if (colors.Count == 0) return;
                Activity?.RunOnUiThread(() => TransitionToColors(colors));
            }
            catch { }
        });
    }

    private void TransitionToColors(List<ColorEntry> newEntries)
    {
        if (_gradientBackground == null || newEntries.Count == 0) return;
        var newPalette = MaterialYouPalette.FromSeedColor(newEntries[0].Color);
        _targetBackgroundColor = newPalette.Background;
        if (_currentEntries == null || _currentEntries.Count == 0)
        {
            _gradientBackground.SetBackgroundColor(new Android.Graphics.Color(_targetBackgroundColor));
            _currentEntries = newEntries;
            _currentBackgroundColor = _targetBackgroundColor;
            _flowColors = PickVividColors(newEntries);
            ApplySweepGradientBackground(newEntries);
            StartFlowAnimation();
            return;
        }
        _colorTransitionAnimator?.Cancel();
        _colorTransitionAnimator = Android.Animation.ValueAnimator.OfFloat(0f, 1f);
        _colorTransitionAnimator.SetDuration(800);
        _colorTransitionAnimator.SetInterpolator(new Android.Views.Animations.AccelerateDecelerateInterpolator());
        var oldColors = _flowColors != null ? (int[])_flowColors.Clone() : null;
        var newColors = PickVividColors(newEntries);
        _colorTransitionAnimator.Update += (s, e) =>
        {
            var fraction = (float)e.Animation.AnimatedValue;
            if (oldColors != null && _flowColors != null && newColors != null)
            {
                var len = Math.Min(oldColors.Length, newColors.Length);
                for (int i = 0; i < len - 1; i++)
                    _flowColors[i] = BlendColor(oldColors[i], newColors[i], fraction);
                _flowColors[_flowColors.Length - 1] = _flowColors[0];
            }
        };
        _colorTransitionAnimator.AnimationEnd += (s, e) =>
        {
            _currentEntries = newEntries;
            _currentBackgroundColor = _targetBackgroundColor;
            ApplySweepGradientBackground(newEntries);
        };
        _colorTransitionAnimator.Start();
    }

    private static int BlendColor(int from, int to, float fraction)
    {
        var a = (int)(Color.GetAlphaComponent(from) + (Color.GetAlphaComponent(to) - Color.GetAlphaComponent(from)) * fraction);
        var r = (int)(Color.GetRedComponent(from) + (Color.GetRedComponent(to) - Color.GetRedComponent(from)) * fraction);
        var g = (int)(Color.GetGreenComponent(from) + (Color.GetGreenComponent(to) - Color.GetGreenComponent(from)) * fraction);
        var b = (int)(Color.GetBlueComponent(from) + (Color.GetBlueComponent(to) - Color.GetBlueComponent(from)) * fraction);
        return Color.Argb(a, r, g, b);
    }

    private void StartFlowAnimation()
    {
        if (_flowAnimator != null) return;

        var prefs = Activity?.GetSharedPreferences("catclaw_prefs", Android.Content.FileCreationMode.Private);
        bool bgAnimEnabled = prefs?.GetBoolean("bg_animation_enabled", false) ?? false;

        if (!bgAnimEnabled) return;

        _flowTimeOffset = SystemClock.ElapsedRealtime();
        _sweepAngle = 0f;
        _flowAnimator = Android.Animation.ValueAnimator.OfFloat(0f, 1f);
        _flowAnimator.SetDuration(24000);
        _flowAnimator.RepeatCount = -1;
        _flowAnimator.RepeatMode = Android.Animation.ValueAnimatorRepeatMode.Restart;
        _flowAnimator.SetInterpolator(new Android.Views.Animations.LinearInterpolator());
        _flowAnimator.Update += (s, e) =>
        {
            if (++_flowFrameSkip % 3 != 0) return;
            var rawTime = SystemClock.ElapsedRealtime();
            var t = (rawTime - _flowTimeOffset) / 1000f;
            _sweepAngle = (t * 15f) % 360f;
            _gradientBackground?.SetRotationAngle(_sweepAngle);
        };
        _flowAnimator.Start();
        if (_flowPaused || _viewModel.PlayPauseIcon == "▶")
        {
            _flowPaused = true;
            _flowAnimator.Pause();
        }
    }

    private void PauseFlowAnimation()
    {
        _flowPaused = true;
        _flowPauseTime = SystemClock.ElapsedRealtime();
        if (_flowAnimator != null && _flowAnimator.IsRunning)
            _flowAnimator.Pause();
    }

    private void ResumeFlowAnimation()
    {
        _flowPaused = false;
        _flowTimeOffset += SystemClock.ElapsedRealtime() - _flowPauseTime;
        if (_flowAnimator != null && _flowAnimator.IsStarted)
            _flowAnimator.Resume();
    }

    private void StopFlowAnimation()
    {
        _flowAnimator?.Cancel();
        _flowAnimator = null;
        _colorTransitionAnimator?.Cancel();
        _colorTransitionAnimator = null;
        _flowPaused = false;
        _flowTimeOffset = 0;
        _flowPauseTime = 0;
        _sweepAngle = 0f;
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
    /// 从 SharedPreferences 加载歌词颜色和字号设置（与 FullLyricsFragment 共享）
    /// </summary>
    private void LoadLyricSettings()
    {
        var prefs = Activity?.GetSharedPreferences("lyric_settings", Android.Content.FileCreationMode.Private);
        if (prefs == null) return;

        _lpLyricFontSize = prefs.GetInt("lyric_font_size", 16);
        _lpLyricBold = prefs.GetBoolean("lyric_bold", true);
        _lpLyricColorMode = prefs.GetInt("lyric_color_mode", 0);
        _lyricBgColorIndex = prefs.GetInt("lyric_bg_color", 0);
        UpdateBgOverlay();

        // 读取 ARGB 颜色值（向后兼容旧版索引存储）
        if (prefs.Contains("lyric_active_argb"))
        {
            _lpLyricActiveColor = new Color(prefs.GetInt("lyric_active_argb", unchecked((int)0xFFFFFFFF)));
        }
        else
        {
            var activeIdx = prefs.GetInt("lyric_active_color", 0);
            _lpLyricActiveColor = Color.ParseColor(LyricActiveColorHex[Math.Clamp(activeIdx, 0, LyricActiveColorHex.Length - 1)]);
        }
        if (prefs.Contains("lyric_inactive_argb"))
        {
            _lpLyricInactiveColor = new Color(prefs.GetInt("lyric_inactive_argb", unchecked((int)0xCCBBBBBB)));
        }
        else
        {
            var inactiveIdx = prefs.GetInt("lyric_inactive_color", 0);
            _lpLyricInactiveColor = Color.ParseColor(LyricInactiveColorHex[Math.Clamp(inactiveIdx, 0, LyricInactiveColorHex.Length - 1)]);
        }
    }

    /// <summary>
    /// 将歌词字号和加粗设置应用到歌词视图（按比例缩放）
    /// </summary>
    private void ApplyLyricFontSize()
    {
        float baseSp = _lpLyricFontSize;
        var boldStyle = _lpLyricBold ? TypefaceStyle.Bold : TypefaceStyle.Normal;
        // 横屏 XML 默认比例: 7:7:8:9:9:10:10:12:15:12:10:10:9:9:8:7:7
        _lyricCurrent.TextSize = baseSp;
        _lyricPrev.TextSize = baseSp * 0.8f;      // 12/15
        _lyricNext.TextSize = baseSp * 0.8f;
        _lyricPrev2.TextSize = baseSp * 0.667f;   // 10/15
        _lyricNext2.TextSize = baseSp * 0.667f;
        _lyricPrev3.TextSize = baseSp * 0.667f;
        _lyricNext3.TextSize = baseSp * 0.667f;
        _lyricPrev4.TextSize = baseSp * 0.6f;     // 9/15
        _lyricNext4.TextSize = baseSp * 0.6f;
        _lyricPrev5.TextSize = baseSp * 0.6f;
        _lyricNext5.TextSize = baseSp * 0.6f;
        _lyricPrev6.TextSize = baseSp * 0.533f;   // 8/15
        _lyricNext6.TextSize = baseSp * 0.533f;
        _lyricPrev7.TextSize = baseSp * 0.467f;   // 7/15
        _lyricNext7.TextSize = baseSp * 0.467f;
        _lyricPrev8.TextSize = baseSp * 0.467f;   // 7/15
        _lyricNext8.TextSize = baseSp * 0.467f;
        // 应用加粗
        var allViews = new[] { _lyricPrev8, _lyricPrev7, _lyricPrev6, _lyricPrev5, _lyricPrev4, _lyricPrev3, _lyricPrev2, _lyricPrev, _lyricCurrent, _lyricNext, _lyricNext2, _lyricNext3, _lyricNext4, _lyricNext5, _lyricNext6, _lyricNext7, _lyricNext8 };
        foreach (var v in allViews)
            v.SetTypeface(null, boldStyle);
    }

    /// <summary>
    /// 仅刷新歌词颜色（不改变背景），用于从歌词页返回时同步设置
    /// 覆盖全部13行歌词，远端按距离逐级降低透明度
    /// </summary>
    private void ApplyDefaultLyricColors()
    {
        // 自适应模式：透明/半透明遮罩用封面亮度，不透明遮罩用遮罩自身亮度
        if (_lpLyricColorMode == 0)
        {
            var overlayAlpha = 0f;
            if (_bgDimOverlay?.Background is ColorDrawable cd)
                overlayAlpha = cd.Color.A / 255f;
            var luminance = overlayAlpha < 0.5f ? _currentBgLuminance : GetBgOverlayLuminance();

            if (luminance >= 0.5f)
            {
                _lpLyricActiveColor = Color.Black;
                _lpLyricInactiveColor = Color.ParseColor("#88333333");
            }
            else
            {
                _lpLyricActiveColor = Color.White;
                _lpLyricInactiveColor = Color.ParseColor("#88BBBBBB");
            }
        }

        // 当前行：活跃色
        _lyricCurrent.SetTextColor(_lpLyricActiveColor);
        _lyricCurrent.SungColor = _lpLyricActiveColor;
        _lyricCurrent.UnsungColor = _lpLyricInactiveColor;
        _lyricCurrent.StrokeEnabled = false;

        // 远端行：按距离逐级降低透明度（offset 1~8）
        var allFarViews = new[] {
            (View: (StrokeTextView)_lyricPrev, Offset: 1), (View: (StrokeTextView)_lyricNext, Offset: 1),
            (View: (StrokeTextView)_lyricPrev2, Offset: 2), (View: (StrokeTextView)_lyricNext2, Offset: 2),
            (View: (StrokeTextView)_lyricPrev3, Offset: 3), (View: (StrokeTextView)_lyricNext3, Offset: 3),
            (View: (StrokeTextView)_lyricPrev4, Offset: 4), (View: (StrokeTextView)_lyricNext4, Offset: 4),
            (View: (StrokeTextView)_lyricPrev5, Offset: 5), (View: (StrokeTextView)_lyricNext5, Offset: 5),
            (View: (StrokeTextView)_lyricPrev6, Offset: 6), (View: (StrokeTextView)_lyricNext6, Offset: 6),
            (View: (StrokeTextView)_lyricPrev7, Offset: 7), (View: (StrokeTextView)_lyricNext7, Offset: 7),
            (View: (StrokeTextView)_lyricPrev8, Offset: 8), (View: (StrokeTextView)_lyricNext8, Offset: 8),
        };

        foreach (var (view, offset) in allFarViews)
        {
            var alpha = (byte)(_lpLyricInactiveColor.A / (offset + 1));
            var color = Color.Argb(alpha, _lpLyricInactiveColor.R, _lpLyricInactiveColor.G, _lpLyricInactiveColor.B);
            view.SetTextColor(color);
            view.StrokeEnabled = false;
        }
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

    private void ApplySweepGradientBackground(List<ColorEntry> entries)
    {
        if (_gradientBackground == null || entries.Count == 0) return;
        var palette = MaterialYouPalette.FromSeedColor(entries[0].Color);

        // 根据种子色计算背景亮度，用于自适应歌词颜色
        int seedR = Android.Graphics.Color.GetRedComponent(entries[0].Color);
        int seedG = Android.Graphics.Color.GetGreenComponent(entries[0].Color);
        int seedB = Android.Graphics.Color.GetBlueComponent(entries[0].Color);
        _currentBgLuminance = (0.299f * seedR + 0.587f * seedG + 0.114f * seedB) / 255f;

        var sorted = entries.OrderByDescending(e => e.Weight).ToList();
        var totalWeight = sorted.Sum(e => Math.Max(e.Weight, 0.01f));
        var topEntries = sorted.Where(e => e.Weight / totalWeight >= 0.20f).Take(3).ToList();
        if (topEntries.Count == 0) topEntries.Add(sorted[0]);
        if (topEntries.Count == 1) topEntries.Add(topEntries[0]);
        _flowColors = PickVividColors(topEntries);
        _flowPositions = CalculateWeightedPositions(topEntries);
        BuildAndApplySweepGradient();
        if (_reflectionMaskBottom != null) _reflectionMaskBottom.Background = null;
        ApplyFogToCover(palette.Background);
        ApplyCoverGlow(entries);
        ApplyCardTheme(palette);
        var onSurfaceColor = new Color(palette.OnSurface);
        _songTitle.SetTextColor(onSurfaceColor);
        _songArtist.SetTextColor(new Color(palette.OnSurfaceVariant));
        ApplyDefaultLyricColors();
    }

    private void BuildAndApplySweepGradient()
    {
        if (_gradientBackground == null || _flowColors == null || _flowColors.Length < 3) return;
        var positions = _flowPositions;
        if (positions == null || positions.Length != _flowColors.Length)
        {
            var n = _flowColors.Length - 1;
            positions = new float[_flowColors.Length];
            for (int i = 0; i <= n; i++) positions[i] = (float)i / n;
            _flowPositions = positions;
        }
        _gradientBackground.SetGradient(_flowColors!, positions);
        _gradientBackground.SetRotationAngle(_sweepAngle);
    }

    private static float[] CalculateWeightedPositions(List<ColorEntry> entries)
    {
        if (entries.Count < 2) return new float[] { 0f, 0.5f, 1f };
        var totalWeight = entries.Sum(e => Math.Max(e.Weight, 0.01f));
        var positions = new float[entries.Count + 1];
        positions[0] = 0f;
        var cumulative = 0f;
        for (int i = 0; i < entries.Count; i++) { cumulative += entries[i].Weight / totalWeight; positions[i + 1] = Math.Clamp(cumulative, 0f, 1f); }
        return positions;
    }

    private static int[] PickVividColors(List<ColorEntry> entries)
    {
        if (entries.Count == 0) return new[] { (int)Color.Argb(0xFF, 0xF0, 0xF2, 0xF5), (int)Color.Argb(0xFF, 0xEE, 0xF0, 0xF5), (int)Color.Argb(0xFF, 0xF0, 0xF0, 0xF5), (int)Color.Argb(0xFF, 0xF0, 0xF2, 0xF5) };
        var n = entries.Count;
        var result = new int[n + 1];
        for (int i = 0; i < n; i++)
        {
            float[] hsv = { 0, 0, 0 };
            Color.RGBToHSV(Color.GetRedComponent(entries[i].Color), Color.GetGreenComponent(entries[i].Color), Color.GetBlueComponent(entries[i].Color), hsv);
            if (hsv[1] < 0.15f)
            {
                // 低饱和度颜色：保持微妙色调，明度根据原始亮度适度调整
                float sat = Math.Clamp(hsv[2] * 0.08f, 0.02f, 0.08f);
                float val = Math.Clamp(hsv[2] * 0.45f + 0.40f, 0.25f, 0.90f);
                result[i] = (int)Color.HSVToColor(new[] { hsv[0], sat, val });
            }
            else
            {
                // 正常颜色：保持中等饱和度，明度根据原始亮度自适应
                float sat = Math.Clamp(hsv[1] * 0.55f + 0.10f, 0.12f, 0.45f);
                float val = Math.Clamp(hsv[2] * 0.50f + 0.40f, 0.55f, 0.95f);
                result[i] = (int)Color.HSVToColor(new[] { hsv[0], sat, val });
            }
        }
        result[n] = result[0];
        return result;
    }

    private void ApplyFogToCover(int backgroundColor)
    {
        if (_coverFog == null) return;
        var fog = new GradientDrawable(GradientDrawable.Orientation.TopBottom, new int[] { Color.Argb(0, 0, 0, 0), backgroundColor });
        fog.SetGradientType(GradientType.LinearGradient);
        _coverFog.Background = fog;
    }

    private void ApplyCoverGlow(List<ColorEntry> entries)
    {
        if (_coverGlow == null || entries.Count == 0) return;
        var seedColor = entries[0].Color;
        var r = Color.GetRedComponent(seedColor);
        var g = Color.GetGreenComponent(seedColor);
        var b = Color.GetBlueComponent(seedColor);
        var glowCenter = Color.Argb(0x40, r, g, b);
        var glowEdge = Color.Argb(0x00, r, g, b);
        var gd = new GradientDrawable();
        gd.SetGradientType(GradientType.RadialGradient);
        gd.SetGradientCenter(0.5f, 0.8f);
        gd.SetGradientRadius(300f);
        gd.SetColors(new int[] { glowCenter, glowEdge });
        _coverGlow.Background = gd;
    }

    private void ApplyCardTheme(MaterialYouPalette palette)
    {
        if (_controlsCard == null) return;
        var surfaceColor = new Color(palette.Surface);
        _controlsCard.SetCardBackgroundColor(Color.Argb(0x30, surfaceColor.R, surfaceColor.G, surfaceColor.B));
        _controlsCard.StrokeColor = palette.Outline;
        var onSurfaceColor = new Color(palette.OnSurface);
        var onSurfaceSemi = Color.Argb((int)(0xEE * 255f / 0xFF), onSurfaceColor.R, onSurfaceColor.G, onSurfaceColor.B);
        _timeCurrent.SetTextColor(onSurfaceSemi);
        _timeTotal.SetTextColor(onSurfaceSemi);
        var sliderCs = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.White);
        _progressSlider.ThumbTintList = sliderCs;
        _progressSlider.TrackActiveTintList = Android.Content.Res.ColorStateList.ValueOf(Color.ParseColor("#FFFFFF"));
        _audioVisualizer.SetColors(Color.ParseColor("#FFFFFF"));
        _progressSlider.HaloTintList = Android.Content.Res.ColorStateList.ValueOf(new Color(Color.Argb(0x30, 0xFF, 0xFF, 0xFF)));
        _progressSlider.TrackInactiveTintList = Android.Content.Res.ColorStateList.ValueOf(new Color(Color.Argb(0x50, 0xFF, 0xFF, 0xFF)));
        _modeActiveColor = onSurfaceColor;
        var white = Android.Content.Res.ColorStateList.ValueOf(Color.ParseColor("#FFFFFF"));
        var whiteSemi = Android.Content.Res.ColorStateList.ValueOf(Color.ParseColor("#DDFFFFFF"));
        _btnPlayPause.ImageTintList = white;
        _btnNext.ImageTintList = white;
        _btnPrev.ImageTintList = white;
        _btnLike.ImageTintList = white;
        _btnModeCycle.ImageTintList = white;
        _btnPlaylist.ImageTintList = whiteSemi;
        var visWhite = Android.Content.Res.ColorStateList.ValueOf(Color.ParseColor("#FFFFFF"));
        var visGray = Android.Content.Res.ColorStateList.ValueOf(Color.ParseColor("#88FFFFFF"));
        _btnVisualizerToggle.ImageTintList = _visualizerEnabled ? visWhite : visGray;
        _btnSleepTimer.ImageTintList = _sleepCts != null ? visWhite : visGray;
    }

    private void ApplyDefaultBackground()
    {
        if (_gradientBackground == null) return;
        _gradientBackground.SetBackgroundColor(Color.ParseColor("#1A0E28"));
        if (_reflectionMaskBottom != null) _reflectionMaskBottom.Background = null;
        if (_coverFog != null) _coverFog.Background = null;
        if (_controlsCard != null)
        {
            _controlsCard.SetCardBackgroundColor(Color.ParseColor("#26000000"));
            _controlsCard.StrokeColor = Color.ParseColor("#15CCCCCC");
        }
        var defaultWhite = Color.ParseColor("#FFFFFF");
        var defaultLight = Color.ParseColor("#DDFFFFFF");
        _modeActiveColor = defaultWhite;
        _timeCurrent.SetTextColor(defaultLight);
        _timeTotal.SetTextColor(defaultLight);
        _btnPlayPause.ImageTintList = Android.Content.Res.ColorStateList.ValueOf(defaultWhite);
        _btnNext.ImageTintList = Android.Content.Res.ColorStateList.ValueOf(defaultWhite);
        _btnPrev.ImageTintList = Android.Content.Res.ColorStateList.ValueOf(defaultWhite);
        _btnLike.ImageTintList = Android.Content.Res.ColorStateList.ValueOf(defaultWhite);
        _btnModeCycle.ImageTintList = Android.Content.Res.ColorStateList.ValueOf(defaultWhite);
        _btnPlaylist.ImageTintList = Android.Content.Res.ColorStateList.ValueOf(defaultLight);
        var visWhite = Android.Content.Res.ColorStateList.ValueOf(Color.ParseColor("#FFFFFF"));
        var visGray = Android.Content.Res.ColorStateList.ValueOf(Color.ParseColor("#88FFFFFF"));
        _btnVisualizerToggle.ImageTintList = _visualizerEnabled ? visWhite : visGray;
        _btnSleepTimer.ImageTintList = _sleepCts != null ? visWhite : visGray;
        var sliderCs = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.White);
        _progressSlider.ThumbTintList = sliderCs;
        _progressSlider.TrackActiveTintList = Android.Content.Res.ColorStateList.ValueOf(Color.ParseColor("#FFFFFF"));
        _audioVisualizer.SetColors(Color.ParseColor("#FFFFFF"));
        _progressSlider.HaloTintList = Android.Content.Res.ColorStateList.ValueOf(new Color(Color.Argb(0x30, 0xFF, 0xFF, 0xFF)));
        _progressSlider.TrackInactiveTintList = Android.Content.Res.ColorStateList.ValueOf(new Color(Color.Argb(0x50, 0xFF, 0xFF, 0xFF)));
        _songTitle.SetTextColor(Color.ParseColor("#FFEEEEEE"));
        _songArtist.SetTextColor(Color.ParseColor("#CCFFFFFF"));
        ApplyDefaultLyricColors();
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

    private int _lastLyricIdx = -1;
    private float _lyricLineHeightPx = -1f;

    private void UpdateLyrics()
    {
        var prev8 = _viewModel.PrevLyricLine8;
        var prev7 = _viewModel.PrevLyricLine7;
        var prev6 = _viewModel.PrevLyricLine6;
        var prev5 = _viewModel.PrevLyricLine5;
        var prev4 = _viewModel.PrevLyricLine4;
        var prev3 = _viewModel.PrevLyricLine3;
        var prev2 = _viewModel.PrevLyricLine2;
        var prev = _viewModel.PrevLyricLine;
        var curr = _viewModel.CurrentLyricLine;
        var next = _viewModel.NextLyricLine;
        var next2 = _viewModel.NextLyricLine2;
        var next3 = _viewModel.NextLyricLine3;
        var next4 = _viewModel.NextLyricLine4;
        var next5 = _viewModel.NextLyricLine5;
        var next6 = _viewModel.NextLyricLine6;
        var next7 = _viewModel.NextLyricLine7;
        var next8 = _viewModel.NextLyricLine8;
        var idx = _viewModel.CurrentLyricIndex;
        var isLineChanged = idx != _lastLyricIdx && _lastLyricIdx != -1 && !string.IsNullOrEmpty(curr);
        _lastLyricIdx = idx;

        _lyricPrev8.Text = prev8;
        _lyricPrev7.Text = prev7;
        _lyricPrev6.Text = prev6;
        _lyricPrev5.Text = prev5;
        _lyricPrev4.Text = prev4;
        _lyricPrev3.Text = prev3;
        _lyricNext3.Text = next3;
        _lyricNext4.Text = next4;
        _lyricNext5.Text = next5;
        _lyricNext6.Text = next6;
        _lyricNext7.Text = next7;
        _lyricNext8.Text = next8;

        if (!isLineChanged)
        {
            _lyricPrev2.Text = prev2; _lyricPrev2.TranslationY = 0f;
            _lyricPrev.Text = prev; _lyricPrev.TranslationY = 0f;
            _lyricNext.Text = next; _lyricNext.TranslationY = 0f;
            _lyricNext2.Text = next2; _lyricNext2.TranslationY = 0f;
            ApplyCurrentLineWithSpannable(curr);
            return;
        }

        if (_lyricLineHeightPx < 0f)
        {
            var density = _lyricCurrent.Context?.Resources?.DisplayMetrics?.Density ?? 1f;
            _lyricLineHeightPx = 40f * density;
        }

        _lyricPrev2.Text = prev2;
        _lyricPrev.Text = prev;
        _lyricNext.Text = next;
        _lyricNext2.Text = next2;
        ApplyCurrentLineWithSpannable(curr);

        var allViews = new List<StrokeTextView> { _lyricPrev6, _lyricPrev5, _lyricPrev4, _lyricPrev3, _lyricPrev2, _lyricPrev, _lyricCurrent, _lyricNext, _lyricNext2, _lyricNext3, _lyricNext4, _lyricNext5, _lyricNext6 };

        for (int i = 0; i < allViews.Count; i++)
        {
            var v = allViews[i];
            if (v.Visibility != ViewStates.Visible) continue;
            v.Animate().Cancel();
            v.TranslationY = _lyricLineHeightPx;
            v.Animate()
                .TranslationY(0f)
                .SetDuration(300L)
                .SetInterpolator(_lyricInterpolator)
                .Start();
        }
    }

    private void ApplyCurrentLineWithSpannable(string? plainText)
    {
        if (_lyricCurrent == null) return;
        if (_viewModel.LyricStyle == 1 && _viewModel.CurrentLyricSpannable != null)
        {
            _lyricCurrent.SetText(_viewModel.CurrentLyricSpannable, TextView.BufferType.Spannable);
            _lyricCurrent.LyricProgress = _viewModel.CurrentLyricProgress;
            _lyricCurrent.Alpha = 1f;
        }
        else
        {
            _lyricCurrent.ResetLyricProgress();
            if (plainText != null)
            {
                _lyricCurrent.Text = plainText;
                _lyricCurrent.Alpha = 1f;
            }
        }
        _lyricCurrent.TranslationY = 0f;
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

    private void OnAudioSessionIdChanged(int newSessionId)
    {
        if (Activity == null) return;
        Activity.RunOnUiThread(() =>
        {
            if (!_visualizerEnabled) return;
            if (newSessionId == 0) return;
            if (newSessionId == _lastVisualizerSessionId && _visualizerHelper != null && _visualizerHelper.IsEnabled)
                return;
            _lastVisualizerSessionId = newSessionId;
            _visualizerHelper?.Stop();
            _visualizerHelper = null;
            StartVisualizerWithSession(newSessionId);
        });
    }

    public void RestartVisualizer()
    {
        if (!_visualizerEnabled) return;
        _visualizerHelper?.Stop();
        _visualizerHelper = null;
        _lastVisualizerSessionId = 0;
        TryStartVisualizer();
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
        private readonly Song? _currentSong;
        private readonly int _dp;
        private readonly Action<Song> _onSongClick;
        public PlaylistAdapter(List<Song> songs, Song? currentSong, int dp, Action<Song> onSongClick) { _songs = songs; _currentSong = currentSong; _dp = dp; _onSongClick = onSongClick; }
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
