using System.Collections.ObjectModel;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.WinUI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CatClawMusic.WinUI.ViewModels;

/// <summary>
/// 歌单页（主从布局）：左侧歌单列表，右侧选中歌单的歌曲。
/// 增删改直接落到复用的 <see cref="IMusicLibraryService"/>。
/// </summary>
public sealed partial class PlaylistsViewModel : ObservableObject
{
    /// <summary>系统歌单「收藏」的虚拟 id（仅为 UI 区分，不落库）</summary>
    private const int FavoritePlaylistId = -1;

    /// <summary>系统歌单「最近播放」的虚拟 id</summary>
    private const int RecentPlaylistId = -2;

    private readonly IMusicLibraryService _library;
    private readonly PlaybackService _playback;

    public PlaylistsViewModel(IMusicLibraryService library, PlaybackService playback)
    {
        _library = library;
        _playback = playback;
    }

    public ObservableCollection<PlaylistRow> Playlists { get; } = [];
    public ObservableCollection<SongRow> Songs { get; } = [];

    [ObservableProperty]
    public partial PlaylistRow? SelectedPlaylist { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public string SelectedName => SelectedPlaylist?.Name ?? "歌单";

    public bool HasSelection => SelectedPlaylist is not null;

    public bool HasSongs => Songs.Count > 0;

    public bool CanEditSelection => SelectedPlaylist?.CanEdit == true;

    [RelayCommand]
    public async Task LoadAsync()
    {
        var previousId = SelectedPlaylist?.Playlist.Id;

        var playlists = await _library.GetAllPlaylistsAsync();
        Playlists.Clear();

        // 系统歌单：数据来自 Favorites / PlayHistory，不是 PlaylistSong 表，所以用虚拟 id 合成。
        Playlists.Add(new PlaylistRow(new Playlist
        {
            Id = FavoritePlaylistId,
            Name = "收藏",
            IsSystem = true,
            SongCount = await _library.GetFavoriteSongCountAsync(),
        }));
        Playlists.Add(new PlaylistRow(new Playlist
        {
            Id = RecentPlaylistId,
            Name = "最近播放",
            IsSystem = true,
            SongCount = await _library.GetRecentSongCountAsync(),
        }));

        foreach (var playlist in playlists.Where(p => !p.IsSystem))
            Playlists.Add(new PlaylistRow(playlist));

        var selection = previousId is null
            ? Playlists.FirstOrDefault()
            : Playlists.FirstOrDefault(p => p.Playlist.Id == previousId) ?? Playlists.FirstOrDefault();

        SelectedPlaylist = selection;

        if (selection is null)
            ClearSongs();
    }

    partial void OnSelectedPlaylistChanged(PlaylistRow? value)
    {
        OnPropertyChanged(nameof(SelectedName));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanEditSelection));
        _ = LoadSongsAsync(value);
    }

    /// <summary>新建歌单，返回新 id（0 表示失败）</summary>
    public async Task<int> CreateAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return 0;

        var id = await _library.CreatePlaylistAsync(name.Trim());
        await LoadAsync();
        SelectedPlaylist = Playlists.FirstOrDefault(p => p.Playlist.Id == id) ?? SelectedPlaylist;
        return id;
    }

    public async Task RenameAsync(string name)
    {
        var row = SelectedPlaylist;
        if (row is null || !row.CanEdit || string.IsNullOrWhiteSpace(name))
            return;

        row.Playlist.Name = name.Trim();
        await _library.UpdatePlaylistAsync(row.Playlist);
        await LoadAsync();
    }

    public async Task DeleteAsync()
    {
        var row = SelectedPlaylist;
        if (row is null || !row.CanEdit)
            return;

        await _library.DeletePlaylistAsync(row.Playlist.Id);
        SelectedPlaylist = null;
        await LoadAsync();
    }

    [RelayCommand]
    private Task PlayAllAsync() => _playback.PlayAllAsync(Songs.Select(r => r.Song).ToList());

    [RelayCommand]
    private void PlaySong(SongRow? row)
    {
        if (row is not null)
            _ = _playback.PlayAsync(Songs.Select(r => r.Song).ToList(), row.Song);
    }

    public async Task AddSongAsync(int playlistId, Song song)
    {
        await _library.AddSongToPlaylistAsync(playlistId, song.Id);

        if (SelectedPlaylist?.Playlist.Id == playlistId)
            await LoadSongsAsync(SelectedPlaylist);

        await LoadAsync();
    }

    public async Task RemoveSongAsync(SongRow? row)
    {
        var playlist = SelectedPlaylist;
        if (row is null || playlist is null)
            return;

        await _library.RemoveSongFromPlaylistAsync(playlist.Playlist.Id, row.Song.Id);
        await LoadSongsAsync(playlist);
        await LoadAsync();
    }

    /// <summary>供歌曲右键菜单「添加到歌单」取候选列表</summary>
    public async Task<List<PlaylistRow>> GetEditablePlaylistsAsync()
    {
        var playlists = await _library.GetAllPlaylistsAsync();
        return playlists
            .Where(p => !p.IsSystem)
            .Select(p => new PlaylistRow(p))
            .ToList();
    }

    private async Task LoadSongsAsync(PlaylistRow? row)
    {
        if (row is null)
        {
            ClearSongs();
            return;
        }

        IsBusy = true;
        try
        {
            var songs = row.Playlist.Id switch
            {
                FavoritePlaylistId => await _library.GetFavoriteSongsAsync(),
                RecentPlaylistId => await _library.GetRecentSongsAsync(),
                _ => await _library.GetPlaylistSongsAsync(row.Playlist.Id),
            };

            // 选中项在 await 期间被换掉：丢弃过期结果。
            if (SelectedPlaylist?.Playlist.Id != row.Playlist.Id)
                return;

            Songs.Clear();
            foreach (var song in songs)
                Songs.Add(new SongRow(song));

            OnPropertyChanged(nameof(HasSongs));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ClearSongs()
    {
        Songs.Clear();
        OnPropertyChanged(nameof(HasSongs));
    }
}
