using System.Collections.ObjectModel;
using CatClawMusic.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CatClawMusic.UI.ViewModels;

public partial class PlaylistViewModel : ObservableObject
{
    public ObservableCollection<Playlist> Playlists { get; } = new();

    [ObservableProperty]
    private bool _isEmpty = true;

    public PlaylistViewModel()
    {
        LoadSystemPlaylists();
    }

    private void LoadSystemPlaylists()
    {
        Playlists.Add(new Playlist { Id = 1, Name = "最近播放", SongCount = 0, IsSystem = true });
        Playlists.Add(new Playlist { Id = 2, Name = "收藏歌曲", SongCount = 0, IsSystem = true });
        IsEmpty = Playlists.Count == 0;
    }

    public void Refresh()
    {
        // TODO: 从数据库加载播放列表
    }

    public void CreatePlaylist(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        Playlists.Add(new Playlist { Name = name, CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
        IsEmpty = false;
    }
}
