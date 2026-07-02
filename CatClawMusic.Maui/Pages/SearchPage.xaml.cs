using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Maui.Controls;

namespace CatClawMusic.Maui.Pages;

public partial class SearchPage : ContentPage
{
    private readonly SearchViewModel _vm;
    private readonly PlayQueue _queue;
    private readonly MusicDatabase _db;
    private readonly IAudioPlayerService _audioPlayer;

    public SearchPage(MusicDatabase db, PlayQueue queue, SearchViewModel vm, IAudioPlayerService audioPlayer)
    {
        InitializeComponent();
        _db = db;
        _queue = queue;
        _vm = vm;
        _audioPlayer = audioPlayer;
        BindingContext = _vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        try
        {
            await _vm.LoadExploreDataAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SearchPage OnAppearing error: {ex.Message}");
        }
    }

    private void OnSearchCompleted(object? sender, EventArgs e)
    {
        var entry = sender as Entry;
        _vm.SearchQuery = entry?.Text?.Trim() ?? "";
        entry?.Unfocus();
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        _vm.SearchQuery = e.NewTextValue ?? "";
    }

    private void OnClearSearchClicked(object? sender, EventArgs e)
    {
        SearchBox.Text = "";
        _vm.ClearSearchDropdown();
    }

    private async void OnSearchResultSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not Song song) return;
        if (sender is CollectionView cv) cv.SelectedItem = null;

        SearchBox.Text = "";
        _vm.ClearSearchDropdown();

        var allSongs = _vm.DailyRecommendSongs
            .Concat(_vm.TopPlayedSongs)
            .Concat(_vm.RecentAddedSongs)
            .ToList();
        await PlaySongAsync(song, allSongs);
    }

    private async void OnSearchArtistSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not SearchArtistItem artist) return;
        if (sender is CollectionView cv) cv.SelectedItem = null;

        SearchBox.Text = "";
        _vm.ClearSearchDropdown();

        await Shell.Current.GoToAsync($"artistdetail?artistName={Uri.EscapeDataString(artist.Name)}");
    }

    private async void OnSearchAlbumSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not SearchAlbumItem album) return;
        if (sender is CollectionView cv) cv.SelectedItem = null;

        SearchBox.Text = "";
        _vm.ClearSearchDropdown();

        await Shell.Current.GoToAsync($"albumdetail?title={Uri.EscapeDataString(album.Title)}");
    }

    private void OnYukiAvatarClicked(object? sender, EventArgs e)
    {
        _vm.EnterChatModeCommand.Execute(null);
    }

    private void OnChatBackClicked(object? sender, EventArgs e)
    {
        _vm.ExitChatModeCommand.Execute(null);
    }

    private void OnChatInputCompleted(object? sender, EventArgs e)
    {
        _ = _vm.SendMessageCommand.ExecuteAsync(null);
    }

    private void OnSendClicked(object? sender, EventArgs e)
    {
        _ = _vm.SendMessageCommand.ExecuteAsync(null);
    }

    private async void OnQuickDailyTapped(object? sender, TappedEventArgs e)
    {
        await DiscoverScroll.ScrollToAsync(DailySectionAnchor, ScrollToPosition.Start, true);
    }

    private async void OnQuickTopPlayedTapped(object? sender, TappedEventArgs e)
    {
        await DiscoverScroll.ScrollToAsync(TopPlayedSection, ScrollToPosition.Start, true);
    }

    private async void OnQuickRecentTapped(object? sender, TappedEventArgs e)
    {
        await DiscoverScroll.ScrollToAsync(RecentSection, ScrollToPosition.Start, true);
    }

    private async void OnGoLibraryClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//library");
    }

    private async void OnArtistSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not SearchArtistItem artist)
        {
            return;
        }

        if (sender is CollectionView collectionView)
        {
            collectionView.SelectedItem = null;
        }

        await Shell.Current.GoToAsync($"artistdetail?artistName={Uri.EscapeDataString(artist.Name)}");
    }

    private async void OnAlbumSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not SearchAlbumItem album)
        {
            return;
        }

        if (sender is CollectionView collectionView)
        {
            collectionView.SelectedItem = null;
        }

        await Shell.Current.GoToAsync($"albumdetail?title={Uri.EscapeDataString(album.Title)}");
    }

    private async void OnSongCardTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Border border || border.BindingContext is not Song song)
        {
            return;
        }

        IReadOnlyList<Song> songs = border.ClassId switch
        {
            "Daily" => _vm.DailyRecommendSongs.ToList(),
            "TopPlayed" => _vm.TopPlayedSongs.ToList(),
            "Recent" => _vm.RecentAddedSongs.ToList(),
            _ => new List<Song>()
        };

        await PlaySongAsync(song, songs);
    }

    private async void OnHeroCardTapped(object? sender, TappedEventArgs e)
    {
        var featured = _vm.FeaturedSong;
        if (featured == null) return;
        await PlaySongAsync(featured, _vm.DailyRecommendSongs.ToList());
    }

    private async void OnDailySongSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not Song song) return;
        if (sender is CollectionView cv) cv.SelectedItem = null;
        await PlaySongAsync(song, _vm.DailyRecommendSongs.ToList());
    }

    private async void OnTopPlayedSongSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not Song song) return;
        if (sender is CollectionView cv) cv.SelectedItem = null;
        await PlaySongAsync(song, _vm.TopPlayedSongs.ToList());
    }

    private async void OnRecentSongSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not Song song) return;
        if (sender is CollectionView cv) cv.SelectedItem = null;
        await PlaySongAsync(song, _vm.RecentAddedSongs.ToList());
    }

    private async Task PlaySongAsync(Song song, IReadOnlyList<Song> songs)
    {
        try
        {
            if (songs.Count > 0)
            {
                _queue.SetSongs(songs);
            }

            _queue.SelectSong(song.Id);
            if (!string.IsNullOrWhiteSpace(song.FilePath))
            {
                await _audioPlayer.PlayAsync(song.FilePath);
            }

            // 不再跳转播放页，迷你播放器会自动弹出
        }
        catch (Exception ex)
        {
            await DisplayAlert("播放失败", ex.Message, "确定");
        }
    }
}
