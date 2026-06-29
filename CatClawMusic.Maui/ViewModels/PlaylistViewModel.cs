using CatClawMusic.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CatClawMusic.Maui.ViewModels;

public partial class PlaylistViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<Playlist> _playlists = new();

    public IAsyncRelayCommand LoadPlaylistsCommand { get; }
    public IRelayCommand<Playlist> OpenPlaylistCommand { get; }
    public IRelayCommand CreatePlaylistCommand { get; }

    public PlaylistViewModel()
    {
        LoadPlaylistsCommand = new AsyncRelayCommand(LoadPlaylistsAsync);
        OpenPlaylistCommand = new RelayCommand<Playlist>(OpenPlaylist);
        CreatePlaylistCommand = new RelayCommand(CreatePlaylist);
    }

    private async Task LoadPlaylistsAsync()
    {
        // Load user's playlists
        await Task.CompletedTask;
    }

    private void OpenPlaylist(Playlist? playlist)
    {
        // Navigate to playlist detail page
    }

    private void CreatePlaylist()
    {
        // Show create playlist dialog
    }
}
