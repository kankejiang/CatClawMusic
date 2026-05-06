using Android.App;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using Android.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using GoogleSlider = Google.Android.Material.Slider.Slider;

namespace CatClawMusic.UI.Fragments;

public class NowPlayingFragment : Fragment
{
    private NowPlayingViewModel _viewModel = null!;
    private ImageView _albumCover = null!;
    private TextView _songTitle = null!, _songArtist = null!;
    private TextView _lyricPrev2 = null!, _lyricPrev = null!, _lyricCurrent = null!, _lyricNext = null!, _lyricNext2 = null!;
    private TextView _timeCurrent = null!, _timeTotal = null!;
    private ImageButton _btnPlayPause = null!, _btnNext = null!, _btnPrev = null!;
    private ImageButton _btnLike = null!, _btnModeCycle = null!, _btnPlaylist = null!;
    private GoogleSlider _progressSlider = null!;

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
        => inflater.Inflate(Resource.Layout.fragment_now_playing, container, false)!;

    public override void OnViewCreated(View view, Bundle? state)
    {
        base.OnViewCreated(view, state);
        _viewModel = MainApplication.Services.GetRequiredService<NowPlayingViewModel>();
        var player = MainApplication.Services.GetRequiredService<IAudioPlayerService>();

        _albumCover = view.FindViewById<ImageView>(Resource.Id.album_cover)!;
        _songTitle = view.FindViewById<TextView>(Resource.Id.song_title)!;
        _songArtist = view.FindViewById<TextView>(Resource.Id.song_artist)!;
        _lyricPrev2 = view.FindViewById<TextView>(Resource.Id.lyric_prev2)!;
        _lyricPrev = view.FindViewById<TextView>(Resource.Id.lyric_prev)!;
        _lyricCurrent = view.FindViewById<TextView>(Resource.Id.lyric_current)!;
        _lyricNext = view.FindViewById<TextView>(Resource.Id.lyric_next)!;
        _lyricNext2 = view.FindViewById<TextView>(Resource.Id.lyric_next2)!;
        _timeCurrent = view.FindViewById<TextView>(Resource.Id.time_current)!;
        _timeTotal = view.FindViewById<TextView>(Resource.Id.time_total)!;
        _btnPlayPause = view.FindViewById<ImageButton>(Resource.Id.btn_play_pause)!;
        _btnNext = view.FindViewById<ImageButton>(Resource.Id.btn_next)!;
        _btnPrev = view.FindViewById<ImageButton>(Resource.Id.btn_prev)!;
        _btnLike = view.FindViewById<ImageButton>(Resource.Id.btn_like)!;
        _btnModeCycle = view.FindViewById<ImageButton>(Resource.Id.btn_mode_cycle)!;
        _btnPlaylist = view.FindViewById<ImageButton>(Resource.Id.btn_playlist)!;
        _progressSlider = view.FindViewById<GoogleSlider>(Resource.Id.progress_slider)!;

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
        var controlsCard = view.FindViewById<View>(Resource.Id.controls_card)!;
        controlsCard.SetOnTouchListener(new ControlsTouchListener());

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

    private void SyncUIFromViewModel()
    {
        try
        {
            if (_albumCover == null) return;
            System.Diagnostics.Debug.WriteLine($"[CatClaw] SyncUI: song={_viewModel.CurrentSong?.Title}(Id={_viewModel.CurrentSong?.Id}), cover={_viewModel.CoverSource?.Substring(0, Math.Min(50, _viewModel.CoverSource?.Length ?? 0))}");

            // 如果 CurrentSong 有值但封面/歌词还没加载过，重新触发加载
            if (_viewModel.CurrentSong != null && string.IsNullOrEmpty(_viewModel.CoverSource))
            {
                _ = _viewModel.LoadCoverAsync(_viewModel.CurrentSong);
                _ = _viewModel.LoadLyricsAsync(_viewModel.CurrentSong);
            }

            if (!string.IsNullOrEmpty(_viewModel.CoverSource))
                _albumCover.SetImageDrawable(Drawable.CreateFromPath(_viewModel.CoverSource));
            else
                _albumCover.SetImageResource(Resource.Drawable.cover_default);
            _songTitle.Text = _viewModel.CurrentSong?.Title ?? "选择歌曲";
            if (_viewModel.CurrentSong?.Source == SongSource.WebDAV && _viewModel.CurrentSong.Artist == "未知艺术家")
                _songArtist.Text = "正在加载...";
            else
                _songArtist.Text = string.IsNullOrEmpty(_viewModel.CurrentSong?.Artist) ? "未知艺术家" : _viewModel.CurrentSong!.Artist;
            UpdateTimeDisplay();
            UpdateSlider();
            UpdatePlayPauseIcon();
            UpdateModeIcon();
            UpdateLikeIcon();
            UpdateLyrics();
        }
        catch { /* Hide/Show 后视图可能短暂无效，忽略 */ }
    }

    private void BindViewModel()
    {
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void UnbindViewModel()
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

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
                        _albumCover.SetImageDrawable(Drawable.CreateFromPath(_viewModel.CoverSource));
                    else
                        _albumCover.SetImageResource(Resource.Drawable.cover_default);
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
            }
        });
    }

    private void UpdateLyrics()
    {
        var prev2 = _viewModel.PrevLyricLine2;
        var prev = _viewModel.PrevLyricLine;
        var curr = _viewModel.CurrentLyricLine;
        var next = _viewModel.NextLyricLine;
        var next2 = _viewModel.NextLyricLine2;
        
        _lyricPrev2.Text = prev2;
        _lyricPrev.Text = prev;
        _lyricCurrent.Text = curr;
        _lyricNext.Text = next;
        _lyricNext2.Text = next2;
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
            if (!_progressSlider.Pressed) // 拖动时不覆盖用户操作
                _progressSlider.Value = Math.Min((float)_viewModel.CurrentPositionSeconds, dur);
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

    public override void OnHiddenChanged(bool hidden)
    {
        base.OnHiddenChanged(hidden);
        if (!hidden)
            SyncUIFromViewModel();
    }

    public override void OnResume()
    {
        base.OnResume();
        var queue = MainApplication.Services.GetRequiredService<PlayQueue>();
        if (queue.CurrentSong != null)
        {
            _viewModel.SyncWithQueue();
            SyncUIFromViewModel();
        }
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

    public override void OnDestroyView()
    {
        UnbindViewModel();
        _playlistDialog?.Dismiss();
        _playlistDialog = null;
        base.OnDestroyView();
    }

    internal class SliderTouchListener : Java.Lang.Object, View.IOnTouchListener
    {
        private readonly Action<float> _onEnd;
        public SliderTouchListener(Action<float> onEnd) => _onEnd = onEnd;
        public bool OnTouch(View? v, Android.Views.MotionEvent? e)
        {
            if (e?.Action == MotionEventActions.Up && v is Google.Android.Material.Slider.Slider slider)
                _onEnd(slider.Value);
            return false; // 不消费，让 Slider 原生拖动正常工作
        }
    }

    internal class ControlsTouchListener : Java.Lang.Object, View.IOnTouchListener
    {
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