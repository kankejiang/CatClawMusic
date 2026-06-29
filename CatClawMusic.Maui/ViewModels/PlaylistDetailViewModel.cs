using CatClawMusic.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CatClawMusic.Maui.ViewModels;

public partial class PlaylistDetailViewModel : ObservableObject
{
    [ObservableProperty]
    private Playlist _playlist = new();

    [ObservableProperty]
    private ObservableCollection<Song> _songs = new();

    [ObservableProperty]
    private bool _isLoading = false;

    public IAsyncRelayCommand LoadPlaylistCommand { get; }
    public IRelayCommand PlayAllCommand { get; }
    public IRelayCommand EditCommand { get; }
    public IRelayCommand<Song> PlaySongCommand { get; }
    public IRelayCommand<Song> RemoveSongCommand { get; }

    public event Action<Song>? SongPlayRequested;

    public PlaylistDetailViewModel()
    {
        LoadPlaylistCommand = new AsyncRelayCommand(LoadPlaylistAsync);
        PlayAllCommand = new RelayCommand(PlayAll);
        EditCommand = new RelayCommand(EditPlaylist);
        PlaySongCommand = new RelayCommand<Song>(PlaySong);
        RemoveSongCommand = new RelayCommand<Song>(RemoveSong);
    }

    private async Task LoadPlaylistAsync()
    {
        IsLoading = true;
        try
        {
            // Load playlist details and songs
            await Task.CompletedTask;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void PlayAll()
    {
        // Play all songs in playlist
    }

    private void EditPlaylist()
    {
        // Show edit playlist dialog
    }

    private void PlaySong(Song? song)
    {
        if (song != null)
        {
            SongPlayRequested?.Invoke(song);
        }
    }

    private void RemoveSong(Song? song)
    {
        // Remove song from playlist
    }
}
