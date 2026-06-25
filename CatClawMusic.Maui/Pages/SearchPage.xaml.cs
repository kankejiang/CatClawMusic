using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;

namespace CatClawMusic.Maui.Pages;

public partial class SearchPage : ContentPage
{
    private readonly MusicDatabase _db;
    private readonly PlayQueue _queue;
    private readonly MultiSourceSearchService? _search;

    public SearchPage(MusicDatabase db, PlayQueue queue, IServiceProvider sp)
    {
        InitializeComponent();
        _db = db;
        _queue = queue;
        _search = sp.GetService<MultiSourceSearchService>();
    }

    private async void OnSearch(object? sender, EventArgs e)
    {
        var query = SearchBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(query)) return;

        var results = await _db.SearchSongsAsync(query);
        ResultsList.ItemsSource = results;
    }

    private async void OnResultSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is Song song)
        {
            var songs = await _db.GetSongsWithDetailsAsync();
            _queue.SetSongs(songs);
            _queue.SelectSong(song.Id);
            await Shell.Current.GoToAsync("//nowplaying");
        }
    }
}
