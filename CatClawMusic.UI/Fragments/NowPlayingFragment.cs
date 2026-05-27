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
    private TextView _songTitle = null!, _songArtist = null!;
    private StrokeTextView _lyricPrev2 = null!, _lyricPrev = null!, _lyricCurrent = null!, _lyricNext = null!, _lyricNext2 = null!;
    private TextView _timeCurrent = null!, _timeTotal = null!;
    private ImageButton _btnPlayPause = null!, _btnNext = null!, _btnPrev = null!;
    private ImageButton _btnLike = null!, _btnModeCycle = null!, _btnPlaylist = null!;
    private ImageButton _btnVisualizerToggle = null!;
    private ImageButton _btnSleepTimer = null!;
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
    private LyricProgressCallback? _lyricProgressCallback;
    private bool _lyricGradientActive;
    private double _lyricLineStartMs;
    private double _lyricLineDurationMs;
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
        _btnSleepTimer = view.FindViewById<ImageButton>(Resource.Id.btn_sleep_timer)!;
        _progressSlider = view.FindViewById<GoogleSlider>(Resource.Id.progress_slider)!;
        _gradientBackground = view.FindViewById<SweepGradientView>(Resource.Id.gradient_background)!;
        _reflectionMaskBottom = view.FindViewById<View>(Resource.Id.reflection_mask_bottom)!;
        _coverFog = view.FindViewById<View>(Resource.Id.cover_fog)!;
        _coverGlow = view.FindViewById<View>(Resource.Id.cover_glow)!;
        _controlsCard = view.FindViewById<Google.Android.Material.Card.MaterialCardView>(Resource.Id.controls_card)!;
        _audioVisualizer = view.FindViewById<AudioVisualizerView>(Resource.Id.audio_visualizer)!;

        _progressSlider.ImportantForAutofill = Android.Views.ImportantForAutofill.No;
        _gradientBackground.ImportantForAutofill = Android.Views.ImportantForAutofill.No;
        _reflectionMaskBottom.ImportantForAutofill = Android.Views.ImportantForAutofill.No;
        _coverFog.ImportantForAutofill = Android.Views.ImportantForAutofill.No;
        _coverGlow.ImportantForAutofill = Android.Views.ImportantForAutofill.No;
        _controlsCard.ImportantForAutofill = Android.Views.ImportantForAutofill.No;
        _audioVisualizer.ImportantForAutofill = Android.Views.ImportantForAutofill.No;
        _btnPlayPause.ImportantForAutofill = Android.Views.ImportantForAutofill.No;
        _btnNext.ImportantForAutofill = Android.Views.ImportantForAutofill.No;
        _btnPrev.ImportantForAutofill = Android.Views.ImportantForAutofill.No;
        _btnLike.ImportantForAutofill = Android.Views.ImportantForAutofill.No;
        _btnModeCycle.ImportantForAutofill = Android.Views.ImportantForAutofill.No;
        _btnPlaylist.ImportantForAutofill = Android.Views.ImportantForAutofill.No;
        _btnVisualizerToggle.ImportantForAutofill = Android.Views.ImportantForAutofill.No;
        _btnSleepTimer.ImportantForAutofill = Android.Views.ImportantForAutofill.No;

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

        _audioVisualizer.Visibility = ViewStates.Gone;
        var visPrefs = Activity?.GetSharedPreferences("catclaw_prefs", Android.Content.FileCreationMode.Private);
        var visEnabled = visPrefs?.GetBoolean("visualizer_enabled", false) ?? false;
        if (visEnabled) TryStartVisualizer();

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
        _btnSleepTimer.Click -= OnSleepTimerClick; _btnSleepTimer.Click += OnSleepTimerClick;

        // 进度条：Touch 松开时 seek（SetOnTouchListener 不影响原生拖动）
        _progressSlider.SetOnTouchListener(new SliderTouchListener(v => _viewModel.CurrentPositionSeconds = v));

        SyncUIFromViewModel();
        BindViewModel();

        var playerSvc = MainApplication.Services.GetRequiredService<IAudioPlayerService>() as AudioPlayerService;
        if (playerSvc != null)
            playerSvc.AudioSessionIdChanged += OnAudioSessionIdChanged;
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
        var a = (int)(Android.Graphics.Color.GetAlphaComponent(from) + (Android.Graphics.Color.GetAlphaComponent(to) - Android.Graphics.Color.GetAlphaComponent(from)) * fraction);
        var r = (int)(Android.Graphics.Color.GetRedComponent(from) + (Android.Graphics.Color.GetRedComponent(to) - Android.Graphics.Color.GetRedComponent(from)) * fraction);
        var g = (int)(Android.Graphics.Color.GetGreenComponent(from) + (Android.Graphics.Color.GetGreenComponent(to) - Android.Graphics.Color.GetGreenComponent(from)) * fraction);
        var b = (int)(Android.Graphics.Color.GetBlueComponent(from) + (Android.Graphics.Color.GetBlueComponent(to) - Android.Graphics.Color.GetBlueComponent(from)) * fraction);
        return Android.Graphics.Color.Argb(a, r, g, b);
    }

    /// <summary>
    /// 启动流光动画：三个色带在背景区域随机缓慢漂移，带呼吸缩放效果。
    /// 使用多重正弦波叠加模拟随机运动，每个色带拥有独立的相位偏移，
    /// 确保运动轨迹各不相同且看起来自然有机。
    /// </summary>
    private void StartFlowAnimation()
    {
        if (_flowAnimator != null) return;

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

    /// <summary>
    /// 暂停流光动画，音乐暂停时调用。
    /// 色带停留在当前位置不动，呼吸也停止。
    /// </summary>
    private void PauseFlowAnimation()
    {
        _flowPaused = true;
        _flowPauseTime = SystemClock.ElapsedRealtime();
        if (_flowAnimator != null && _flowAnimator.IsRunning)
            _flowAnimator.Pause();
    }

    /// <summary>
    /// 恢复流光动画，音乐恢复播放时调用。
    /// 色带从暂停位置继续漂移和呼吸。
    /// </summary>
    private void ResumeFlowAnimation()
    {
        _flowPaused = false;
        _flowTimeOffset += SystemClock.ElapsedRealtime() - _flowPauseTime;
        if (_flowAnimator != null && _flowAnimator.IsStarted)
            _flowAnimator.Resume();
    }

    /// <summary>
    /// 停止流光动画并清理所有状态，Fragment 销毁时调用
    /// </summary>
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
                    bitmap = BitmapFactory.DecodeFile(newCoverPath);
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

    /// <summary>
    /// 应用 Material You 色调方案：背景、光晕、控件卡片、文字图标全部统一配色
    /// </summary>
    private void ApplySweepGradientBackground(List<ColorEntry> entries)
    {
        if (_gradientBackground == null || entries.Count == 0) return;

        var palette = MaterialYouPalette.FromSeedColor(entries[0].Color);

        var sorted = entries.OrderByDescending(e => e.Weight).ToList();
        var totalWeight = sorted.Sum(e => Math.Max(e.Weight, 0.01f));
        var topEntries = sorted.Where(e => e.Weight / totalWeight >= 0.20f).Take(3).ToList();

        if (topEntries.Count == 0)
            topEntries.Add(sorted[0]);
        if (topEntries.Count == 1)
            topEntries.Add(topEntries[0]);

        _flowColors = PickVividColors(topEntries);
        _flowPositions = CalculateWeightedPositions(topEntries);
        BuildAndApplySweepGradient();

        if (_reflectionMaskBottom != null)
            _reflectionMaskBottom.Background = null;

        ApplyFogToCover(palette.Background);
        ApplyCoverGlow(entries);
        ApplyCardTheme(palette);

        var onSurfaceColor = new Android.Graphics.Color(palette.OnSurface);
        _songTitle.SetTextColor(onSurfaceColor);
        _songArtist.SetTextColor(new Android.Graphics.Color(palette.OnSurfaceVariant));

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

    private void BuildAndApplySweepGradient()
    {
        if (_gradientBackground == null || _flowColors == null || _flowColors.Length < 3) return;
        var positions = _flowPositions;
        if (positions == null || positions.Length != _flowColors.Length)
        {
            var n = _flowColors.Length - 1;
            positions = new float[_flowColors.Length];
            for (int i = 0; i <= n; i++)
                positions[i] = (float)i / n;
            _flowPositions = positions;
        }
        _gradientBackground.SetGradient(_flowColors!, positions);
        _gradientBackground.SetRotationAngle(_sweepAngle);
    }

    private void UpdateSweepGradientColors()
    {
        if (_flowColors == null || _flowColors.Length < 3 || _gradientBackground == null) return;
        _gradientBackground.UpdateColors(_flowColors);
        _gradientBackground.SetRotationAngle(_sweepAngle);
    }

    private static float[] CalculateWeightedPositions(List<ColorEntry> entries)
    {
        if (entries.Count < 2)
            return new float[] { 0f, 0.5f, 1f };

        var totalWeight = entries.Sum(e => Math.Max(e.Weight, 0.01f));
        var positions = new float[entries.Count + 1];
        positions[0] = 0f;
        var cumulative = 0f;
        for (int i = 0; i < entries.Count; i++)
        {
            cumulative += entries[i].Weight / totalWeight;
            positions[i + 1] = Math.Clamp(cumulative, 0f, 1f);
        }
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
            Color.RGBToHSV(
                Color.GetRedComponent(entries[i].Color),
                Color.GetGreenComponent(entries[i].Color),
                Color.GetBlueComponent(entries[i].Color), hsv);

            if (hsv[1] < 0.15f)
            {
                float sat = Math.Clamp(hsv[2] * 0.06f, 0.02f, 0.06f);
                float val = Math.Clamp(hsv[2] * 0.35f + 0.50f, 0.30f, 1.0f);
                result[i] = (int)Color.HSVToColor(new[] { hsv[0], sat, val });
            }
            else
            {
                float sat = Math.Clamp(hsv[1] * 0.60f + 0.10f, 0.12f, 0.45f);
                float val = Math.Clamp(hsv[2] * 0.40f + 0.55f, 0.85f, 0.98f);
                result[i] = (int)Color.HSVToColor(new[] { hsv[0], sat, val });
            }
        }

        result[n] = result[0];
        return result;
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

    private void ApplyCoverGlow(List<ColorEntry> entries)
    {
        if (_coverGlow == null || entries.Count == 0) return;
        var seedColor = entries[0].Color;
        var r = Android.Graphics.Color.GetRedComponent(seedColor);
        var g = Android.Graphics.Color.GetGreenComponent(seedColor);
        var b = Android.Graphics.Color.GetBlueComponent(seedColor);
        var glowCenter = Android.Graphics.Color.Argb(0x40, r, g, b);
        var glowEdge = Android.Graphics.Color.Argb(0x00, r, g, b);
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

        var sliderCs = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.ParseColor("#FFFFFF"));
        _progressSlider.ThumbTintList = sliderCs;
        _progressSlider.TrackActiveTintList = sliderCs;
        _audioVisualizer.SetColors(Android.Graphics.Color.ParseColor("#FFFFFF"));
        _progressSlider.HaloTintList = Android.Content.Res.ColorStateList.ValueOf(
            new Android.Graphics.Color(Android.Graphics.Color.Argb(0x30, 0xFF, 0xFF, 0xFF)));
        _progressSlider.TrackInactiveTintList = Android.Content.Res.ColorStateList.ValueOf(
            new Android.Graphics.Color(Android.Graphics.Color.Argb(0x50, 0xFF, 0xFF, 0xFF)));

        _modeActiveColor = onSurfaceColor;

        var white = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.ParseColor("#FFFFFF"));
        var whiteSemi = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.ParseColor("#DDFFFFFF"));
        _btnPlayPause.ImageTintList = white;
        _btnNext.ImageTintList = white;
        _btnPrev.ImageTintList = white;
        _btnLike.ImageTintList = white;
        _btnModeCycle.ImageTintList = white;
        _btnPlaylist.ImageTintList = whiteSemi;

        var visWhite = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.ParseColor("#FFFFFF"));
        var visGray = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.ParseColor("#88FFFFFF"));
        _btnVisualizerToggle.ImageTintList = _visualizerEnabled ? visWhite : visGray;
        _btnSleepTimer.ImageTintList = _sleepCts != null ? visWhite : visGray;
    }

    /// <summary>
    /// 恢复默认深色背景及控件配色
    /// </summary>
    private void ApplyDefaultBackground()
    {
        if (_gradientBackground == null) return;
        var defaultBg = Android.Graphics.Color.ParseColor("#1A0E28");
        _gradientBackground.SetBackgroundColor(defaultBg);

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

        var defaultWhite = Android.Graphics.Color.ParseColor("#FFFFFF");
        var defaultLight = Android.Graphics.Color.ParseColor("#DDFFFFFF");

        _modeActiveColor = defaultWhite;

        _timeCurrent.SetTextColor(defaultLight);
        _timeTotal.SetTextColor(defaultLight);
        _btnPlayPause.ImageTintList = Android.Content.Res.ColorStateList.ValueOf(defaultWhite);
        _btnNext.ImageTintList = Android.Content.Res.ColorStateList.ValueOf(defaultWhite);
        _btnPrev.ImageTintList = Android.Content.Res.ColorStateList.ValueOf(defaultWhite);
        _btnLike.ImageTintList = Android.Content.Res.ColorStateList.ValueOf(defaultWhite);
        _btnModeCycle.ImageTintList = Android.Content.Res.ColorStateList.ValueOf(defaultWhite);
        _btnPlaylist.ImageTintList = Android.Content.Res.ColorStateList.ValueOf(defaultLight);

        var visWhite = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.ParseColor("#FFFFFF"));
        var visGray = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.ParseColor("#88FFFFFF"));
        _btnVisualizerToggle.ImageTintList = _visualizerEnabled ? visWhite : visGray;
        _btnSleepTimer.ImageTintList = _sleepCts != null ? visWhite : visGray;

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
                AnimateCoverChange(_viewModel.CoverSource);
                UpdateGradientBackground();
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
            ApplyVisualizerState(visualizerEnabled);
            if (visualizerEnabled) TryStartVisualizer();
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
                        AnimateCoverChange(_viewModel.CoverSource);
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
                    /* 音乐暂停时流光跟随暂停，恢复播放时流光继续 */
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
                    StopLyricGradientUpdates();
                    _songTitle.Text = _viewModel.CurrentSong?.Title ?? "选择歌曲";
                    if ((_viewModel.CurrentSong?.Source == SongSource.WebDAV || _viewModel.CurrentSong?.Source == SongSource.SMB) && _viewModel.CurrentSong.Artist == "未知艺术家")
                        _songArtist.Text = "正在加载...";
                    else
                        _songArtist.Text = string.IsNullOrEmpty(_viewModel.CurrentSong?.Artist) ? "未知艺术家" : _viewModel.CurrentSong!.Artist;
                    break;
                case nameof(_viewModel.CurrentLyricLine):
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
            _lyricPrev2.Text = prev2;  _lyricPrev2.TranslationY = 0f; _lyricPrev2.Alpha = 0.35f;
            _lyricPrev.Text = prev;    _lyricPrev.TranslationY = 0f;   _lyricPrev.Alpha = 0.45f;
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
        float[] startY = { -8f, -10f, 14f, 10f, 8f };
        float[] startAlpha = { 0.2f, 0.3f, 0f, 0.6f, 0.4f };
        float[] endAlpha = { 0.35f, 0.45f, 1f, 1f, 0.6f };
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
                .SetInterpolator(_lyricInterpolator)
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
    private void ApplyCurrentLineWithSpannable(string? plainText)
    {
        if (_lyricCurrent == null) return;
        var spannable = _viewModel.CurrentLyricSpannable;
        if (spannable != null)
        {
            _lyricCurrent.SetText(spannable, TextView.BufferType.Spannable);
            _lyricCurrent.SetupLyricGradient();
            CacheLyricLineTiming();
            StartLyricGradientUpdates();
        }
        else if (plainText != null)
        {
            _lyricCurrent.Text = plainText;
            _lyricCurrent.Alpha = 1f;
            StopLyricGradientUpdates();
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

    private void OnVisualizerToggleClick(object? s, EventArgs e)
    {
        var prefs = Activity?.GetSharedPreferences("catclaw_prefs", Android.Content.FileCreationMode.Private);
        var enabled = !(prefs?.GetBoolean("visualizer_enabled", false) ?? false);
        prefs?.Edit().PutBoolean("visualizer_enabled", enabled).Apply();
        ApplyVisualizerState(enabled);
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
            _audioVisualizer.Visibility = ViewStates.Gone;
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

    /// <summary>
    /// Fragment暂停时清理资源
    /// </summary>
    public override void OnPause()
    {
        base.OnPause();
    }

    /// <summary>
    /// Fragment销毁时清理资源，解绑事件，关闭对话框
    /// </summary>
    public override void OnDestroyView()
    {
        StopFlowAnimation();
        StopLyricGradientUpdates();
        var playerSvc = MainApplication.Services.GetService<IAudioPlayerService>() as AudioPlayerService;
        if (playerSvc != null)
            playerSvc.AudioSessionIdChanged -= OnAudioSessionIdChanged;
        _visualizerHelper?.Stop();
        _visualizerHelper = null;
        UnbindViewModel();
        _playlistDialog?.Dismiss();
        _playlistDialog = null;

        base.OnDestroyView();
    }

    private int _lastVisualizerSessionId;

    private void OnAudioSessionIdChanged(int newSessionId)
    {
        if (Activity == null) return;
        Activity.RunOnUiThread(() =>
        {
            if (!_visualizerEnabled) return;
            if (newSessionId == 0) return;
            _lastVisualizerSessionId = newSessionId;
            _visualizerHelper?.Stop();
            _visualizerHelper = null;
            StartVisualizerWithSession(newSessionId);
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
            var src = spectrum;
            if (_latestSpectrum.Length < src.Length)
                _latestSpectrum = new float[src.Length];
            Array.Copy(src, _latestSpectrum, src.Length);

            if (Interlocked.Exchange(ref _spectrumUpdateQueued, 1) == 1)
                return;

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

    private void StartLyricGradientUpdates()
    {
        if (_lyricGradientActive) return;
        _lyricGradientActive = true;
        _lyricProgressCallback ??= new LyricProgressCallback(this);
        Choreographer.Instance.PostFrameCallback(_lyricProgressCallback);
    }

    private void StopLyricGradientUpdates()
    {
        _lyricGradientActive = false;
    }

    private void CacheLyricLineTiming()
    {
        var lines = _viewModel.CurrentLyrics?.Lines;
        var idx = _viewModel.CurrentLyricIndex;
        if (lines == null || idx < 0 || idx >= lines.Count) { _lyricLineDurationMs = 0; return; }

        _lyricLineStartMs = lines[idx].Timestamp.TotalMilliseconds;
        if (idx + 1 < lines.Count)
        {
            var gap = (lines[idx + 1].Timestamp - lines[idx].Timestamp).TotalMilliseconds;
            _lyricLineDurationMs = gap > 0 && gap < 30000 ? gap : 5000;
        }
        else
        {
            _lyricLineDurationMs = 5000;
        }
    }

    private void UpdateLyricGradientProgress()
    {
        if (!_lyricGradientActive || _lyricCurrent == null || _lyricLineDurationMs <= 0) return;
        var nowMs = _viewModel.CurrentPosition.TotalMilliseconds;
        var progress = (float)Math.Clamp((nowMs - _lyricLineStartMs) / _lyricLineDurationMs, 0.0, 1.0);
        _lyricCurrent.SetLyricProgress(progress);
    }

    private class LyricProgressCallback : Java.Lang.Object, Choreographer.IFrameCallback
    {
        private readonly WeakReference<NowPlayingFragment> _fragment;
        private int _frameCount;
        public LyricProgressCallback(NowPlayingFragment f) => _fragment = new(f);

        public void DoFrame(long frameTimeNanos)
        {
            if (_fragment.TryGetTarget(out var f))
            {
                if (++_frameCount % 2 == 0)
                    f.UpdateLyricGradientProgress();
                if (f._lyricGradientActive)
                    Choreographer.Instance.PostFrameCallback(this);
            }
        }
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

            // 高亮当前歌曲
            view.SetBackgroundColor(isCurrent
                ? Android.Graphics.Color.Argb(40, 155, 126, 216)
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