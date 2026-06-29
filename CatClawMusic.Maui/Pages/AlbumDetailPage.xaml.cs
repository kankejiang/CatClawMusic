using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Maui.Controls;

namespace CatClawMusic.Maui.Pages;

public partial class AlbumDetailPage : ContentPage
{
    private readonly MusicDatabase _db;
    private readonly PlayQueue _queue;
    private readonly AlbumDetailViewModel _vm;
    private readonly IAudioPlayerService? _audioPlayer;

    public AlbumDetailPage(MusicDatabase db, PlayQueue queue, AlbumDetailViewModel vm, IServiceProvider sp)
    {
        InitializeComponent();
        _db = db;
        _queue = queue;
        _vm = vm;
        _audioPlayer = sp.GetService<IAudioPlayerService>();
        BindingContext = _vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        
        // Get album from navigation parameters
        if (BindingContext is AlbumDetailViewModel viewModel)
        {
            viewModel.LoadAsync();
        }
    }

    private async void OnSongSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is Song song)
        {
            // Clear selection
            var collectionView = sender as CollectionView;
            if (collectionView != null)
            {
                collectionView.SelectedItem = null;
            }
            
            await PlaySongAsync(song);
        }
    }

    private async Task PlaySongAsync(Song song)
    {
        try
        {
            // Add all songs to queue and play selected
            _queue.SetSongs([.. _vm.Songs]);
            _queue.SelectSong(song.Id);

            if (_audioPlayer != null && !string.IsNullOrEmpty(song.FilePath))
            {
                await _audioPlayer.PlayAsync(song.FilePath);
            }

            // Navigate to now playing page
            await Shell.Current.GoToAsync("//nowplaying");
        }
        catch (Exception ex)
        {
            await DisplayAlert("播放失败", ex.Message, "确定");
        }
    }
}
