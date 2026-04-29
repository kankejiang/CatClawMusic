using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using Android.Widget;
using CatClawMusic.UI.Helpers;
using CatClawMusic.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

public class NowPlayingFragment : Fragment
{
    private NowPlayingViewModel _viewModel = null!;
    private ImageView _albumCover = null!;
    private TextView _songTitle = null!, _songArtist = null!, _songAlbum = null!;
    private TextView _lyricCurrent = null!, _lyricNext = null!;
    private TextView _timeCurrent = null!, _timeTotal = null!;
    private ImageButton _btnPlayPause = null!, _btnNext = null!, _btnPrev = null!;

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
        => inflater.Inflate(Resource.Layout.fragment_now_playing, container, false)!;

    public override void OnViewCreated(View view, Bundle? state)
    {
        base.OnViewCreated(view, state);
        _viewModel = MainApplication.Services.GetRequiredService<NowPlayingViewModel>();

        _albumCover = view.FindViewById<ImageView>(Resource.Id.album_cover)!;
        _songTitle = view.FindViewById<TextView>(Resource.Id.song_title)!;
        _songArtist = view.FindViewById<TextView>(Resource.Id.song_artist)!;
        _songAlbum = view.FindViewById<TextView>(Resource.Id.song_album)!;
        _lyricCurrent = view.FindViewById<TextView>(Resource.Id.lyric_current)!;
        _lyricNext = view.FindViewById<TextView>(Resource.Id.lyric_next)!;
        _timeCurrent = view.FindViewById<TextView>(Resource.Id.time_current)!;
        _timeTotal = view.FindViewById<TextView>(Resource.Id.time_total)!;
        _btnPlayPause = view.FindViewById<ImageButton>(Resource.Id.btn_play_pause)!;
        _btnNext = view.FindViewById<ImageButton>(Resource.Id.btn_next)!;
        _btnPrev = view.FindViewById<ImageButton>(Resource.Id.btn_prev)!;

        _btnPlayPause.Click += (s, e) => _viewModel.PlayPauseCommand.Execute(null);
        _btnNext.Click += (s, e) => _viewModel.NextCommand.Execute(null);
        _btnPrev.Click += (s, e) => _viewModel.PreviousCommand.Execute(null);

        BindViews();
    }

    private void BindViews()
    {
        BindingHelper.BindText(_songTitle, _viewModel, nameof(_viewModel.CurrentSong), _ => _viewModel.CurrentSong?.Title ?? "");
        BindingHelper.BindText(_songArtist, _viewModel, nameof(_viewModel.CurrentSong), _ => _viewModel.CurrentSong?.Artist ?? "");
        BindingHelper.BindText(_songAlbum, _viewModel, nameof(_viewModel.CurrentSong), _ => _viewModel.CurrentSong?.Album ?? "");
        BindingHelper.BindText(_lyricCurrent, _viewModel, nameof(_viewModel.CurrentLyricLine), _ => _viewModel.CurrentLyricLine);
        BindingHelper.BindText(_lyricNext, _viewModel, nameof(_viewModel.NextLyricLine), _ => _viewModel.NextLyricLine);

        _viewModel.PropertyChanged += (s, e) =>
        {
            var act = Activity;
            if (act == null) return;
            act.RunOnUiThread(() =>
            {
                if (e.PropertyName == nameof(_viewModel.CoverSource) && !string.IsNullOrEmpty(_viewModel.CoverSource))
                    _albumCover.SetImageDrawable(Drawable.CreateFromPath(_viewModel.CoverSource));
                if (e.PropertyName == nameof(_viewModel.CurrentPosition))
                {
                    _timeCurrent.Text = $"{_viewModel.CurrentPosition.Minutes}:{_viewModel.CurrentPosition.Seconds:D2}";
                    _timeTotal.Text = $"{_viewModel.TotalDuration.Minutes}:{_viewModel.TotalDuration.Seconds:D2}";
                }
            });
        };
    }

    public override void OnResume()
    {
        base.OnResume();
        _viewModel.SyncWithQueue();
    }
}
