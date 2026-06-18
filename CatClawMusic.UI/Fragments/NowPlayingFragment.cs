using Android.App;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using Android.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;
using CatClawMusic.UI.Helpers;
using CatClawMusic.UI.Services;
using CatClawMusic.UI.ViewModels;
using CatClawMusic.UI.Platforms.Android;
using AndroidX.RecyclerView.Widget;
using AndroidX.ViewPager2.Widget;
using Microsoft.Extensions.DependencyInjection;
using AndroidX.Activity.Result;
using AndroidX.Activity.Result.Contract;
using AndroidX.Core.View;
using GoogleSlider = Google.Android.Material.Slider.Slider;
using System.Collections.Generic;

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
    private LyricRendererView? _lyricRenderer;
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

    // --- 垂直 ViewPager2：播放页 ↔ 歌曲详情 ---
    private ViewPager2? _verticalPager;

    // --- 歌曲详情页面（Page 1）视图引用 ---
    private View? _songDetailView;
    private ImageView? _detailAlbumCover;
    private ImageView? _detailArtistThumb;
    private ImageView? _detailAlbumThumb;
    private TextView? _detailTitle;
    private TextView? _detailArtist;
    private TextView? _detailAlbum;
    private TextView? _detailDuration;
    private TextView? _detailYear;
    private TextView? _detailBitrate;
    private TextView? _detailSampleRate;
    private TextView? _detailChannels;
    private TextView? _detailBitDepth;
    private TextView? _detailCodec;
    private TextView? _detailFormat;
    private TextView? _detailFileSize;
    private TextView? _detailFilePath;
    private TextView? _detailLyrics;
    private RadioGroup? _detailLyricSource;
    private RadioButton? _detailRbEmbedded;
    private RadioButton? _detailRbExternal;
    private Song? _detailSong;
    private string _detailEmbeddedLyrics = "";
    private string _detailExternalLyrics = "";
    private int _detailSongId;

    // --- 歌词自定义设置（与 FullLyricsFragment 共享） ---
    /// <summary>背景遮罩颜色预设（与 FullLyricsFragment 共享）</summary>
    private static readonly string[] BgColorHex = { "#CCF0EBE3", "#CC0F0D16", "#00000000" };

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

        // 先初始化垂直 ViewPager2（page 0 = 正在播放, page 1 = 歌曲详情）
        // album_cover 等视图在 page 0 中，必须先设置 adapter 才能通过 FindViewById 找到
        _verticalPager = view.FindViewById<ViewPager2>(Resource.Id.vertical_pager)!;
        var inflater = LayoutInflater.From(Context!)!;
        var nowPlayingPage = inflater.Inflate(Resource.Layout.page_now_playing_content, _verticalPager, false)!;
        var songDetailPage = inflater.Inflate(Resource.Layout.fragment_song_detail, _verticalPager, false)!;
        _verticalPager.Adapter = new VerticalPagerAdapter(nowPlayingPage, songDetailPage);
        _verticalPager.OffscreenPageLimit = 1; // 保持两个页面都存活
        _songDetailView = songDetailPage;

        // 延迟绑定详情页面视图（等待 ViewPager2 完成布局）
        _verticalPager.Post(() => InitDetailPage());

        // 滑到详情页面时自动刷新数据
        _verticalPager.RegisterOnPageChangeCallback(new VerticalPagerPageCallback(this));

        // album_cover 在 ViewPager2 page 0 中，通过 nowPlayingPage 直接查找（ViewPager2 异步创建页面，不能用 view.FindViewById）
        _albumCover = nowPlayingPage.FindViewById<ImageView>(Resource.Id.album_cover)!;
        var coverContainer = (ViewGroup?)_albumCover.Parent?.Parent?.Parent;
        if (coverContainer?.LayoutParameters is LinearLayout.LayoutParams clp)
            _coverLayoutParams = clp;

        _songTitle = nowPlayingPage.FindViewById<TextView>(Resource.Id.song_title)!;
        _songArtist = nowPlayingPage.FindViewById<TextView>(Resource.Id.song_artist)!;
        _lyricRenderer = nowPlayingPage.FindViewById<LyricRendererView>(Resource.Id.lyric_renderer);
        _songTitle.ImportantForAutofill = Android.Views.ImportantForAutofill.No;
        _songArtist.ImportantForAutofill = Android.Views.ImportantForAutofill.No;
        _timeCurrent = nowPlayingPage.FindViewById<TextView>(Resource.Id.time_current)!;
        _timeTotal = nowPlayingPage.FindViewById<TextView>(Resource.Id.time_total)!;
        _timeCurrent.ImportantForAutofill = Android.Views.ImportantForAutofill.No;
        _timeTotal.ImportantForAutofill = Android.Views.ImportantForAutofill.No;
        _btnPlayPause = nowPlayingPage.FindViewById<ImageButton>(Resource.Id.btn_play_pause)!;
        _btnNext = nowPlayingPage.FindViewById<ImageButton>(Resource.Id.btn_next)!;
        _btnPrev = nowPlayingPage.FindViewById<ImageButton>(Resource.Id.btn_prev)!;
        _btnLike = nowPlayingPage.FindViewById<ImageButton>(Resource.Id.btn_like)!;
        _btnModeCycle = nowPlayingPage.FindViewById<ImageButton>(Resource.Id.btn_mode_cycle)!;
        _btnPlaylist = nowPlayingPage.FindViewById<ImageButton>(Resource.Id.btn_playlist)!;
        _btnVisualizerToggle = nowPlayingPage.FindViewById<ImageButton>(Resource.Id.btn_visualizer_toggle)!;
        _btnEq = nowPlayingPage.FindViewById<ImageButton>(Resource.Id.btn_eq)!;
        _btnSleepTimer = nowPlayingPage.FindViewById<ImageButton>(Resource.Id.btn_sleep_timer)!;
        _btnLandscape = nowPlayingPage.FindViewById<ImageButton>(Resource.Id.btn_landscape)!;
        _progressSlider = nowPlayingPage.FindViewById<GoogleSlider>(Resource.Id.progress_slider)!;
        _controlsCard = nowPlayingPage.FindViewById<Google.Android.Material.Card.MaterialCardView>(Resource.Id.controls_card)!;
        _audioVisualizer = nowPlayingPage.FindViewById<AudioVisualizerView>(Resource.Id.audio_visualizer)!;
        _bgDimOverlay = view.FindViewById<View>(Resource.Id.bg_dim_overlay);
        _bgCover = view.FindViewById<ImageView>(Resource.Id.bg_cover)!;
        ApplyBlur();
        _flowLight = view.FindViewById<FlowLightView>(Resource.Id.flow_light)!;
        InitFlowLight();

        _progressSlider.ImportantForAutofill = Android.Views.ImportantForAutofill.No;
        _progressSlider.TickVisible = false;
        _progressSlider.ThumbRadius = 8;
        _progressSlider.SetLabelFormatter(new SliderLabelFormatter());
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

        // 初始化歌词渲染视图
        var prefs = Activity?.GetSharedPreferences("lyric_settings", Android.Content.FileCreationMode.Private);
        if (_lyricRenderer != null)
        {
            _lyricRenderer.Init(_viewModel, prefs);
            _lyricRenderer.LoadSettings();
            _lyricRenderer.EnableScroll = true;    // 启用自动滚动，当前行保持在歌词区中央
            _lyricRenderer.EnableDragSeek = false; // 播放页不支持拖拽跳转
            _lyricRenderer.BgDimOverlay = _bgDimOverlay;
            _lyricRenderer.OnClickCallback = () => MainActivity.Instance?.SwitchTab(0);
        }

        // 加载歌词颜色和字号设置（同步背景遮罩）
        LoadLyricSettings();

        // 控制区域拦截 ViewPager2 的横向滑动
        var controlsArea = nowPlayingPage.FindViewById<View>(Resource.Id.controls_area)!;
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
        _progressSlider.SetOnTouchListener(new SliderTouchListener(v =>
        {
            _viewModel.CurrentPositionSeconds = v;
            // seek 后强制立即滚动到当前歌词，保持当前行在歌词区中央
            _lyricRenderer?.ForceScrollToCurrent();
        }));

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
        _lyricRenderer?.SetBgLuminance(_currentBgLuminance);
        UpdateFlowLightColors();
    }

    /// <summary>从封面 Drawable 提取平均亮度</summary>
    private void ComputeCoverLuminance(Android.Graphics.Drawables.Drawable drawable)
    {
        try
        {
            var bd = drawable as Android.Graphics.Drawables.BitmapDrawable;
            var bitmap = bd?.Bitmap;
            if (bitmap == null || bitmap.IsRecycled) { _currentBgLuminance = 0.3f; _lyricRenderer?.SetBgLuminance(_currentBgLuminance); return; }

            var scaled = Bitmap.CreateScaledBitmap(bitmap, 1, 1, false);
            if (scaled == null) { _currentBgLuminance = 0.3f; _lyricRenderer?.SetBgLuminance(_currentBgLuminance); return; }

            var pixel = new int[1];
            scaled.GetPixels(pixel, 0, 1, 0, 0, 1, 1);
            var c = new Color(pixel[0]);
            _currentBgLuminance = (0.299f * c.R + 0.587f * c.G + 0.114f * c.B) / 255f;
            _lyricRenderer?.SetBgLuminance(_currentBgLuminance);

            if (!ReferenceEquals(scaled, bitmap)) scaled.Recycle();
        }
        catch { _currentBgLuminance = 0.3f; _lyricRenderer?.SetBgLuminance(_currentBgLuminance); }
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
            // 流光开启时降低遮罩透明度，让流光效果透出来
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

    /// <summary>
    /// 从 SharedPreferences 加载歌词颜色和字号设置（委托给 LyricRendererView）
    /// </summary>
    private void LoadLyricSettings()
    {
        var prefs = Activity?.GetSharedPreferences("lyric_settings", Android.Content.FileCreationMode.Private);
        if (prefs == null) return;

        _lyricBgColorIndex = prefs.GetInt("lyric_bg_color", 0);
        UpdateBgOverlay();

        // 同步设置到歌词渲染视图
        if (_lyricRenderer != null)
        {
            _lyricRenderer.LoadSettings();
            _lyricRenderer.BgDimOverlay = _bgDimOverlay;
            _lyricRenderer.SetBgLuminance(_currentBgLuminance);
            _lyricRenderer.ApplyAdaptiveColors();
            _lyricRenderer.RefreshLyricColors();
        }
    }

    /// <summary>更新背景遮罩颜色</summary>
    private void UpdateBgOverlay()
    {
        if (_bgDimOverlay == null) return;
        _bgDimOverlay.SetBackgroundColor(Color.ParseColor(BgColorHex[Math.Clamp(_lyricBgColorIndex, 0, BgColorHex.Length - 1)]));
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
            _viewModel.LyricsMode = prefs?.GetInt("lyrics_mode", 3) ?? 3;
            UpdateTimeDisplay();
            UpdateSlider();
            UpdatePlayPauseIcon();
            UpdateModeIcon();
            UpdateLikeIcon();
            // 确保 Spannable 在歌词渲染之前已创建，避免整行闪烁
            if (_viewModel.LyricStyle == 1 && _viewModel.CurrentLyricSpannable == null)
                _viewModel.UpdateLyricSpannable();
            _lyricRenderer?.RebuildLyrics();
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
                    // 切歌时刷新详情页面数据
                    LoadDetailDataAsync();
                    // 切歌时无需重启 Visualizer：ExoPlayer 复用同一 SessionId，
                    // 已绑定的 Visualizer 会自动接收新轨道的 FFT 数据
                    break;
                case nameof(_viewModel.CurrentLyricLine):
                    _lyricRenderer?.HighlightCurrentLine();
                    break;
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

        PlaylistAdapter? adapter = null;
        adapter = new PlaylistAdapter(allSongs, currentSong, dp, song =>
        {
            PlaySong(song);
            adapter?.SetCurrentSong(song);
            // 滚动到新播放歌曲的位置，让用户看到高亮变化
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
            InitFlowLight();
            // 强制刷新歌词颜色（用户可能在歌词页修改了设置）
            UpdateBackground();
            _lyricRenderer?.ApplyAdaptiveColors();
            _lyricRenderer?.RefreshLyricColors();
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
                _lyricRenderer?.RebuildLyrics();
                InitFlowLight();
                // 确保歌词颜色反映最新设置
                UpdateBackground();
                _lyricRenderer?.ApplyAdaptiveColors();
                _lyricRenderer?.RefreshLyricColors();
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
        private Song? _currentSong;
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

    // --- 垂直 ViewPager2 管理 ---

    /// <summary>滑到歌曲详情页面（page 1）</summary>
    public void ShowSongDetailPage(int songId = 0)
    {
        if (songId > 0) _detailSongId = songId;
        _verticalPager?.SetCurrentItem(1, true);
    }

    /// <summary>滑回正在播放页面（page 0）</summary>
    public void NavigateToPlayingPage()
    {
        _verticalPager?.SetCurrentItem(0, false);
    }

    /// <summary>绑定歌曲详情页面视图并设置交互</summary>
    private void InitDetailPage()
    {
        if (_songDetailView == null) return;
        var v = _songDetailView;

        _detailAlbumCover = v.FindViewById<ImageView>(Resource.Id.iv_album_cover);
        _detailArtistThumb = v.FindViewById<ImageView>(Resource.Id.iv_artist_thumb);
        _detailAlbumThumb = v.FindViewById<ImageView>(Resource.Id.iv_album_thumb);
        _detailTitle = v.FindViewById<TextView>(Resource.Id.tv_song_title);
        _detailArtist = v.FindViewById<TextView>(Resource.Id.tv_artist);
        _detailAlbum = v.FindViewById<TextView>(Resource.Id.tv_album);
        _detailDuration = v.FindViewById<TextView>(Resource.Id.tv_duration);
        _detailYear = v.FindViewById<TextView>(Resource.Id.tv_year);
        _detailBitrate = v.FindViewById<TextView>(Resource.Id.tv_bitrate);
        _detailSampleRate = v.FindViewById<TextView>(Resource.Id.tv_sample_rate);
        _detailChannels = v.FindViewById<TextView>(Resource.Id.tv_channels);
        _detailBitDepth = v.FindViewById<TextView>(Resource.Id.tv_bit_depth);
        _detailCodec = v.FindViewById<TextView>(Resource.Id.tv_codec);
        _detailFormat = v.FindViewById<TextView>(Resource.Id.tv_format);
        _detailFileSize = v.FindViewById<TextView>(Resource.Id.tv_file_size);
        _detailFilePath = v.FindViewById<TextView>(Resource.Id.tv_file_path);
        _detailLyrics = v.FindViewById<TextView>(Resource.Id.tv_lyrics);
        _detailLyricSource = v.FindViewById<RadioGroup>(Resource.Id.rg_lyric_source);
        _detailRbEmbedded = v.FindViewById<RadioButton>(Resource.Id.rb_embedded);
        _detailRbExternal = v.FindViewById<RadioButton>(Resource.Id.rb_external);

        var btnEdit = v.FindViewById<ImageButton>(Resource.Id.btn_edit);
        if (btnEdit != null)
            btnEdit.Click += (s, e) => ShowDetailEditDialog();

        var navService = MainApplication.Services.GetRequiredService<INavigationService>();
        var rowArtist = v.FindViewById<LinearLayout>(Resource.Id.row_artist);
        if (rowArtist != null)
        {
            rowArtist.Click += (s, e) =>
            {
                if (_detailSong != null && !string.IsNullOrEmpty(_detailSong.Artist))
                {
                    NavigateToPlayingPage();
                    navService.PushFragment("ArtistDetail",
                        new Dictionary<string, object> { ["artistName"] = _detailSong.Artist });
                }
            };
        }

        var rowAlbum = v.FindViewById<LinearLayout>(Resource.Id.row_album);
        if (rowAlbum != null)
        {
            rowAlbum.Click += (s, e) =>
            {
                if (_detailSong != null && !string.IsNullOrEmpty(_detailSong.Album))
                {
                    NavigateToPlayingPage();
                    navService.PushFragment("AlbumDetail",
                        new Dictionary<string, object>
                        {
                            ["albumTitle"] = _detailSong.Album,
                            ["albumArtist"] = _detailSong.Artist
                        });
                }
            };
        }

        if (_detailLyricSource != null)
        {
            _detailLyricSource.CheckedChange += (s, e) =>
            {
                if (_detailLyrics == null) return;
                if (e.CheckedId == Resource.Id.rb_embedded)
                    _detailLyrics.Text = string.IsNullOrEmpty(_detailEmbeddedLyrics) ? "无内置歌词" : _detailEmbeddedLyrics;
                else if (e.CheckedId == Resource.Id.rb_external)
                    _detailLyrics.Text = string.IsNullOrEmpty(_detailExternalLyrics) ? "无外嵌歌词" : _detailExternalLyrics;
            };
        }

        // 歌词数据在 OnPageSelected(1) 时加载，避免重复调用导致竞态
    }

    /// <summary>加载歌曲详情数据（从 SongDetailBottomSheet 迁移）</summary>
    private async void LoadDetailDataAsync()
    {
        if (_detailTitle == null) return;

        // 优先使用指定的 songId，否则使用当前播放歌曲
        var songId = _detailSongId > 0 ? _detailSongId : (_viewModel.CurrentSong?.Id ?? 0);
        if (songId <= 0) return;

        var db = MainApplication.Services.GetRequiredService<MusicDatabase>();
        await db.EnsureInitializedAsync();

        try
        {
            var songs = await db.GetSongsWithDetailsAsync();
            _detailSong = songs.FirstOrDefault(s => s.Id == songId);

            if (_detailSong == null)
            {
                Activity?.RunOnUiThread(() =>
                {
                    if (_detailTitle != null) _detailTitle.Text = "歌曲不存在";
                    if (_detailLyrics != null) _detailLyrics.Text = "未找到歌曲信息";
                });
                return;
            }

            Activity?.RunOnUiThread(() =>
            {
                if (_detailTitle != null) _detailTitle.Text = _detailSong.Title;
                if (_detailArtist != null) _detailArtist.Text = _detailSong.Artist;
                if (_detailAlbum != null) _detailAlbum.Text = _detailSong.Album;
                if (_detailDuration != null)
                {
                    var dur = _detailSong.Duration;
                    _detailDuration.Text = dur > 0
                        ? TimeSpan.FromSeconds(dur).ToString(dur >= 3600 ? @"h\:mm\:ss" : @"mm\:ss")
                        : "未知";
                }
                if (_detailYear != null) _detailYear.Text = _detailSong.Year > 0 ? _detailSong.Year.ToString() : "未知";
                if (_detailBitrate != null) _detailBitrate.Text = _detailSong.Bitrate > 0 ? $"{_detailSong.Bitrate} kbps" : "未知";
                if (_detailFileSize != null) _detailFileSize.Text = FormatDetailFileSize(_detailSong.FileSize);
                if (_detailFilePath != null) _detailFilePath.Text = _detailSong.FilePath ?? "未知";
            });

            await Task.WhenAll(
                LoadDetailCoverAsync(),
                LoadDetailThumbnailsAsync(db),
                LoadDetailLyricsAsync(db),
                LoadDetailAudioPropertiesAsync());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SongDetail] 加载失败: {ex}");
            Activity?.RunOnUiThread(() =>
            {
                if (_detailLyrics != null) _detailLyrics.Text = "加载失败";
            });
        }
    }

    private async Task LoadDetailCoverAsync()
    {
        if (_detailSong == null) return;
        try
        {
            if (_detailSong.Source == SongSource.Local && _detailSong.MediaStoreId > 0)
            {
                var bitmap = await Task.Run(() =>
                    Platforms.Android.MediaStoreCoverHelper.LoadCoverFromMediaStore(_detailSong.MediaStoreId, 480));
                if (bitmap != null)
                {
                    Activity?.RunOnUiThread(() =>
                    {
                        _detailAlbumCover?.SetImageBitmap(bitmap);
                        _detailAlbumThumb?.SetImageBitmap(bitmap);
                    });
                    return;
                }
            }

            if (!string.IsNullOrEmpty(_detailSong.CoverArtPath) && System.IO.File.Exists(_detailSong.CoverArtPath))
            {
                var bitmap = await Task.Run(() => BitmapFactory.DecodeFile(_detailSong.CoverArtPath));
                if (bitmap != null)
                {
                    Activity?.RunOnUiThread(() =>
                    {
                        _detailAlbumCover?.SetImageBitmap(bitmap);
                        _detailAlbumThumb?.SetImageBitmap(bitmap);
                    });
                    return;
                }
            }

            var cachePath = System.IO.Path.Combine(
                global::Android.App.Application.Context.CacheDir!.AbsolutePath,
                "covers", $"cover_{_detailSong.Id}.jpg");
            if (System.IO.File.Exists(cachePath))
            {
                var bitmap = await Task.Run(() => BitmapFactory.DecodeFile(cachePath));
                if (bitmap != null)
                {
                    Activity?.RunOnUiThread(() =>
                    {
                        _detailAlbumCover?.SetImageBitmap(bitmap);
                        _detailAlbumThumb?.SetImageBitmap(bitmap);
                    });
                    return;
                }
            }

            if (!string.IsNullOrEmpty(_detailSong.FilePath)
                && !_detailSong.FilePath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                && !_detailSong.FilePath.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
            {
                var coverBytes = await Task.Run(() => TagReader.ExtractCoverArt(_detailSong.FilePath));
                if (coverBytes is { Length: > 0 })
                {
                    var bitmap = await Task.Run(() => BitmapFactory.DecodeByteArray(coverBytes, 0, coverBytes.Length));
                    if (bitmap != null)
                    {
                        Activity?.RunOnUiThread(() =>
                        {
                            _detailAlbumCover?.SetImageBitmap(bitmap);
                            _detailAlbumThumb?.SetImageBitmap(bitmap);
                        });
                    }
                }
            }
        }
        catch { }
    }

    private async Task LoadDetailThumbnailsAsync(MusicDatabase db)
    {
        if (_detailSong == null) return;
        try
        {
            var artists = await db.GetAllArtistsAsync();
            var artist = artists.FirstOrDefault(a => a.Name == _detailSong.Artist);
            if (artist != null && !string.IsNullOrEmpty(artist.Cover) && System.IO.File.Exists(artist.Cover))
            {
                var bitmap = await Task.Run(() => BitmapFactory.DecodeFile(artist.Cover));
                if (bitmap != null)
                {
                    Activity?.RunOnUiThread(() =>
                    {
                        if (_detailArtistThumb != null)
                        {
                            _detailArtistThumb.SetImageBitmap(bitmap);
                            _detailArtistThumb.ImageTintList = null;
                        }
                    });
                }
            }
        }
        catch { }
    }

    private async Task LoadDetailAudioPropertiesAsync()
    {
        if (_detailSong == null) return;

        try
        {
            TagLib.Properties? props = null;
            string? fileExtension = null;

            if (!string.IsNullOrEmpty(_detailSong.FilePath))
            {
                fileExtension = System.IO.Path.GetExtension(_detailSong.FilePath).TrimStart('.').ToUpperInvariant();
                if (fileExtension == "")
                    fileExtension = null;
            }

            if (!string.IsNullOrEmpty(_detailSong.FilePath) && _detailSong.FilePath.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
            {
                var ctx = global::Android.App.Application.Context;
                var uri = Android.Net.Uri.Parse(_detailSong.FilePath);
                if (uri != null)
                {
                    using var stream = ctx.ContentResolver!.OpenInputStream(uri);
                    if (stream != null)
                    {
                        var abstraction = new ReadOnlyFileAbstraction(System.IO.Path.GetFileName(_detailSong.FilePath) ?? "audio", stream);
                        using var file = TagLib.File.Create(abstraction);
                        props = file.Properties;
                        fileExtension ??= GetExtensionFromMimeType(ctx.ContentResolver!.GetType(uri));
                    }
                }
            }
            else if (!string.IsNullOrEmpty(_detailSong.FilePath) && System.IO.File.Exists(_detailSong.FilePath))
            {
                using var file = TagLib.File.Create(_detailSong.FilePath);
                props = file.Properties;
            }

            if (props == null) return;

            var sampleRate = props.AudioSampleRate;
            var channels = props.AudioChannels;
            var bitDepth = props.BitsPerSample;
            var codec = props.Codecs.FirstOrDefault();

            Activity?.RunOnUiThread(() =>
            {
                if (_detailSampleRate != null) _detailSampleRate.Text = sampleRate > 0 ? FormatDetailSampleRate(sampleRate) : "未知";
                if (_detailChannels != null) _detailChannels.Text = channels > 0 ? FormatDetailChannels(channels) : "未知";
                if (_detailBitDepth != null) _detailBitDepth.Text = bitDepth > 0 ? $"{bitDepth} bit" : "未知";
                if (_detailCodec != null)
                    _detailCodec.Text = !string.IsNullOrWhiteSpace(GetDetailCodecDescription(codec, fileExtension))
                        ? GetDetailCodecDescription(codec, fileExtension)
                        : (fileExtension ?? "未知");
                if (_detailFormat != null) _detailFormat.Text = !string.IsNullOrWhiteSpace(fileExtension) ? fileExtension : "未知";
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NowPlayingDetail] 读取音频属性失败: {ex.Message}");
        }
    }

    private static string FormatDetailSampleRate(int sampleRate)
    {
        if (sampleRate >= 1000)
            return $"{sampleRate / 1000.0:F1} kHz";
        return $"{sampleRate} Hz";
    }

    private static string FormatDetailChannels(int channels)
    {
        return channels switch
        {
            1 => "单声道 (Mono)",
            2 => "立体声 (Stereo)",
            6 => "5.1 声道",
            8 => "7.1 声道",
            _ => $"{channels} 声道"
        };
    }

    private static string? GetExtensionFromMimeType(string? mimeType)
    {
        if (string.IsNullOrWhiteSpace(mimeType)) return null;
        var lowered = mimeType.ToLowerInvariant();
        return lowered switch
        {
            "audio/mpeg" => "MP3",
            "audio/flac" => "FLAC",
            "audio/wav" => "WAV",
            "audio/x-wav" => "WAV",
            "audio/aac" => "AAC",
            "audio/mp4" => "M4A",
            "audio/x-m4a" => "M4A",
            "audio/ogg" => "OGG",
            "audio/opus" => "OPUS",
            "audio/x-ms-wma" => "WMA",
            _ => null
        };
    }

    private static string? GetDetailCodecDescription(TagLib.ICodec? codec, string? fileExtension)
    {
        if (codec != null && !string.IsNullOrWhiteSpace(codec.Description))
        {
            var desc = codec.Description;
            if (desc.Contains("FLAC", StringComparison.OrdinalIgnoreCase)) return "FLAC";
            if (desc.Contains("MPEG", StringComparison.OrdinalIgnoreCase)) return "MP3";
            if (desc.Contains("AAC", StringComparison.OrdinalIgnoreCase)) return "AAC";
            if (desc.Contains("ALAC", StringComparison.OrdinalIgnoreCase)) return "ALAC";
            if (desc.Contains("Vorbis", StringComparison.OrdinalIgnoreCase)) return "Vorbis";
            if (desc.Contains("Opus", StringComparison.OrdinalIgnoreCase)) return "Opus";
            return desc;
        }
        return fileExtension;
    }

    private async Task LoadDetailLyricsAsync(MusicDatabase db)
    {
        if (_detailSong == null) return;

        // 使用局部变量加载，避免并发调用互相覆盖
        var embedded = "";
        var external = "";
        var lyricsService = MainApplication.Services.GetRequiredService<LyricsService>();

        try
        {
            if (!string.IsNullOrEmpty(_detailSong.FilePath)
                && !_detailSong.FilePath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                && !_detailSong.FilePath.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
            {
                embedded = await Task.Run(() => TagReader.ReadEmbeddedLyrics(_detailSong.FilePath)) ?? "";
            }
            else if (!string.IsNullOrEmpty(_detailSong.FilePath)
                && _detailSong.FilePath.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
            {
                var ctx = global::Android.App.Application.Context;
                var uri = Android.Net.Uri.Parse(_detailSong.FilePath);
                if (uri != null)
                {
                    using var stream = ctx.ContentResolver!.OpenInputStream(uri);
                    if (stream != null)
                        embedded = await Task.Run(() =>
                            TagReader.ReadEmbeddedLyricsFromStream(stream, _detailSong.FilePath)) ?? "";
                }
            }

            if (!string.IsNullOrWhiteSpace(embedded))
            {
                var parsed = await Task.Run(() => lyricsService.TryParseLyrics(embedded));
                if (parsed != null)
                    embedded = LyricsFormatter.FormatLrcLyrics(parsed);
            }
        }
        catch { }

        try
        {
            var lyric = await db.GetLyricAsync(_detailSong.Id);
            if (lyric != null && !string.IsNullOrEmpty(lyric.Content))
            {
                external = lyric.Content;
            }
            else if (!string.IsNullOrEmpty(_detailSong.LyricsPath) && System.IO.File.Exists(_detailSong.LyricsPath))
            {
                external = await Task.Run(() => System.IO.File.ReadAllText(_detailSong.LyricsPath));
            }
            else if (!string.IsNullOrEmpty(_detailSong.FilePath)
                && !_detailSong.FilePath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                && !_detailSong.FilePath.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
            {
                var lrcPath = await Task.Run(() => MusicUtility.FindLyricsFile(_detailSong.FilePath));
                if (!string.IsNullOrEmpty(lrcPath))
                    external = await LyricsService.ReadLyricsFileWithEncodingDetection(lrcPath);
            }
            else if (!string.IsNullOrEmpty(_detailSong.FilePath)
                && _detailSong.FilePath.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
            {
                // content:// URI → 通过 MediaStore 解析出真实文件路径，再查找 .lrc
                var realPath = await Task.Run(() =>
                {
                    try
                    {
                        var ctx = global::Android.App.Application.Context;
                        var uri = Android.Net.Uri.Parse(_detailSong.FilePath);
                        if (uri == null) return (string?)null;
                        using var cursor = ctx.ContentResolver!.Query(uri,
                            new[] { Android.Provider.MediaStore.Audio.Media.InterfaceConsts.Data },
                            null, null, null);
                        if (cursor != null && cursor.MoveToFirst())
                        {
                            var idx = cursor.GetColumnIndex(Android.Provider.MediaStore.Audio.Media.InterfaceConsts.Data);
                            if (idx >= 0) return cursor.GetString(idx);
                        }
                    }
                    catch { }
                    return (string?)null;
                });
                if (!string.IsNullOrEmpty(realPath))
                {
                    var lrcPath = await Task.Run(() => MusicUtility.FindLyricsFile(realPath));
                    if (!string.IsNullOrEmpty(lrcPath))
                        external = await LyricsService.ReadLyricsFileWithEncodingDetection(lrcPath);
                }
            }

            if (!string.IsNullOrWhiteSpace(external))
            {
                var parsed = await Task.Run(() => lyricsService.TryParseLyrics(external));
                if (parsed != null)
                    external = LyricsFormatter.FormatLrcLyrics(parsed);
            }
        }
        catch { }

        bool hasEmbedded = !string.IsNullOrWhiteSpace(embedded);
        bool hasExternal = !string.IsNullOrWhiteSpace(external);

        Activity?.RunOnUiThread(() =>
        {
            // 原子更新共享字段
            _detailEmbeddedLyrics = embedded;
            _detailExternalLyrics = external;

            if (_detailRbEmbedded != null) _detailRbEmbedded.Enabled = true;
            if (_detailRbExternal != null) _detailRbExternal.Enabled = true;

            if (_detailLyrics == null) return;
            if (hasEmbedded && hasExternal)
            {
                if (_detailRbExternal != null) _detailRbExternal.Checked = true;
                _detailLyrics.Text = external;
            }
            else if (hasEmbedded)
            {
                if (_detailRbEmbedded != null) _detailRbEmbedded.Checked = true;
                _detailLyrics.Text = embedded;
            }
            else if (hasExternal)
            {
                if (_detailRbExternal != null) _detailRbExternal.Checked = true;
                _detailLyrics.Text = external;
            }
            else
            {
                if (_detailRbEmbedded != null) _detailRbEmbedded.Checked = true;
                _detailLyrics.Text = "暂无歌词";
            }
        });
    }

    private void ShowDetailEditDialog()
    {
        var ctx = Context;
        if (ctx == null || _detailSong == null) return;

        var ll = new LinearLayout(ctx)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        ll.SetPadding(48, 24, 48, 16);

        var etTitle = CreateDetailEditField(ctx, "歌曲标题", _detailSong.Title);
        var etArtist = CreateDetailEditField(ctx, "艺术家", _detailSong.Artist);
        var etAlbum = CreateDetailEditField(ctx, "专辑", _detailSong.Album);
        ll.AddView(etTitle);
        ll.AddView(etArtist);
        ll.AddView(etAlbum);

        var dialog = new Google.Android.Material.Dialog.MaterialAlertDialogBuilder(ctx)
            .SetTitle("编辑歌曲信息")
            .SetView(ll)
            .SetPositiveButton("保存", async (s, e) =>
            {
                var newTitle = etTitle.Text?.Trim();
                var newArtist = etArtist.Text?.Trim();
                var newAlbum = etAlbum.Text?.Trim();

                if (string.IsNullOrEmpty(newTitle))
                {
                    Toast.MakeText(ctx, "标题不能为空", ToastLength.Short)?.Show();
                    return;
                }

                try
                {
                    var db = MainApplication.Services.GetRequiredService<MusicDatabase>();
                    await db.EnsureInitializedAsync();
                    _detailSong!.Title = newTitle;
                    _detailSong.Artist = newArtist ?? "";
                    _detailSong.Album = newAlbum ?? "";
                    await db.SaveSongAsync(_detailSong);
                    Activity?.RunOnUiThread(() =>
                    {
                        if (_detailTitle != null) _detailTitle.Text = _detailSong.Title;
                        if (_detailArtist != null) _detailArtist.Text = _detailSong.Artist;
                        if (_detailAlbum != null) _detailAlbum.Text = _detailSong.Album;
                        Toast.MakeText(ctx, "已保存", ToastLength.Short)?.Show();
                    });
                }
                catch
                {
                    Activity?.RunOnUiThread(() =>
                        Toast.MakeText(ctx, "保存失败", ToastLength.Short)?.Show());
                }
            })
            .SetNegativeButton("取消", (s, e) => { })
            .Create();

        dialog?.Show();
    }

    private static EditText CreateDetailEditField(Android.Content.Context ctx, string hint, string text)
    {
        var et = new EditText(ctx)
        {
            Hint = hint,
            Text = text,
            InputType = Android.Text.InputTypes.TextFlagCapSentences
        };
        et.SetTextSize(Android.Util.ComplexUnitType.Sp, 14f);
        var lp = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        lp.BottomMargin = 16;
        et.LayoutParameters = lp;
        return et;
    }

    private static string FormatDetailFileSize(long bytes)
    {
        if (bytes <= 0) return "未知";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }

    /// <summary>垂直 ViewPager2 适配器：Page 0 = 正在播放, Page 1 = 歌曲详情</summary>
    private class VerticalPagerAdapter : RecyclerView.Adapter
    {
        private readonly View _page0;
        private readonly View _page1;

        public VerticalPagerAdapter(View page0, View page1)
        {
            _page0 = page0;
            _page1 = page1;
        }

        public override int ItemCount => 2;

        public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
        {
            var view = viewType == 0 ? _page0 : _page1;
            if (view.Parent is ViewGroup p) p.RemoveView(view);
            return new PageViewHolder(view);
        }

        public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position) { }

        public override int GetItemViewType(int position) => position;
    }

    private class PageViewHolder : RecyclerView.ViewHolder
    {
        public PageViewHolder(View view) : base(view) { }
    }

    /// <summary>垂直 ViewPager2 页面切换回调</summary>
    private class VerticalPagerPageCallback : ViewPager2.OnPageChangeCallback
    {
        private readonly NowPlayingFragment _parent;
        public VerticalPagerPageCallback(NowPlayingFragment parent) => _parent = parent;

        public override void OnPageSelected(int position)
        {
            if (position == 1)
            {
                _parent.LoadDetailDataAsync();
                // 歌曲详情页面禁用主 ViewPager2 左右滑动
                MainActivity.Instance?.SetViewPagerSwipeEnabled(false);
            }
            else
            {
                // 回到播放页面恢复主 ViewPager2 滑动
                MainActivity.Instance?.SetViewPagerSwipeEnabled(true);
            }
        }
    }
}