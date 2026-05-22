using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Core.Models;
using CatClawMusic.UI.Adapters;
using CatClawMusic.UI.Helpers;
using CatClawMusic.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

public class PlaylistFragment : Fragment
{
    private PlaylistViewModel _viewModel = null!;
    private PlaylistAdapter _adapter = null!;
    private RecyclerView _playlistList = null!;
    private TextView _statusText = null!;
    private LibraryViewModel? _libraryVm;
    private EventHandler? _scanCompletedHandler;

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
    {
        return inflater.Inflate(Resource.Layout.fragment_playlist, container, false)!;
    }

    public override void OnViewCreated(View view, Bundle? state)
    {
        base.OnViewCreated(view, state);

        _viewModel = MainApplication.Services.GetRequiredService<PlaylistViewModel>();

        _playlistList = view.FindViewById<RecyclerView>(Resource.Id.playlist_list)!;
        _playlistList.SetLayoutManager(new LinearLayoutManager(Context));

        _statusText = view.FindViewById<TextView>(Resource.Id.status_text)!;

        _adapter = MainApplication.Services.GetRequiredService<PlaylistAdapter>();
        _adapter.PlaylistClicked += (s, playlist) =>
        {
            _viewModel.NavigateToPlaylist(playlist.Id, playlist.Name);
        };
        _adapter.PlaylistLongClicked += (s, playlist) =>
        {
            if (!playlist.IsSystem)
                ShowDeletePlaylistDialog(playlist);
        };
        _playlistList.SetAdapter(_adapter);

        _viewModel.Playlists.CollectionChanged += (s, e) =>
        {
            var a = Activity;
            if (a != null) a.RunOnUiThread(() =>
            {
                _adapter.UpdatePlaylists(_viewModel.Playlists);
                _statusText.Visibility = _viewModel.Playlists.Count == 0
                    ? ViewStates.Visible : ViewStates.Gone;
                _statusText.Text = _viewModel.StatusText;
            });
        };

        _adapter.NewPlaylistClicked += (s, e) => ShowCreatePlaylistDialog();

        _libraryVm = MainApplication.Services.GetService<LibraryViewModel>();
        if (_libraryVm != null)
        {
            _scanCompletedHandler = (s, e) =>
            {
                var a = Activity;
                if (a != null) a.RunOnUiThread(() => _ = _viewModel.LoadPlaylistsAsync());
            };
            _libraryVm.ScanCompleted += _scanCompletedHandler;
        }

        if (_viewModel.Playlists.Count > 0)
            _adapter.UpdatePlaylists(_viewModel.Playlists);
        else
            _ = _viewModel.LoadPlaylistsAsync();
    }

    private void ShowCreatePlaylistDialog()
    {
        var ctx = Context;
        if (ctx == null) return;

        new GlassDialog(ctx)
            .SetTitle("新建歌单")
            .AddInput("请输入歌单名称")
            .AddPositiveButton("创建", async (name) =>
            {
                if (!string.IsNullOrEmpty(name))
                    await _viewModel.CreatePlaylistAsync(name);
            })
            .AddNegativeButton("取消")
            .Show();
    }

    private void ShowDeletePlaylistDialog(Playlist playlist)
    {
        var ctx = Context;
        if (ctx == null) return;

        new GlassDialog(ctx)
            .SetTitle("删除歌单")
            .AddMessage($"确定要删除歌单「{playlist.Name}」吗？\n歌单中的歌曲不会被删除。")
            .AddItem("🗑  确认删除", async () =>
            {
                await _viewModel.DeletePlaylistAsync(playlist.Id);
            })
            .AddNegativeButton("取消")
            .Show();
    }

    public override void OnResume()
    {
        base.OnResume();
        _ = _viewModel.LoadPlaylistsAsync();
    }

    public override void OnDestroyView()
    {
        if (_libraryVm != null && _scanCompletedHandler != null)
            _libraryVm.ScanCompleted -= _scanCompletedHandler;
        base.OnDestroyView();
    }
}
