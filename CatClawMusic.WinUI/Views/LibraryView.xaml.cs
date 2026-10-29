using CatClawMusic.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace CatClawMusic.WinUI.Views;

/// <summary>
/// 音乐库页：歌曲 / 歌手 / 专辑 三类视图 + 右键菜单。
/// 数据与命令都在 <see cref="LibraryViewModel"/>，这里只做视图切换与菜单拼装。
/// </summary>
public sealed partial class LibraryView : UserControl
{
    private readonly PlaylistsViewModel _playlists;
    private string _tab = "songs";
    private bool _syncingTab;

    public LibraryView(LibraryViewModel viewModel, PlaylistsViewModel playlists)
    {
        ViewModel = viewModel;
        _playlists = playlists;

        InitializeComponent();

        Loaded += OnLoaded;
        ViewModel.FilterApplied += (_, _) => SelectTab("songs");
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(LibraryViewModel.IsEmpty) or nameof(LibraryViewModel.IsBusy))
                UpdateVisibility();
        };
    }

    public LibraryViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync();
        UpdateVisibility();
    }

    private void OnTabSelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_syncingTab)
            return;

        _tab = sender.SelectedItem?.Tag as string ?? "songs";
        UpdateVisibility();
    }

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        ViewModel.SearchTextValue = sender.Text;
        UpdateVisibility();
    }

    private async void OnPickFolderClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.PickAndScanAsync();
        UpdateVisibility();
    }

    private async void OnRescanClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.RescanAsync();
        UpdateVisibility();
    }

    private void OnClearFilterClick(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearFilterCommand.Execute(null);
        UpdateVisibility();
    }

    private void OnSongClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SongRow row)
            ViewModel.PlaySongCommand.Execute(row);
    }

    private void OnArtistClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ArtistRow row)
            ViewModel.SelectArtistCommand.Execute(row);
    }

    private void OnAlbumClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is AlbumRow row)
            ViewModel.SelectAlbumCommand.Execute(row);
    }

    private async void OnSongRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is not FrameworkElement source || source.DataContext is not SongRow row)
            return;

        var flyout = new MenuFlyout();

        var play = new MenuFlyoutItem { Text = "播放" };
        play.Click += (_, _) => ViewModel.PlaySongCommand.Execute(row);
        flyout.Items.Add(play);

        var playNext = new MenuFlyoutItem { Text = "下一首播放" };
        playNext.Click += (_, _) => ViewModel.PlayNextCommand.Execute(row);
        flyout.Items.Add(playNext);

        var favorite = await ViewModel.IsFavoriteAsync(row);
        var favoriteItem = new MenuFlyoutItem { Text = favorite ? "取消收藏" : "收藏" };
        favoriteItem.Click += async (_, _) => await ViewModel.ToggleFavoriteAsync(row);
        flyout.Items.Add(favoriteItem);

        var addTo = new MenuFlyoutSubItem { Text = "添加到歌单" };
        var playlists = await _playlists.GetEditablePlaylistsAsync();
        if (playlists.Count == 0)
        {
            addTo.Items.Add(new MenuFlyoutItem { Text = "还没有自建歌单", IsEnabled = false });
        }
        else
        {
            foreach (var playlist in playlists)
            {
                var item = new MenuFlyoutItem { Text = playlist.Name };
                var playlistId = playlist.Playlist.Id;
                item.Click += async (_, _) => await _playlists.AddSongAsync(playlistId, row.Song);
                addTo.Items.Add(item);
            }
        }

        flyout.Items.Add(addTo);
        flyout.ShowAt(source, e.GetPosition(source));
    }

    private void SelectTab(string tag)
    {
        var target = LibraryTabs.Items
            .OfType<SelectorBarItem>()
            .FirstOrDefault(item => (item.Tag as string) == tag);

        if (target is null)
            return;

        _syncingTab = true;
        LibraryTabs.SelectedItem = target;
        _syncingTab = false;

        _tab = tag;
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        var empty = ViewModel.IsEmpty;
        var busy = ViewModel.IsBusy;

        BusyRing.IsActive = busy;
        StatusText.Text = ViewModel.StatusText;
        FoldersText.Text = ViewModel.FoldersText;

        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;

        SongsList.Visibility = !empty && _tab == "songs" ? Visibility.Visible : Visibility.Collapsed;
        ArtistsList.Visibility = !empty && _tab == "artists" ? Visibility.Visible : Visibility.Collapsed;
        AlbumsList.Visibility = !empty && _tab == "albums" ? Visibility.Visible : Visibility.Collapsed;

        FilterChip.Visibility = ViewModel.HasFilter ? Visibility.Visible : Visibility.Collapsed;
        FilterChipText.Text = ViewModel.FilterText;
    }
}
