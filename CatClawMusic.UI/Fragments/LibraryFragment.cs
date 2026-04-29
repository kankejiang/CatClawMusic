using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Services;
using CatClawMusic.UI.Adapters;
using CatClawMusic.UI.Helpers;
using CatClawMusic.UI.ViewModels;
using CoreModels = CatClawMusic.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

public class LibraryFragment : Fragment
{
    private LibraryViewModel _viewModel = null!;
    private RecyclerView _songList = null!;
    private TextView _statusText = null!;
    private TextView _permissionText = null!;
    private View _permissionOverlay = null!;
    private Button _btnLocal = null!;
    private Button _btnNetwork = null!;
    private Button _btnGrant = null!;
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
        _permissionText = view.FindViewById<TextView>(Resource.Id.permission_text)!;
        _permissionOverlay = view.FindViewById<View>(Resource.Id.permission_overlay)!;
        _btnLocal = view.FindViewById<Button>(Resource.Id.btn_local)!;
        _btnNetwork = view.FindViewById<Button>(Resource.Id.btn_network)!;
        _btnGrant = view.FindViewById<Button>(Resource.Id.btn_grant)!;

        _adapter = sp.GetRequiredService<SongAdapter>();
        _adapter.SongClicked += OnSongClicked;
        _songList.SetAdapter(_adapter);

        _btnLocal.Click += (s, e) => _viewModel.SwitchTabCommand.Execute("Local");
        _btnNetwork.Click += (s, e) => _viewModel.SwitchTabCommand.Execute("Network");
        _btnGrant.Click += (s, e) => _ = _viewModel.DirectRequestPermissionAsync();

        BindViews();
        _ = _viewModel.LoadLocalAsync();
    }

    private void BindViews()
    {
        BindingHelper.BindText(_statusText, _viewModel, nameof(_viewModel.StatusText), _ => _viewModel.StatusText);
        BindingHelper.BindVisible(_permissionOverlay, _viewModel, nameof(_viewModel.ShowPermissionRequest), _ => _viewModel.ShowPermissionRequest);
        BindingHelper.BindText(_permissionText, _viewModel, nameof(_viewModel.PermissionText), _ => _viewModel.PermissionText);

        _viewModel.Songs.CollectionChanged += (s, e) => { var a = Activity; if (a != null) a.RunOnUiThread(() => _adapter.UpdateSongs(_viewModel.Songs)); };
    }

    private void OnSongClicked(object? sender, CoreModels.Song song)
    {
        var queue = MainApplication.Services.GetRequiredService<PlayQueue>();
        queue.SetSongs(_viewModel.Songs);
        queue.SelectSong(song.Id);
        _ = MainApplication.Services.GetRequiredService<IAudioPlayerService>().PlayAsync(song.FilePath);
        MainApplication.Services.GetRequiredService<INavigationService>().PushFragment("NowPlaying");
    }
}
