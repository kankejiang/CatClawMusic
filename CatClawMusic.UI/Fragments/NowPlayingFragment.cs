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
using GoogleSlider = Google.Android.Material.Slider.Slider;

namespace CatClawMusic.UI.Fragments;

/// <summary>
/// 正在播放Fragment，显示专辑封面、歌曲信息、歌词预览和播放控制
/// </summary>
public class NowPlayingFragment : Fragment
{
    private NowPlayingViewModel _viewModel = null!;
    private ImageView _albumCover = null!;
    private TextView _songTitle = null!, _songArtist = null!;
    private StrokeTextView _lyricPrev2 = null!, _lyricPrev = null!, _lyricCurrent = null!, _lyricNext = null!, _lyricNext2 = null!;
    private TextView _timeCurrent = null!, _timeTotal = null!;
    private ImageButton _btnPlayPause = null!, _btnNext = null!, _btnPrev = null!;
    private ImageButton _btnLike = null!, _btnModeCycle = null!, _btnPlaylist = null!;
    private GoogleSlider _progressSlider = null!;
    private View _gradientBackground = null!;
    private View _glow1 = null!, _glow2 = null!, _glow3 = null!;
    private View _glow4 = null!, _glow5 = null!, _glow6 = null!;
    private View _reflectionMaskBottom = null!, _coverFog = null!;
    private Google.Android.Material.Card.MaterialCardView _controlsCard = null!;
    private AudioVisualizerView _audioVisualizer = null!;
    private VisualizerHelper? _visualizerHelper;
    private Android.OS.Handler? _mainHandler;
    private ActivityResultLauncher? _recordAudioLauncher;

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
        _songTitle = view.FindViewById<TextView>(Resource.Id.song_title)!;
        _songArtist = view.FindViewById<TextView>(Resource.Id.song_artist)!;
        _lyricPrev2 = (StrokeTextView)view.FindViewById(Resource.Id.lyric_prev2)!;
        _lyricPrev = (StrokeTextView)view.FindViewById(Resource.Id.lyric_prev)!;
        _lyricCurrent = (StrokeTextView)view.FindViewById(Resource.Id.lyric_current)!;
        _lyricNext = (StrokeTextView)view.FindViewById(Resource.Id.lyric_next)!;
        _lyricNext2 = (StrokeTextView)view.FindViewById(Resource.Id.lyric_next2)!;
        _timeCurrent = view.FindViewById<TextView>(Resource.Id.time_current)!;
        _timeTotal = view.FindViewById<TextView>(Resource.Id.time_total)!;
        _btnPlayPause = view.FindViewById<ImageButton>(Resource.Id.btn_play_pause)!;
        _btnNext = view.FindViewById<ImageButton>(Resource.Id.btn_next)!;
        _btnPrev = view.FindViewById<ImageButton>(Resource.Id.btn_prev)!;
        _btnLike = view.FindViewById<ImageButton>(Resource.Id.btn_like)!;
        _btnModeCycle = view.FindViewById<ImageButton>(Resource.Id.btn_mode_cycle)!;
        _btnPlaylist = view.FindViewById<ImageButton>(Resource.Id.btn_playlist)!;
        _progressSlider = view.FindViewById<GoogleSlider>(Resource.Id.progress_slider)!;
        _gradientBackground = view.FindViewById<View>(Resource.Id.gradient_background)!;
        _glow1 = view.FindViewById<View>(Resource.Id.glow_1)!;
        _glow2 = view.FindViewById<View>(Resource.Id.glow_2)!;
        _glow3 = view.FindViewById<View>(Resource.Id.glow_3)!;
        _glow4 = view.FindViewById<View>(Resource.Id.glow_4)!;
        _glow5 = view.FindViewById<View>(Resource.Id.glow_5)!;
        _glow6 = view.FindViewById<View>(Resource.Id.glow_6)!;
        _reflectionMaskBottom = view.FindViewById<View>(Resource.Id.reflection_mask_bottom)!;
        _coverFog = view.FindViewById<View>(Resource.Id.cover_fog)!;
        _controlsCard = view.FindViewById<Google.Android.Material.Card.MaterialCardView>(Resource.Id.controls_card)!;
        _audioVisualizer = view.FindViewById<AudioVisualizerView>(Resource.Id.audio_visualizer)!;

        _recordAudioLauncher = RegisterForActivityResult(
            new ActivityResultContracts.RequestPermission(),
            new RecordAudioCallback(granted =>
            {
                if (granted)
                {
                    var playerService = MainApplication.Services.GetRequiredService<IAudioPlayerService>();
                    var sessionId = playerService.AudioSessionId;
                    if (sessionId != 0)
                        StartVisualizerWithSession(sessionId);
                }
            }));

        TryStartVisualizer();

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

        // 进度条：Touch 松开时 seek（SetOnTouchListener 不影响原生拖动）
        _progressSlider.SetOnTouchListener(new SliderTouchListener(v => _viewModel.CurrentPositionSeconds = v));

        SyncUIFromViewModel();
        BindViewModel();
    }

    /// <summary>
    /// 从封面文件提取主色调并更新渐变背景和发光效果
    /// </summary>
    private void UpdateGradientBackground()
    {
        if (_gradientBackground == null) return;

        var coverPath = _viewModel.CoverSource;
        if (string.IsNullOrEmpty(coverPath) || !System.IO.File.Exists(coverPath))
        {
            // 无封面时恢复默认背景
            ApplyDefaultBackground();
            return;
        }

        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var colors = CoverColorExtractor.ExtractFromFile(coverPath);
                if (colors.Count == 0) return;

                Activity?.RunOnUiThread(() => ApplyColorsToBackground(colors));
            }
            catch { }
        });
    }

    /// <summary>
    /// 应用 Material You 色调方案：背景、光晕、控件卡片、文字图标全部统一配色
    /// </summary>
    private void ApplyColorsToBackground(List<ColorEntry> entries)
    {
        if (_gradientBackground == null || entries.Count == 0) return;

        var palette = MaterialYouPalette.FromSeedColor(entries[0].Color);

        _gradientBackground.SetBackgroundColor(new Android.Graphics.Color(palette.Background));

        var screenW = Resources.DisplayMetrics.WidthPixels;
        var density = Resources.DisplayMetrics.Density;
        var transparent = Android.Graphics.Color.Argb(0, 0, 0, 0);

        var glowViews = new[] { _glow1, _glow2, _glow3, _glow4, _glow5, _glow6 };
        var radii     = new[] { 220f, 280f, 180f, 320f, 200f, 260f };
        var alphas    = new[] { 0x66, 0x55, 0x5A, 0x48, 0x4C, 0x50 };
        var widthsDp  = new[] { 220, 280, 180, 320, 200, 260 };
        var jitter    = new[] { -0.08f, 0.06f, -0.12f, 0.10f, -0.04f, 0.08f };

        var seedHsv = new float[3];
        Color.RGBToHSV(
            Color.GetRedComponent(entries[0].Color),
            Color.GetGreenComponent(entries[0].Color),
            Color.GetBlueComponent(entries[0].Color), seedHsv);

        for (int i = 0; i < glowViews.Length; i++)
        {
            if (glowViews[i] == null) continue;
            var entry = entries[i % entries.Count];
            var glowColor = Color.HSVToColor(new[] {
                seedHsv[0],
                Math.Min(seedHsv[1] * 0.7f, 0.35f),
                Math.Min(seedHsv[2] * 0.8f + 0.15f, 0.85f)
            });
            ApplyGlow(glowViews[i], ToAlpha(glowColor, alphas[i]), transparent, radii[i]);

            var glowPx = (int)(widthsDp[i] * density + 0.5f);
            var x = entry.CenterX * (screenW - glowPx) + jitter[i] * screenW;
            x = Math.Max(-glowPx * 0.3f, Math.Min(screenW - glowPx * 0.7f, x));
            glowViews[i].TranslationX = x;
        }

        if (_reflectionMaskBottom != null)
            _reflectionMaskBottom.Background = null;

        // 封面底部雾化过渡：封面底边 → 背景色
        ApplyFogToCover(palette.Background);

        // 控件卡片：Surface 色 + Outline 描边
        ApplyCardTheme(palette);

        // 文字颜色
        var onSurfaceColor = new Android.Graphics.Color(palette.OnSurface);
        _songTitle.SetTextColor(onSurfaceColor);
        _songArtist.SetTextColor(new Android.Graphics.Color(palette.OnSurfaceVariant));

        // 歌词颜色
        var onSurfaceVariant = new Android.Graphics.Color(palette.OnSurfaceVariant);
        var onSurfaceLight = Android.Graphics.Color.Argb(
            (int)(0x90 * 255f / 0xFF), Color.GetRedComponent(palette.OnSurfaceVariant),
            Color.GetGreenComponent(palette.OnSurfaceVariant), Color.GetBlueComponent(palette.OnSurfaceVariant));
        var onSurfaceLighter = Android.Graphics.Color.Argb(
            (int)(0xB0 * 255f / 0xFF), Color.GetRedComponent(palette.OnSurfaceVariant),
            Color.GetGreenComponent(palette.OnSurfaceVariant), Color.GetBlueComponent(palette.OnSurfaceVariant));
        _lyricCurrent.SetTextColor(onSurfaceColor);
        _lyricPrev.SetTextColor(onSurfaceLighter);
        _lyricNext.SetTextColor(onSurfaceLighter);
        _lyricPrev2.SetTextColor(onSurfaceLight);
        _lyricNext2.SetTextColor(onSurfaceLight);
    }

    private void ApplyFogToCover(int backgroundColor)
    {
        if (_coverFog == null) return;
        var fog = new GradientDrawable(
            GradientDrawable.Orientation.TopBottom,
            new int[] {
                Android.Graphics.Color.Argb(0, 0, 0, 0),
                backgroundColor
            });
        fog.SetGradientType(GradientType.LinearGradient);
        _coverFog.Background = fog;
    }

    private void ApplyCardTheme(MaterialYouPalette palette)
    {
        if (_controlsCard == null) return;

        var surfaceColor = new Android.Graphics.Color(palette.Surface);
        _controlsCard.SetCardBackgroundColor(Android.Graphics.Color.Argb(
            0x30, surfaceColor.R, surfaceColor.G, surfaceColor.B));
        _controlsCard.StrokeColor = palette.Outline;

        var onSurfaceColor = new Android.Graphics.Color(palette.OnSurface);
        var onSurfaceSemi = Android.Graphics.Color.Argb(
            (int)(0xEE * 255f / 0xFF), onSurfaceColor.R, onSurfaceColor.G, onSurfaceColor.B);
        var onSurfaceLight = Android.Graphics.Color.Argb(
            (int)(0xAA * 255f / 0xFF), onSurfaceColor.R, onSurfaceColor.G, onSurfaceColor.B);

        _timeCurrent.SetTextColor(onSurfaceSemi);
        _timeTotal.SetTextColor(onSurfaceSemi);

        var sliderCs = Android.Content.Res.ColorStateList.ValueOf(new Android.Graphics.Color(palette.Primary));
        _progressSlider.ThumbTintList = sliderCs;
        _progressSlider.TrackActiveTintList = sliderCs;
        _audioVisualizer.SetColors(palette.Primary);
        _progressSlider.HaloTintList = Android.Content.Res.ColorStateList.ValueOf(
            new Android.Graphics.Color(Android.Graphics.Color.Argb(0x30, onSurfaceColor.R, onSurfaceColor.G, onSurfaceColor.B)));
        _progressSlider.TrackInactiveTintList = Android.Content.Res.ColorStateList.ValueOf(
            new Android.Graphics.Color(Android.Graphics.Color.Argb(0x40, onSurfaceColor.R, onSurfaceColor.G, onSurfaceColor.B)));

        _btnPlayPause.ImageTintList = Android.Content.Res.ColorStateList.ValueOf(onSurfaceColor);
        _btnNext.ImageTintList = Android.Content.Res.ColorStateList.ValueOf(onSurfaceSemi);
        _btnPrev.ImageTintList = Android.Content.Res.ColorStateList.ValueOf(onSurfaceSemi);
        _btnLike.ImageTintList = Android.Content.Res.ColorStateList.ValueOf(onSurfaceLight);
        _btnModeCycle.ImageTintList = Android.Content.Res.ColorStateList.ValueOf(onSurfaceLight);
        _btnPlaylist.ImageTintList = Android.Content.Res.ColorStateList.ValueOf(onSurfaceSemi);
    }

    private static int ToAlpha(int color, int alpha)
    {
        return Android.Graphics.Color.Argb(alpha,
            Android.Graphics.Color.GetRedComponent(color),
            Android.Graphics.Color.GetGreenComponent(color),
            Android.Graphics.Color.GetBlueComponent(color));
    }

    private static void ApplyGlow(View view, int centerColor, int edgeColor, float radius)
    {
        if (view == null) return;
        var gd = new GradientDrawable();
        gd.SetGradientType(GradientType.RadialGradient);
        gd.SetGradientCenter(0.5f, 0.5f);
        gd.SetGradientRadius(radius);
        gd.SetColors(new int[] { centerColor, edgeColor });
        view.Background = gd;
    }

    /// <summary>
    /// 恢复默认深色背景及控件配色
    /// </summary>
    private void ApplyDefaultBackground()
    {
        if (_gradientBackground == null) return;
        var defaultBg = Android.Graphics.Color.ParseColor("#1A0E28");
        _gradientBackground.SetBackgroundColor(defaultBg);

        // 清除光晕
        if (_glow1 != null) _glow1.Background = null;
        if (_glow2 != null) _glow2.Background = null;
        if (_glow3 != null) _glow3.Background = null;
        if (_glow4 != null) _glow4.Background = null;
        if (_glow5 != null) _glow5.Background = null;
        if (_glow6 != null) _glow6.Background = null;
        if (_reflectionMaskBottom != null) _reflectionMaskBottom.Background = null;

        // 清除雾化
        if (_coverFog != null) _coverFog.Background = null;

        // 恢复卡片和控件默认配色
        if (_controlsCard != null)
        {
            var defaultSurface = Android.Graphics.Color.ParseColor("#26000000");
            _controlsCard.SetCardBackgroundColor(defaultSurface);
            _controlsCard.StrokeColor = Android.Graphics.Color.ParseColor("#15CCCCCC");
        }

        var defaultWhite = Android.Graphics.Color.ParseColor("#EEEEEE");
        var defaultBright = Android.Graphics.Color.ParseColor("#FFFFFF");
        var defaultLight = Android.Graphics.Color.ParseColor("#DDFFFFFF");

        _timeCurrent.SetTextColor(defaultLight);
        _timeTotal.SetTextColor(defaultLight);
        _btnPlayPause.ImageTintList = Android.Content.Res.ColorStateList.ValueOf(defaultBright);
        _btnNext.ImageTintList = Android.Content.Res.ColorStateList.ValueOf(defaultWhite);
        _btnPrev.ImageTintList = Android.Content.Res.ColorStateList.ValueOf(defaultWhite);
        _btnLike.ImageTintList = Android.Content.Res.ColorStateList.ValueOf(defaultBright);
        _btnModeCycle.ImageTintList = Android.Content.Res.ColorStateList.ValueOf(defaultBright);
        _btnPlaylist.ImageTintList = Android.Content.Res.ColorStateList.ValueOf(defaultLight);

        var sliderCs = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.ParseColor("#FFFFFF"));
        _progressSlider.ThumbTintList = sliderCs;
        _progressSlider.TrackActiveTintList = sliderCs;
        _audioVisualizer.SetColors(Android.Graphics.Color.ParseColor("#FFFFFF"));
        _progressSlider.HaloTintList = Android.Content.Res.ColorStateList.ValueOf(
            new Android.Graphics.Color(Android.Graphics.Color.Argb(0x30, 0xFF, 0xFF, 0xFF)));
        _progressSlider.TrackInactiveTintList = Android.Content.Res.ColorStateList.ValueOf(
            new Android.Graphics.Color(Android.Graphics.Color.Argb(0x50, 0xFF, 0xFF, 0xFF)));

        var defaultText = Android.Graphics.Color.ParseColor("#FFFFFF");
        var defaultGray = Android.Graphics.Color.ParseColor("#CCFFFFFF");
        _songTitle.SetTextColor(defaultText);
        _songArtist.SetTextColor(defaultGray);
        _lyricCurrent.SetTextColor(Android.Graphics.Color.ParseColor("#FF444444"));
        _lyricPrev.SetTextColor(Android.Graphics.Color.ParseColor("#B0999999"));
        _lyricNext.SetTextColor(Android.Graphics.Color.ParseColor("#B0999999"));
        _lyricPrev2.SetTextColor(Android.Graphics.Color.ParseColor("#90999999"));
        _lyricNext2.SetTextColor(Android.Graphics.Color.ParseColor("#90999999"));
    }

    private void SyncUIFromViewModel()
    {
        try
        {
            if (_albumCover == null) return;

            // 如果 CurrentSong 有值但封面/歌词还没加载过，重新触发加载
            if (_viewModel.CurrentSong != null && string.IsNullOrEmpty(_viewModel.CoverSource))
            {
                _ = _viewModel.LoadCoverAsync(_viewModel.CurrentSong);
                _ = _viewModel.LoadLyricsAsync(_viewModel.CurrentSong);
            }

            if (!string.IsNullOrEmpty(_viewModel.CoverSource))
            {
                _albumCover.SetImageDrawable(Drawable.CreateFromPath(_viewModel.CoverSource));
                UpdateGradientBackground();
            }
            else
            {
                _albumCover.SetImageResource(Resource.Drawable.cover_default);
            }
            _songTitle.Text = _viewModel.CurrentSong?.Title ?? "选择歌曲";
            if (_viewModel.CurrentSong?.Source == SongSource.WebDAV && _viewModel.CurrentSong.Artist == "未知艺术家")
                _songArtist.Text = "正在加载...";
            else
                _songArtist.Text = string.IsNullOrEmpty(_viewModel.CurrentSong?.Artist) ? "未知艺术家" : _viewModel.CurrentSong!.Artist;
            TryStartVisualizer();
            UpdateTimeDisplay();
            UpdateSlider();
            UpdatePlayPauseIcon();
            UpdateModeIcon();
            UpdateLikeIcon();
            UpdateLyrics();
        }
        catch { /* Hide/Show 后视图可能短暂无效，忽略 */ }
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
                    if (!string.IsNullOrEmpty(_viewModel.CoverSource))
                    {
                        _albumCover.SetImageDrawable(Drawable.CreateFromPath(_viewModel.CoverSource));
                        UpdateGradientBackground();
                    }
                    else
                    {
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
                    TryStartVisualizer();
                    break;
                case nameof(_viewModel.PlayModeIcon):
                    UpdateModeIcon();
                    break;
                case nameof(_viewModel.LikeIcon):
                    UpdateLikeIcon();
                    break;
                case nameof(_viewModel.CurrentSong):
                    _songTitle.Text = _viewModel.CurrentSong?.Title ?? "选择歌曲";
                    if (_viewModel.CurrentSong?.Source == SongSource.WebDAV && _viewModel.CurrentSong.Artist == "未知艺术家")
                        _songArtist.Text = "正在加载...";
                    else
                        _songArtist.Text = string.IsNullOrEmpty(_viewModel.CurrentSong?.Artist) ? "未知艺术家" : _viewModel.CurrentSong!.Artist;
                    break;
                case nameof(_viewModel.CurrentLyricLine):
                case nameof(_viewModel.PrevLyricLine):
                case nameof(_viewModel.PrevLyricLine2):
                case nameof(_viewModel.NextLyricLine):
                case nameof(_viewModel.NextLyricLine2):
                    UpdateLyrics();
                    break;
                case nameof(_viewModel.CurrentLyricSpannable):
                    ApplyLyricSpannable();
                    break;
            }
        });
    }

    private int _lastLyricIdx = -1; // 跟踪上次歌词索引

    /// <summary>
    /// 更新歌词预览区域，支持滚动动画过渡
    /// </summary>
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
            // 首次加载或切歌 → 直接设置
            _lyricPrev2.Text = prev2;  _lyricPrev2.TranslationY = 0f; _lyricPrev2.Alpha = 0.6f;
            _lyricPrev.Text = prev;    _lyricPrev.TranslationY = 0f;   _lyricPrev.Alpha = 1f;
            _lyricNext.Text = next;    _lyricNext.TranslationY = 0f;   _lyricNext.Alpha = 1f;
            _lyricNext2.Text = next2;  _lyricNext2.TranslationY = 0f; _lyricNext2.Alpha = 0.6f;
            ApplyCurrentLineWithSpannable(curr);
            return;
        }

        // ── 零延迟滚动动画：文字立即更新，动画只做视觉过渡 ──
        // 1. 立即更新所有文字
        _lyricPrev2.Text = prev2;
        _lyricPrev.Text = prev;
        _lyricNext.Text = next;
        _lyricNext2.Text = next2;
        ApplyCurrentLineWithSpannable(curr);

        // 2. 将每行设到"刚滚入"的起始偏移位置
        float[] startY = { -8f, -10f, 14f, 10f, 8f }; // 上方行从微上偏→归位，下方行从微下偏→归位
        float[] startAlpha = { 0.4f, 0.6f, 0f, 0.6f, 0.4f };
        float[] endAlpha = { 0.6f, 1f, 1f, 1f, 0.6f };
        long[] durations = { 180, 200, 250, 200, 180 };
        var views = new[] { _lyricPrev2, _lyricPrev, _lyricCurrent, _lyricNext, _lyricNext2 };

        for (int i = 0; i < 5; i++)
        {
            var v = views[i];
            v.Animate().Cancel(); // 取消进行中的动画

            // 设起始位
            v.TranslationY = startY[i];
            v.Alpha = startAlpha[i];

            // 动画滑回正常位置
            v.Animate()
                .TranslationY(0f)
                .Alpha(endAlpha[i])
                .SetDuration(durations[i])
                .SetInterpolator(new Android.Views.Animations.DecelerateInterpolator(1.5f))
                .Start();
        }
    }

    /// <summary>
    /// 将逐字着色的 Spannable 应用到当前歌词行 StrokeTextView
    /// </summary>
    private void ApplyLyricSpannable()
    {
        ApplyCurrentLineWithSpannable(null);
    }

    /// <summary>
    /// 设当前歌词文本，优先用 ViewModel 生成的逐字 Spannable
    /// </summary>
    private void ApplyCurrentLineWithSpannable(string? plainText)
    {
        if (_lyricCurrent == null) return;
        var spannable = _viewModel.CurrentLyricSpannable;
        if (spannable != null)
        {
            _lyricCurrent.SetText(spannable, TextView.BufferType.Spannable);
        }
        else if (plainText != null)
        {
            _lyricCurrent.Text = plainText;
            _lyricCurrent.Alpha = 1f;
        }
        _lyricCurrent.TranslationY = 0f;
    }

    /// <summary>
    /// 更新当前播放时间和总时长显示
    /// </summary>
    private void UpdateTimeDisplay()
    {
        _timeCurrent.Text = $"{_viewModel.CurrentPosition.Minutes}:{_viewModel.CurrentPosition.Seconds:D2}";
        _timeTotal.Text = $"{_viewModel.TotalDuration.Minutes}:{_viewModel.TotalDuration.Seconds:D2}";
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

    /// <summary>弹出当前播放列表悬浮窗（毛玻璃圆角卡片风格）</summary>
    private void ShowPlaylistDialog()
    {
        var act = Activity;
        if (act == null) return;

        var queue = MainApplication.Services.GetRequiredService<PlayQueue>();
        var allSongs = queue.GetSongs().ToList();
        if (allSongs.Count == 0) return;

        var currentSong = queue.CurrentSong;

        // 加载自定义布局
        var view = LayoutInflater.From(act)!.Inflate(Resource.Layout.dialog_playlist, null)!;
        var listView = view.FindViewById<ListView>(Resource.Id.playlist_list)!;

        // 构建适配器数据
        var adapter = new PlaylistSongAdapter(act, allSongs, currentSong);
        listView.Adapter = adapter;

        // 点击歌曲播放
        listView.ItemClick += (s, e) =>
        {
            var song = allSongs[e.Position];
            PlaySong(song);
            _playlistDialog?.Dismiss();
        };

        // 创建半透明 Dialog
        var dialog = new Android.App.Dialog(act, Android.Resource.Style.ThemeTranslucentNoTitleBar);
        dialog.SetContentView(view);
        dialog.SetCancelable(true);
        dialog.SetCanceledOnTouchOutside(true);

        // 点击背景关闭
        var root = view.FindViewById<FrameLayout>(Resource.Id.playlist_root)!;
        root.Click += (s, e) => dialog.Dismiss();

        // 自动滚到当前播放歌曲
        if (currentSong != null)
        {
            var idx = allSongs.IndexOf(currentSong);
            if (idx >= 0)
            {
                listView.Post(() => listView.SetSelection(idx));
            }
        }

        _playlistDialog = dialog;
        dialog.Show();
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
                ? Android.Graphics.Color.ParseColor("#9B7ED8")
                : Android.Graphics.Color.ParseColor("#B0A8BA"));
    }

    /// <summary>
    /// Fragment显示/隐藏时同步UI状态
    /// </summary>
    public override void OnHiddenChanged(bool hidden)
    {
        base.OnHiddenChanged(hidden);
        if (!hidden)
            SyncUIFromViewModel();
    }

    /// <summary>
    /// Fragment恢复可见时同步播放队列状态并刷新UI
    /// </summary>
    public override void OnResume()
    {
        base.OnResume();
        var queue = MainApplication.Services.GetRequiredService<PlayQueue>();
        if (queue.CurrentSong != null)
        {
            _viewModel.SyncWithQueue();
            SyncUIFromViewModel();
        }
        TryStartVisualizer();
        View?.PostDelayed(() =>
        {
            UpdateSlider();
            UpdatePlayPauseIcon();
            UpdateLyrics();
        }, 800);
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
        _visualizerHelper?.Stop();
        _visualizerHelper = null;
        UnbindViewModel();
        _playlistDialog?.Dismiss();
        _playlistDialog = null;
        base.OnDestroyView();
    }

    private int _lastVisualizerSessionId;

    private void TryStartVisualizer()
    {
        var playerService = MainApplication.Services.GetRequiredService<IAudioPlayerService>();
        var sessionId = playerService.AudioSessionId;
        if (sessionId == 0) return;

        if (_visualizerHelper != null && _visualizerHelper.IsEnabled && sessionId == _lastVisualizerSessionId)
            return;

        if (Activity?.CheckSelfPermission(Android.Manifest.Permission.RecordAudio) != Android.Content.PM.Permission.Granted)
        {
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
        _visualizerHelper.SpectrumUpdated += spectrum =>
        {
            _mainHandler.Post(() =>
            {
                _audioVisualizer?.UpdateSpectrum(spectrum);
                if (++spectrumCounter % 30 == 0)
                    Android.Util.Log.Debug("CatClaw", $"[CatClaw] SpectrumUpdated x{spectrumCounter}, sum={spectrum.Sum():F2}");
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
    internal class ControlsTouchListener : Java.Lang.Object, View.IOnTouchListener
    {
        /// <summary>
        /// 处理触摸事件，按下时请求父视图不要拦截，抬起时恢复
        /// </summary>
        public bool OnTouch(View? v, Android.Views.MotionEvent? e)
        {
            if (e == null || v == null) return false;
            if (e.Action == MotionEventActions.Down)
            {
                // 阻止父 ViewPager2 拦截触摸，允许控制区自由操作
                var parent = v.Parent;
                while (parent != null)
                {
                    parent.RequestDisallowInterceptTouchEvent(true);
                    parent = parent.Parent;
                }
            }
            else if (e.Action is MotionEventActions.Up or MotionEventActions.Cancel)
            {
                var parent = v.Parent;
                while (parent != null)
                {
                    parent.RequestDisallowInterceptTouchEvent(false);
                    parent = parent.Parent;
                }
            }
            return false; // 不消费，让子控件正常处理

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

            // 高亮当前歌曲
            view.SetBackgroundColor(isCurrent
                ? Android.Graphics.Color.Argb(40, 155, 126, 216)  // 淡紫色高亮
                : Android.Graphics.Color.Transparent);

            text1.SetTextColor(isCurrent
                ? Android.Graphics.Color.ParseColor("#9B7ED8")
                : Android.Graphics.Color.ParseColor("#DDDDDD"));

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
                    // 按下时请求父视图不拦截，确保我们能收到完整的事件序列
                    v.Parent?.RequestDisallowInterceptTouchEvent(true);
                    break;
                    
                case MotionEventActions.Move:
                    if (_isDown)
                    {
                        float dx = Math.Abs(e.GetX() - _downX);
                        float dy = Math.Abs(e.GetY() - _downY);
                        // 如果是明显的水平滑动，交还给 ViewPager2 处理
                        if (dx > 30 && dx > dy * 2)
                        {
                            v.Parent?.RequestDisallowInterceptTouchEvent(false);
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
                        // 短按 + 小移动 → 视为点击
                        if (dx < 40 && dy < 40 && dt < 500)
                            _onTap();
                    }
                    _isDown = false;
                    // 恢复父视图的事件拦截权限
                    v.Parent?.RequestDisallowInterceptTouchEvent(false);
                    break;
            }
            return false; // 不消费事件，让 ViewPager2 仍能处理滑动
        }
    }
}