using Android.OS;
using Android.Views;
using Android.Widget;
using CatClawMusic.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

public class PlaylistFragment : Fragment
{
    private PlaylistViewModel _viewModel = null!;
    private ListView _listView = null!;
    private TextView _emptyText = null!;

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
    {
        return inflater.Inflate(Resource.Layout.fragment_playlist, container, false)!;
    }

    public override void OnViewCreated(View view, Bundle? state)
    {
        base.OnViewCreated(view, state);
        _viewModel = MainApplication.Services.GetRequiredService<PlaylistViewModel>();
        _listView = view.FindViewById<ListView>(Resource.Id.playlist_list)!;
        _emptyText = view.FindViewById<TextView>(Resource.Id.empty_text)!;

        var adapter = new ArrayAdapter<string>(Context!, Android.Resource.Layout.SimpleListItem1,
            _viewModel.Playlists.Select(p => p.Name).ToList());
        _listView.Adapter = adapter;
        _listView.ItemClick += (s, e) =>
        {
            var pl = _viewModel.Playlists[e.Position];
            var nav = MainApplication.Services.GetRequiredService<Core.Interfaces.INavigationService>();
            nav.PushFragment("PlaylistDetail", new Dictionary<string, object> { ["playlistId"] = pl.Id, ["playlistName"] = pl.Name });
        };

        _emptyText.Visibility = _viewModel.Playlists.Count == 0 ? ViewStates.Visible : ViewStates.Gone;
    }
}
