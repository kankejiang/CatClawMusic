using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.UI.Adapters;
using CatClawMusic.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

public class PlaylistFragment : Fragment
{
    private PlaylistViewModel _viewModel = null!;
    private RecyclerView _playlistList = null!;
    private PlaylistAdapter _adapter = null!;
    private TextView _emptyText = null!;

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
    {
        return inflater.Inflate(Resource.Layout.fragment_playlist, container, false)!;
    }

    public override void OnViewCreated(View view, Bundle? state)
    {
        base.OnViewCreated(view, state);
        _viewModel = MainApplication.Services.GetRequiredService<PlaylistViewModel>();
        _playlistList = view.FindViewById<RecyclerView>(Resource.Id.playlist_list)!;
        _emptyText = view.FindViewById<TextView>(Resource.Id.empty_text)!;

        _adapter = new PlaylistAdapter();
        _adapter.PlaylistClicked += OnPlaylistClicked;
        _playlistList.SetLayoutManager(new LinearLayoutManager(Context!));
        _playlistList.SetAdapter(_adapter);

        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(_viewModel.Playlists))
            {
                _adapter.UpdatePlaylists(_viewModel.Playlists);
                _emptyText.Visibility = _viewModel.Playlists.Count == 0 ? ViewStates.Visible : ViewStates.Gone;
            }
        };
    }

    private void OnPlaylistClicked(object? sender, Core.Models.Playlist pl)
    {
        var nav = MainApplication.Services.GetRequiredService<Core.Interfaces.INavigationService>();
        nav.PushFragment("PlaylistDetail", new Dictionary<string, object> { ["playlistId"] = pl.Id, ["playlistName"] = pl.Name });
    }

    public override void OnResume()
    {
        base.OnResume();
        _viewModel.Refresh();
        _adapter.UpdatePlaylists(_viewModel.Playlists);
        _emptyText.Visibility = _viewModel.Playlists.Count == 0 ? ViewStates.Visible : ViewStates.Gone;
    }
}
