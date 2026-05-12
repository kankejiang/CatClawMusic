using Android.Graphics;
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
    private RecyclerView _songList = null!;
    private SongAdapter _adapter = null!;
    private TextView _tabAll = null!, _tabFav = null!, _tabRecent = null!;
    private TextView _statusText = null!;
    private TextView _sortTitle = null!, _sortArtist = null!, _sortAlbum = null!;

    private readonly Color _colorActive = Color.ParseColor("#9B7ED8");
    private readonly Color _colorInactive = Color.ParseColor("#2D2438");
    private readonly Color _colorSortActive = Color.ParseColor("#9B7ED8");
    private readonly Color _colorSortInactive = Color.ParseColor("#C0B8CA");

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
    {
        return inflater.Inflate(Resource.Layout.fragment_playlist, container, false)!;
    }

    public override void OnViewCreated(View view, Bundle? state)
    {
        base.OnViewCreated(view, state);
        _viewModel = MainApplication.Services.GetRequiredService<PlaylistViewModel>();
        _songList = view.FindViewById<RecyclerView>(Resource.Id.song_list)!;
        _songList.SetLayoutManager(new LinearLayoutManager(Context));
        _statusText = view.FindViewById<TextView>(Resource.Id.status_text)!;

        _tabAll = view.FindViewById<TextView>(Resource.Id.tab_all)!;
        _tabFav = view.FindViewById<TextView>(Resource.Id.tab_fav)!;
        _tabRecent = view.FindViewById<TextView>(Resource.Id.tab_recent)!;

        _tabAll.Click += (s, e) => _ = _viewModel.LoadAllSongsAsync();
        _tabFav.Click += (s, e) => _ = _viewModel.LoadFavoritesAsync();
        _tabRecent.Click += (s, e) => _ = _viewModel.LoadRecentAsync();

        _sortTitle = view.FindViewById<TextView>(Resource.Id.sort_title)!;
        _sortArtist = view.FindViewById<TextView>(Resource.Id.sort_artist)!;
        _sortAlbum = view.FindViewById<TextView>(Resource.Id.sort_album)!;

        _sortTitle.Click += (s, e) => _viewModel.SetSort("title");
        _sortArtist.Click += (s, e) => _viewModel.SetSort("artist");
        _sortAlbum.Click += (s, e) => _viewModel.SetSort("album");

        _adapter = MainApplication.Services.GetRequiredService<SongAdapter>();
        _adapter.SongClicked += (s, song) => _ = _viewModel.PlaySongAsync(song);
        _songList.SetAdapter(_adapter);

        _viewModel.Songs.CollectionChanged += (s, e) =>
        {
            var a = Activity;
            if (a != null) a.RunOnUiThread(() =>
            {
                _adapter.UpdateSongs(_viewModel.Songs);
                _statusText.Visibility = _viewModel.Songs.Count == 0 ? ViewStates.Visible : ViewStates.Gone;
                _statusText.Text = _viewModel.StatusText;
            });
        };

        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(_viewModel.ActiveTab))
            {
                var a = Activity;
                if (a != null) a.RunOnUiThread(UpdateTabStyles);
            }
            else if (e.PropertyName == nameof(_viewModel.SortKey) || e.PropertyName == nameof(_viewModel.SortDescending))
            {
                var a = Activity;
                if (a != null) a.RunOnUiThread(UpdateSortStyles);
            }
        };

        // 默认加载全部歌曲
        _ = _viewModel.LoadAllSongsAsync();
    }

    private void UpdateTabStyles()
    {
        var active = _viewModel.ActiveTab;
        _tabAll.SetTextColor(active == "all" ? _colorActive : _colorInactive);
        _tabAll.Typeface = active == "all" ? Typeface.DefaultBold : Typeface.Default;
        _tabFav.SetTextColor(active == "fav" ? _colorActive : _colorInactive);
        _tabFav.Typeface = active == "fav" ? Typeface.DefaultBold : Typeface.Default;
        _tabRecent.SetTextColor(active == "recent" ? _colorActive : _colorInactive);
        _tabRecent.Typeface = active == "recent" ? Typeface.DefaultBold : Typeface.Default;
    }

    private void UpdateSortStyles()
    {
        var isDesc = _viewModel.SortDescending;
        var arrow = isDesc ? " ↓" : " ↑";

        _sortTitle.Text = "标题" + (_viewModel.SortKey == "title" ? arrow : "");
        _sortTitle.SetTextColor(_viewModel.SortKey == "title" ? _colorSortActive : _colorSortInactive);
        _sortTitle.Typeface = _viewModel.SortKey == "title" ? Typeface.DefaultBold : Typeface.Default;

        _sortArtist.Text = "艺术家" + (_viewModel.SortKey == "artist" ? arrow : "");
        _sortArtist.SetTextColor(_viewModel.SortKey == "artist" ? _colorSortActive : _colorSortInactive);
        _sortArtist.Typeface = _viewModel.SortKey == "artist" ? Typeface.DefaultBold : Typeface.Default;

        _sortAlbum.Text = "专辑" + (_viewModel.SortKey == "album" ? arrow : "");
        _sortAlbum.SetTextColor(_viewModel.SortKey == "album" ? _colorSortActive : _colorSortInactive);
        _sortAlbum.Typeface = _viewModel.SortKey == "album" ? Typeface.DefaultBold : Typeface.Default;
    }

    public override void OnResume()
    {
        base.OnResume();
        // 回到页面时刷新当前分类
        if (_viewModel.ActiveTab == "all") _ = _viewModel.LoadAllSongsAsync();
        else if (_viewModel.ActiveTab == "fav") _ = _viewModel.LoadFavoritesAsync();
        else _ = _viewModel.LoadRecentAsync();
    }
}
