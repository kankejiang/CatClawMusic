using System.Collections.ObjectModel;
using CatClawMusic.Core.Models;

namespace CatClawMusic.UI.ViewModels;

public class PlaylistViewModel : BindableObject
{
    public ObservableCollection<Playlist> Playlists { get; } = new();

    private bool _isEmpty = true;
    public bool IsEmpty
    {
        get => _isEmpty;
        set
        {
            _isEmpty = value;
            OnPropertyChanged();
        }
    }

    public PlaylistViewModel()
    {
        LoadSystemPlaylists();
    }

    private void LoadSystemPlaylists()
    {
        Playlists.Add(new Playlist
        {
            Id = 1,
            Name = "最近播放",
            SongCount = 0,
            IsSystem = true
        });
        Playlists.Add(new Playlist
        {
            Id = 2,
            Name = "收藏歌曲",
            SongCount = 0,
            IsSystem = true
        });

        IsEmpty = Playlists.Count == 0;
    }

    public void Refresh()
    {
        // TODO: 从数据库加载播放列表
    }

    public void CreatePlaylist(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        var playlist = new Playlist
        {
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        Playlists.Add(playlist);
        IsEmpty = false;
    }
}
