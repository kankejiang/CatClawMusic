using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.UI.Adapters;
using CatClawMusic.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

public class PlaylistDetailFragment : Fragment
{
    private PlaylistDetailViewModel _viewModel = null!;
    private RecyclerView _songList = null!;
    private TextView _titleText = null!, _statusText = null!;
    private SongAdapter _adapter = null!;

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
        => inflater.Inflate(Resource.Layout.fragment_playlist_detail, container, false)!;

    public override void OnViewCreated(View view, Bundle? state)
    {
        base.OnViewCreated(view, state);
        _viewModel = MainApplication.Services.GetRequiredService<PlaylistDetailViewModel>();
        _songList = view.FindViewById<RecyclerView>(Resource.Id.song_list)!;
        _songList.SetLayoutManager(new LinearLayoutManager(Context));
        _titleText = view.FindViewById<TextView>(Resource.Id.title_text)!;
        _statusText = view.FindViewById<TextView>(Resource.Id.status_text)!;

        _adapter = MainApplication.Services.GetRequiredService<SongAdapter>();
        _adapter.SongClicked += (s, song) =>
        {
            _ = _viewModel.PlaySongAsync(song);
        };
        _songList.SetAdapter(_adapter);

        var args = Arguments;
        if (args != null)
        {
            int playlistId = args.GetInt("playlistId", 0);
            string name = args.GetString("playlistName") ?? "播放列表";
            _ = _viewModel.LoadAsync(playlistId, name);
        }

        _viewModel.Songs.CollectionChanged += (s, e) => { var a = Activity; if (a != null) a.RunOnUiThread(() => _adapter.UpdateSongs(_viewModel.Songs)); };
    }
}
