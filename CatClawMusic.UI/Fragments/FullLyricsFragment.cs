using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using Android.Widget;
using CatClawMusic.Core.Models;
using CatClawMusic.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

public class FullLyricsFragment : Fragment
{
    private NowPlayingViewModel _viewModel = null!;
    private ImageView _bgCover = null!;
    private ScrollView _scrollView = null!;
    private LinearLayout _lyricsContainer = null!;
    private TextView _songTitle = null!, _songArtist = null!;
    private TextView _progressText = null!;
    private readonly List<TextView> _lyricViews = new();
    private int _lastLyricIndex = -1;
    private bool _userScrolling;
    private readonly Handler _scrollResumeHandler = new(Looper.MainLooper!);
    private string? _lastCoverSource;

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
        => inflater.Inflate(Resource.Layout.fragment_full_lyrics, container, false)!;

    public override void OnViewCreated(View view, Bundle? state)
    {
        base.OnViewCreated(view, state);
        _viewModel = MainApplication.Services.GetRequiredService<NowPlayingViewModel>();

        _bgCover = view.FindViewById<ImageView>(Resource.Id.lyric_bg_cover)!;
        _scrollView = view.FindViewById<ScrollView>(Resource.Id.lyrics_scroll)!;
        _lyricsContainer = view.FindViewById<LinearLayout>(Resource.Id.lyrics_container)!;
        _songTitle = view.FindViewById<TextView>(Resource.Id.lyric_song_title)!;
        _songArtist = view.FindViewById<TextView>(Resource.Id.lyric_song_artist)!;
        _progressText = view.FindViewById<TextView>(Resource.Id.lyric_progress_text)!;

        ApplyBlur();

        // 用户手动滚动 → 暂停自动滚动 3 秒
        _scrollView.ScrollChange += (s, e) =>
        {
            _userScrolling = true;
            _scrollResumeHandler.RemoveCallbacksAndMessages(null);
            _scrollResumeHandler.PostDelayed(() => _userScrolling = false, 3000);
        };

        BindViewModel();
        SyncUI();
    }

    private void ApplyBlur()
    {
        // API 31+ 使用 RenderEffect 实时模糊
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            _bgCover.SetRenderEffect(
                Android.Graphics.RenderEffect.CreateBlurEffect(120f, 120f, Android.Graphics.Shader.TileMode.Clamp));
        }
    }

    private void UpdateBackground()
    {
        var coverSource = _viewModel.CoverSource;
        if (coverSource == _lastCoverSource) return;
        _lastCoverSource = coverSource;

        if (!string.IsNullOrEmpty(coverSource))
        {
            var drawable = Drawable.CreateFromPath(coverSource);
            if (drawable != null)
            {
                _bgCover.SetImageDrawable(drawable);
                return;
            }
        }

        // 无封面时用默认图
        _bgCover.SetImageResource(Resource.Drawable.cover_default);
    }

    private void BindViewModel()
    {
        _viewModel.PropertyChanged += (s, e) =>
        {
            Activity?.RunOnUiThread(() =>
            {
                switch (e.PropertyName)
                {
                    case nameof(_viewModel.CurrentLyrics):
                        RebuildLyrics();
                        break;
                    case nameof(_viewModel.CurrentLyricIndex):
                        HighlightCurrentLine();
                        break;
                    case nameof(_viewModel.CurrentPosition):
                    case nameof(_viewModel.TotalDuration):
                        UpdateProgress();
                        break;
                    case nameof(_viewModel.CurrentSong):
                        _songTitle.Text = _viewModel.CurrentSong?.Title ?? "";
                        _songArtist.Text = _viewModel.CurrentSong?.Artist ?? "";
                        break;
                    case nameof(_viewModel.CoverSource):
                        UpdateBackground();
                        break;
                }
            });
        };
    }

    private void SyncUI()
    {
        var song = _viewModel.CurrentSong;
        _songTitle.Text = song?.Title ?? "";
        _songArtist.Text = song?.Artist ?? "";
        UpdateBackground();
        RebuildLyrics();
        UpdateProgress();
    }

    private void RebuildLyrics()
    {
        _lyricsContainer.RemoveAllViews();
        _lyricViews.Clear();
        _lastLyricIndex = -1;

        var lyrics = _viewModel.CurrentLyrics;
        if (lyrics?.Lines == null || lyrics.Lines.Count == 0)
        {
            var empty = new TextView(Context) { Text = "暂无歌词" };
            empty.SetTextSize(Android.Util.ComplexUnitType.Sp, 16);
            empty.SetTextColor(Color.ParseColor("#B0A8BA"));
            empty.Gravity = GravityFlags.Center;
            var lp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
            lp.TopMargin = 80;
            empty.LayoutParameters = lp;
            _lyricsContainer.AddView(empty);
            return;
        }

        for (int i = 0; i < lyrics.Lines.Count; i++)
        {
            var line = lyrics.Lines[i];
            var tv = new TextView(Context) { Text = line.Text };
            tv.SetTextSize(Android.Util.ComplexUnitType.Sp, 16);
            tv.SetTextColor(Color.ParseColor("#CCCCCC"));
            tv.Gravity = GravityFlags.Center;
            tv.SetLineSpacing(0, 1.4f);
            var lp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
            lp.TopMargin = i > 0 ? 16 : 0;
            tv.LayoutParameters = lp;
            tv.Tag = i;
            _lyricsContainer.AddView(tv);
            _lyricViews.Add(tv);
        }

        HighlightCurrentLine();
    }

    private void HighlightCurrentLine()
    {
        var idx = _viewModel.CurrentLyricIndex;
        if (idx == _lastLyricIndex || _lyricViews.Count == 0) return;

        // 重置所有行样式
        for (int i = 0; i < _lyricViews.Count; i++)
        {
            _lyricViews[i].SetTextSize(Android.Util.ComplexUnitType.Sp, 16);
            _lyricViews[i].SetTextColor(Color.ParseColor("#CCCCCC"));
        }

        // 高亮当前行
        if (idx >= 0 && idx < _lyricViews.Count)
        {
            var current = _lyricViews[idx];
            current.SetTextSize(Android.Util.ComplexUnitType.Sp, 20);
            current.SetTextColor(Color.ParseColor("#9B7ED8")); // 紫色高亮
        }

        _lastLyricIndex = idx;

        // 自动滚动（用户未手动滚动时）
        if (!_userScrolling && idx >= 0 && idx < _lyricViews.Count)
        {
            var target = _lyricViews[idx];
            var scrollY = target.Top - _scrollView.Height / 2 + target.Height / 2;
            _scrollView.SmoothScrollTo(0, Math.Max(0, scrollY));
        }
    }

    private void UpdateProgress()
    {
        var pos = _viewModel.CurrentPosition;
        var dur = _viewModel.TotalDuration;
        _progressText.Text = $"{pos.Minutes}:{pos.Seconds:D2} / {dur.Minutes}:{dur.Seconds:D2}";
    }

    public override void OnResume()
    {
        base.OnResume();
        SyncUI();
    }

    public override void OnDestroyView()
    {
        _scrollResumeHandler.RemoveCallbacksAndMessages(null);
        base.OnDestroyView();
    }
}
