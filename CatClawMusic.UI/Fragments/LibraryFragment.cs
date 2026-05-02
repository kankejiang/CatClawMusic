using System.Collections.Specialized;
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
    private ImageButton _btnRefresh = null!;
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
        _btnRefresh = view.FindViewById<ImageButton>(Resource.Id.btn_refresh)!;

        _adapter = sp.GetRequiredService<SongAdapter>();
        _adapter.SongClicked += OnSongClicked;
        _songList.SetAdapter(_adapter);

        _btnLocal.Click += (s, e) => _viewModel.SwitchTabCommand.Execute("Local");
        _btnNetwork.Click += (s, e) => _viewModel.SwitchTabCommand.Execute("Network");
        _btnRefresh.Click += (s, e) => _viewModel.RefreshCommand.Execute(null);

        BindViews();
        _ = _viewModel.LoadLocalAsync();
        if (_viewModel.Songs.Count > 0)
            _adapter.UpdateSongs(_viewModel.Songs);
    }

    private void BindViews()
    {
        BindingHelper.BindText(_statusText, _viewModel, nameof(_viewModel.StatusText), _ => _viewModel.StatusText);

        // 增量化 CollectionChanged 处理：Add 用 AddRange，Reset/Remove 用全量刷新
        _viewModel.Songs.CollectionChanged += OnSongsCollectionChanged;

        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(_viewModel.LocalTabColor))
                UpdateTabButtonColor(_btnLocal, _viewModel.LocalTabColor, _viewModel.CurrentTab == "Local");
            else if (e.PropertyName == nameof(_viewModel.NetworkTabColor))
                UpdateTabButtonColor(_btnNetwork, _viewModel.NetworkTabColor, _viewModel.CurrentTab == "Network");
        };
        UpdateTabButtonColor(_btnLocal, _viewModel.LocalTabColor, true);
        UpdateTabButtonColor(_btnNetwork, _viewModel.NetworkTabColor, false);
    }

    private void OnSongsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var a = Activity;
        if (a == null) return;

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (e.NewItems != null && e.NewItems.Count > 0)
                {
                    var newSongs = e.NewItems.Cast<CoreModels.Song>().ToList();
                    a.RunOnUiThread(() => _adapter.AddRange(newSongs));
                }
                break;

            case NotifyCollectionChangedAction.Reset:
                a.RunOnUiThread(() => _adapter.UpdateSongs(_viewModel.Songs));
                break;

            case NotifyCollectionChangedAction.Remove:
                // 简单处理：删除后全量刷新
                a.RunOnUiThread(() => _adapter.UpdateSongs(_viewModel.Songs));
                break;

            default:
                a.RunOnUiThread(() => _adapter.UpdateSongs(_viewModel.Songs));
                break;
        }
    }

    private static void UpdateTabButtonColor(Button btn, string hexColor, bool isActive)
    {
        var color = Android.Graphics.Color.ParseColor(hexColor);
        btn.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(color);
        btn.SetTextColor(isActive
            ? Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.White)
            : Android.Content.Res.ColorStateList.ValueOf(
                Android.Graphics.Color.ParseColor("#4A0072"))); // primaryDark
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
