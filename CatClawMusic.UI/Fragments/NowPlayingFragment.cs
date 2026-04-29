using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using Android.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.UI.ViewModels;
using CatClawMusic.UI.Views;
using Microsoft.Extensions.DependencyInjection;
using GoogleSlider = Google.Android.Material.Slider.Slider;

namespace CatClawMusic.UI.Fragments;

public class NowPlayingFragment : Fragment
{
    private NowPlayingViewModel _viewModel = null!;
    private ImageView _albumCover = null!;
    private TextView _songTitle = null!, _songArtist = null!;
    private TextView _lyricPrev = null!, _lyricCurrent = null!, _lyricNext = null!;
    private TextView _timeCurrent = null!, _timeTotal = null!;
    private ImageButton _btnPlayPause = null!, _btnNext = null!, _btnPrev = null!;
    private ImageButton _btnBack = null!, _btnLike = null!, _btnMode = null!, _btnShuffle = null!;
    private GoogleSlider _progressSlider = null!;
    private SpectrumView _spectrumView = null!;
    private bool _isSliding;
    private readonly Android.OS.Handler _sliderHandler = new(Android.OS.Looper.MainLooper!);

    // Visualizer
    private Android.Media.Audiofx.Visualizer? _visualizer;
    private bool _visActive;

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
        => inflater.Inflate(Resource.Layout.fragment_now_playing, container, false)!;

    public override void OnViewCreated(View view, Bundle? state)
    {
        base.OnViewCreated(view, state);
        _viewModel = MainApplication.Services.GetRequiredService<NowPlayingViewModel>();
        var nav = MainApplication.Services.GetRequiredService<INavigationService>();
        var player = MainApplication.Services.GetRequiredService<IAudioPlayerService>();

        _albumCover = view.FindViewById<ImageView>(Resource.Id.album_cover)!;
        _songTitle = view.FindViewById<TextView>(Resource.Id.song_title)!;
        _songArtist = view.FindViewById<TextView>(Resource.Id.song_artist)!;
        _lyricPrev = view.FindViewById<TextView>(Resource.Id.lyric_prev)!;
        _lyricCurrent = view.FindViewById<TextView>(Resource.Id.lyric_current)!;
        _lyricNext = view.FindViewById<TextView>(Resource.Id.lyric_next)!;
        _timeCurrent = view.FindViewById<TextView>(Resource.Id.time_current)!;
        _timeTotal = view.FindViewById<TextView>(Resource.Id.time_total)!;
        _btnPlayPause = view.FindViewById<ImageButton>(Resource.Id.btn_play_pause)!;
        _btnNext = view.FindViewById<ImageButton>(Resource.Id.btn_next)!;
        _btnPrev = view.FindViewById<ImageButton>(Resource.Id.btn_prev)!;
        _btnBack = view.FindViewById<ImageButton>(Resource.Id.btn_back)!;
        _btnLike = view.FindViewById<ImageButton>(Resource.Id.btn_like)!;
        _btnMode = view.FindViewById<ImageButton>(Resource.Id.btn_mode)!;
        _btnShuffle = view.FindViewById<ImageButton>(Resource.Id.btn_shuffle)!;
        _progressSlider = view.FindViewById<GoogleSlider>(Resource.Id.progress_slider)!;

        // 频谱控件
        var spectrumContainer = view.FindViewById<FrameLayout>(Resource.Id.spectrum_container)!;
        _spectrumView = new SpectrumView(Context!);
        spectrumContainer.AddView(_spectrumView,
            new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        // 播放控制
        _btnPlayPause.Click += (s, e) => _viewModel.PlayPauseCommand.Execute(null);
        _btnNext.Click += (s, e) => _viewModel.NextCommand.Execute(null);
        _btnPrev.Click += (s, e) => _viewModel.PreviousCommand.Execute(null);
        _btnBack.Click += (s, e) => nav.GoBack();
        _btnLike.Click += (s, e) => _viewModel.ToggleLikeCommand.Execute(null);
        _btnMode.Click += (s, e) => _viewModel.TogglePlayModeCommand.Execute(null);
        _btnShuffle.Click += (s, e) => _viewModel.ToggleShuffleCommand.Execute(null);

        // 进度条 — ChangeListener + Handler 延迟检测拖拽结束
        _progressSlider.AddOnChangeListener(new SliderChangeListener(_sliderHandler,
            () => _isSliding = true,
            () => { _isSliding = false; _viewModel.CurrentPositionSeconds = _progressSlider.Value; }
        ));

        SyncUIFromViewModel();
        BindViewModel();
    }

    private void SyncUIFromViewModel()
    {
        if (!string.IsNullOrEmpty(_viewModel.CoverSource))
            _albumCover.SetImageDrawable(Drawable.CreateFromPath(_viewModel.CoverSource));
        _songTitle.Text = _viewModel.CurrentSong?.Title ?? "选择歌曲";
        _songArtist.Text = _viewModel.CurrentSong?.Artist ?? "未知艺术家";
        UpdateTimeDisplay();
        UpdateSlider();
        UpdatePlayPauseIcon();
        UpdateModeIcon();
        UpdateLikeIcon();
        UpdateLyrics();
    }

    private void BindViewModel()
    {
        _viewModel.PropertyChanged += (s, e) =>
        {
            var act = Activity;
            if (act == null) return;
            act.RunOnUiThread(() =>
            {
                switch (e.PropertyName)
                {
                    case nameof(_viewModel.CoverSource):
                        if (!string.IsNullOrEmpty(_viewModel.CoverSource))
                            _albumCover.SetImageDrawable(Drawable.CreateFromPath(_viewModel.CoverSource));
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
                        break;
                    case nameof(_viewModel.PlayModeIcon):
                        UpdateModeIcon();
                        break;
                    case nameof(_viewModel.LikeIcon):
                        UpdateLikeIcon();
                        break;
                    case nameof(_viewModel.CurrentSong):
                        _songTitle.Text = _viewModel.CurrentSong?.Title ?? "选择歌曲";
                        _songArtist.Text = _viewModel.CurrentSong?.Artist ?? "未知艺术家";
                        break;
                    case nameof(_viewModel.CurrentLyricLine):
                        UpdateLyrics();
                        break;
                }
            });
        };
    }

    private void UpdateLyrics()
    {
        _lyricPrev.Text = "";  // 上一句由 ViewModel 提供或为空白
        _lyricCurrent.Text = _viewModel.CurrentLyricLine;
        _lyricNext.Text = _viewModel.NextLyricLine;
    }

    private void UpdateTimeDisplay()
    {
        _timeCurrent.Text = $"{_viewModel.CurrentPosition.Minutes}:{_viewModel.CurrentPosition.Seconds:D2}";
        _timeTotal.Text = $"{_viewModel.TotalDuration.Minutes}:{_viewModel.TotalDuration.Seconds:D2}";
    }

    private void UpdateSlider()
    {
        if (_isSliding) return;
        var dur = (float)_viewModel.TotalDurationSeconds;
        if (dur > 0)
        {
            _progressSlider.ValueTo = dur;
            _progressSlider.Value = (float)_viewModel.CurrentPositionSeconds;
        }
    }

    private void UpdatePlayPauseIcon()
    {
        _btnPlayPause.SetImageResource(
            _viewModel.PlayPauseIcon == "⏸" ? Resource.Drawable.ic_pause : Resource.Drawable.ic_play);
    }

    private void UpdateLikeIcon()
    {
        _btnLike.SetImageResource(
            _viewModel.LikeIcon == "❤️" ? Resource.Drawable.ic_favorite : Resource.Drawable.ic_favorite_border);
    }

    private void UpdateModeIcon()
    {
        _btnMode.SetImageResource(Resource.Drawable.ic_repeat);
        _btnShuffle.SetImageResource(Resource.Drawable.ic_shuffle);
        _btnMode.SetColorFilter(
            _viewModel.PlayModeIcon is "🔁" or "🔂"
                ? Android.Graphics.Color.ParseColor("#9B7ED8")
                : Android.Graphics.Color.ParseColor("#B0A8BA"));
        _btnShuffle.SetColorFilter(
            _viewModel.PlayModeIcon == "🔀"
                ? Android.Graphics.Color.ParseColor("#9B7ED8")
                : Android.Graphics.Color.ParseColor("#B0A8BA"));
    }

    // ═══════════ Visualizer ═══════════

    public override void OnResume()
    {
        base.OnResume();
        _viewModel.SyncWithQueue();
        SyncUIFromViewModel();
        StartVisualizer();
    }

    public override void OnPause()
    {
        base.OnPause();
        StopVisualizer();
    }

    private void StartVisualizer()
    {
        try
        {
            StopVisualizer();
            _visualizer = new Android.Media.Audiofx.Visualizer(0);
            _visualizer.SetCaptureSize(Android.Media.Audiofx.Visualizer.GetCaptureSizeRange()[1]);

            _visualizer.SetDataCaptureListener(new VisCaptureListener(data =>
            {
                if (!_visActive) return;
                var fft = new float[data.Length];
                for (int i = 0; i < data.Length; i++)
                    fft[i] = Math.Abs((sbyte)data[i]) / 128f;
                _spectrumView.UpdateFftData(fft);
            }), 10000, false, true);

            _visualizer.SetEnabled(true);
            _visActive = true;
        }
        catch
        {
            _visActive = false;
        }
    }

    private void StopVisualizer()
    {
        _visActive = false;
        try
        {
            if (_visualizer != null)
            {
                _visualizer.SetEnabled(false);
                _visualizer.Release();
                _visualizer.Dispose();
                _visualizer = null;
            }
        }
        catch { }
    }

    /// <summary>Visualizer 波形数据回调</summary>
    private class VisCaptureListener : Java.Lang.Object,
        Android.Media.Audiofx.Visualizer.IOnDataCaptureListener
    {
        private readonly Action<byte[]> _onWaveform;
        public VisCaptureListener(Action<byte[]> onWaveform) => _onWaveform = onWaveform;
        public void OnWaveFormDataCapture(Android.Media.Audiofx.Visualizer? visualizer, byte[]? waveform, int samplingRate)
        {
            if (waveform != null) _onWaveform(waveform);
        }
        public void OnFftDataCapture(Android.Media.Audiofx.Visualizer? visualizer, byte[]? fft, int samplingRate) { }
    }

    /// <summary>Material Slider 拖拽回调</summary>
    private class SliderChangeListener : Java.Lang.Object,
        Google.Android.Material.Slider.Slider.IOnChangeListener
    {
        private readonly Android.OS.Handler _handler;
        private readonly Action _onStart, _onStop;
        private Java.Lang.IRunnable? _pendingStop;

        public SliderChangeListener(Android.OS.Handler handler, Action onStart, Action onStop)
        { _handler = handler; _onStart = onStart; _onStop = onStop; }

        public void OnValueChange(Google.Android.Material.Slider.Slider? slider, float value, bool fromUser)
        {
            if (!fromUser) return;
            _onStart();
            _pendingStop?.Dispose();
            _pendingStop = new StopRunnable(_onStop, () => _pendingStop = null);
            _handler.PostDelayed(_pendingStop, 400);
        }

        private class StopRunnable : Java.Lang.Object, Java.Lang.IRunnable
        {
            private readonly Action _action, _cleanup;
            public StopRunnable(Action action, Action cleanup) { _action = action; _cleanup = cleanup; }
            public void Run() { try { _action(); } finally { _cleanup(); } }
        }
    }
}
