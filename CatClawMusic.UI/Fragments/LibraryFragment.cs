using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;
using CatClawMusic.UI.Adapters;
using CatClawMusic.UI.ViewModels;
using CatClawMusic.UI.Helpers;
using Microsoft.Extensions.DependencyInjection;
using CoreModels = CatClawMusic.Core.Models;

namespace CatClawMusic.UI.Fragments;

public class LibraryFragment : Fragment
{
    private LibraryViewModel _viewModel = null!;
    private RecyclerView _songList = null!;
    private TextView _statusText = null!;
    private Button _btnLocal = null!;
    private Button _btnNetwork = null!;
    private SongAdapter _adapter = null!;

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
    {
        return inflater.Inflate(Resource.Layout.fragment_library, container, false)!;
    }

    public override void OnViewCreated(View view, Bundle? state)
    {
        base.OnViewCreated(view, state);
        var sp = MainApplication.Services;

        _viewModel = sp.GetRequiredService<LibraryViewModel>();
        _songList = view.FindViewById<RecyclerView>(Resource.Id.song_list)!;
        _songList.SetLayoutManager(new LinearLayoutManager(Context));
        _statusText = view.FindViewById<TextView>(Resource.Id.status_text)!;
        _btnLocal = view.FindViewById<Button>(Resource.Id.btn_local)!;
        _btnNetwork = view.FindViewById<Button>(Resource.Id.btn_network)!;

        _adapter = sp.GetRequiredService<SongAdapter>();
        _adapter.SongClicked += OnSongClicked;
        _songList.SetAdapter(_adapter);

        _btnLocal.Click += (s, e) => _viewModel.SwitchTabCommand.Execute("Local");
        _btnNetwork.Click += (s, e) => _viewModel.SwitchTabCommand.Execute("Network");

        BindViews();
        _ = _viewModel.LoadLocalAsync();
        if (_viewModel.Songs.Count > 0)
            _adapter.UpdateSongs(_viewModel.Songs);
    }

    private void BindViews()
    {
        BindingHelper.BindText(_statusText, _viewModel, nameof(_viewModel.StatusText), _ => _viewModel.StatusText);
        _viewModel.Songs.CollectionChanged += (s, e) =>
        {
            var a = Activity;
            if (a != null) a.RunOnUiThread(() => _adapter.UpdateSongs(_viewModel.Songs));
        };
    }

    private void OnSongClicked(object? sender, CoreModels.Song song)
    {
        var queue = MainApplication.Services.GetRequiredService<PlayQueue>();
        queue.SetSongs(_viewModel.Songs);
        queue.SelectSong(song.Id);
        _ = MainApplication.Services.GetRequiredService<IAudioPlayerService>().PlayAsync(song.FilePath);
        // 同步迷你播放器
        MainApplication.Services.GetRequiredService<NowPlayingViewModel>().SetCurrentSong(song);
        // 记录播放历史
        _ = MainApplication.Services.GetRequiredService<MusicDatabase>().RecordPlayAsync(song.Id);
    }
}
