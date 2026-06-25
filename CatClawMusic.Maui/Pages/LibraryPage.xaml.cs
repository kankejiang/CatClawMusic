using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;

namespace CatClawMusic.Maui.Pages;

public partial class LibraryPage : ContentPage
{
    private readonly MusicDatabase _db;
    private readonly PlayQueue _queue;
    private readonly ViewModels.LibraryViewModel _vm;

    public LibraryPage(MusicDatabase db, PlayQueue queue, ViewModels.LibraryViewModel vm)
    {
        InitializeComponent();
        _db = db;
        _queue = queue;
        _vm = vm;
        BindingContext = _vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
        SongList.ItemsSource = _vm.Songs;
        StatusLabel.Text = $"{_vm.Songs.Count} 首歌曲";
    }

    private async void OnSongSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is Song song)
        {
            if (_queue.CurrentSong?.Id == song.Id) return;
            
            // Add all songs to queue and play selected
            _queue.SetSongs(_vm.Songs);
            _queue.SelectSong(song.Id);
            
            await Shell.Current.GoToAsync("//nowplaying");
        }
    }
}
