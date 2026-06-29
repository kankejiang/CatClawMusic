using CatClawMusic.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CatClawMusic.Maui.ViewModels;

public partial class ArtistDetailViewModel : ObservableObject
{
    [ObservableProperty]
    private Artist _artist = new();

    [ObservableProperty]
    private ObservableCollection<Album> _albums = new();

    [ObservableProperty]
    private ObservableCollection<Song> _songs = new();

    [ObservableProperty]
    private bool _isAlbumsTabVisible = true;

    [ObservableProperty]
    private bool _isSongsTabVisible = false;

    [ObservableProperty]
    private string _albumsTabColor = "#9B7ED8";

    [ObservableProperty]
    private string _songsTabColor = "#1E787880";

    public IAsyncRelayCommand LoadArtistCommand { get; }
    public IRelayCommand PlayAllCommand { get; }
    public IRelayCommand<string> SwitchTabCommand { get; }
    public IRelayCommand<Song> PlaySongCommand { get; }

    public event Action<Song>? SongPlayRequested;

    public ArtistDetailViewModel()
    {
        LoadArtistCommand = new AsyncRelayCommand(LoadArtistAsync);
        PlayAllCommand = new RelayCommand(PlayAll);
        SwitchTabCommand = new RelayCommand<string>(SwitchTab);
        PlaySongCommand = new RelayCommand<Song>(PlaySong);
    }

    private async Task LoadArtistAsync()
    {
        // Load artist details, albums, and songs
        await Task.CompletedTask;
    }

    private void PlayAll()
    {
        // Play all songs by this artist
    }

    private void SwitchTab(string tab)
    {
        IsAlbumsTabVisible = tab == "Albums";
        IsSongsTabVisible = tab == "Songs";
        AlbumsTabColor = IsAlbumsTabVisible ? "#9B7ED8" : "#1E787880";
        SongsTabColor = IsSongsTabVisible ? "#9B7ED8" : "#1E787880";
    }

    private void PlaySong(Song? song)
    {
        if (song != null)
        {
            SongPlayRequested?.Invoke(song);
        }
    }
}
