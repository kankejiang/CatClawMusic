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

    private readonly Color _colorActive = Color.ParseColor("#9B7ED8");
    private readonly Color _colorInactive = Color.ParseColor("#2D2438");

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

    public override void OnResume()
    {
        base.OnResume();
        // 回到页面时刷新当前分类
        if (_viewModel.ActiveTab == "all") _ = _viewModel.LoadAllSongsAsync();
        else if (_viewModel.ActiveTab == "fav") _ = _viewModel.LoadFavoritesAsync();
        else _ = _viewModel.LoadRecentAsync();
    }
}
