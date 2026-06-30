using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CatClawMusic.Maui.ViewModels;

public partial class ArtistDetailViewModel : ObservableObject
{
    private readonly MusicDatabase _db;
    private readonly IAudioPlayerService? _audioPlayer;
    private readonly PlayQueue? _playQueue;

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
    private Color _albumsTabColor = Colors.Transparent;

    [ObservableProperty]
    private Color _songsTabColor = Colors.Transparent;

    [ObservableProperty]
    private bool _isLoading = false;

    [ObservableProperty]
    private string _statusText = "";

    public event Action<Song>? SongPlayRequested;

    public ArtistDetailViewModel(MusicDatabase db,
        IAudioPlayerService? audioPlayer = null,
        PlayQueue? playQueue = null)
    {
        _db = db;
        _audioPlayer = audioPlayer;
        _playQueue = playQueue;

        // 默认 Tab 颜色
        AlbumsTabColor = Color.FromArgb("#9B7ED8");
        SongsTabColor = Color.FromArgb("#1E787880");
    }

    /// <summary>加载艺术家详情、专辑和歌曲</summary>
    [RelayCommand]
    public async Task LoadArtistAsync(string artistName)
    {
        IsLoading = true;
        StatusText = "加载中...";
        try
        {
            // 从数据库获取艺术家信息
            var allArtists = await _db.GetAllArtistsAsync();
            var artist = allArtists.FirstOrDefault(a =>
                string.Equals(a.Name, artistName, StringComparison.OrdinalIgnoreCase));
            if (artist != null)
                Artist = artist;
            else
                Artist = new Artist { Name = artistName };

            // 加载该艺术家的歌曲
            var songs = await _db.GetSongsByArtistAsync(artistName);
            Songs.Clear();
            foreach (var s in songs)
                Songs.Add(s);

            // 从歌曲中提取专辑列表（去重）
            var albumDict = new Dictionary<string, Album>();
            foreach (var s in songs)
            {
                if (!string.IsNullOrEmpty(s.Album) && !albumDict.ContainsKey(s.Album))
                {
                    albumDict[s.Album] = new Album
                    {
                        Title = s.Album,
                        Artist = s.Artist,
                    };
                }
            }
            Albums.Clear();
            foreach (var a in albumDict.Values)
                Albums.Add(a);

            StatusText = $"共 {Albums.Count} 张专辑 · {Songs.Count} 首歌曲";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ArtistDetailVM] LoadArtist({artistName}) failed: {ex}");
            StatusText = "加载失败";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public void SwitchTab(string tab)
    {
        IsAlbumsTabVisible = tab == "Albums";
        IsSongsTabVisible = tab == "Songs";
        AlbumsTabColor = IsAlbumsTabVisible ? Color.FromArgb("#9B7ED8") : Color.FromArgb("#1E787880");
        SongsTabColor = IsSongsTabVisible ? Color.FromArgb("#9B7ED8") : Color.FromArgb("#1E787880");
    }

    [RelayCommand]
    public async Task PlayAllAsync()
    {
        if (_audioPlayer == null || _playQueue == null || Songs.Count == 0) return;
        _playQueue.SetSongs(Songs);
        var first = Songs[0];
        _playQueue.SelectSong(first.Id);
        if (!string.IsNullOrEmpty(first.FilePath))
            await _audioPlayer.PlayAsync(first.FilePath);
        _ = RecordPlayAsync(first);
        SongPlayRequested?.Invoke(first);
    }

    [RelayCommand]
    public async Task PlaySongAsync(Song? song)
    {
        if (song == null || _audioPlayer == null || _playQueue == null) return;

        var currentSongInQueue = _playQueue.CurrentSong;
        if (currentSongInQueue != null && currentSongInQueue.Id == song.Id)
        {
            if (_audioPlayer.IsPlaying)
                await _audioPlayer.PauseAsync();
            else
                await _audioPlayer.ResumeAsync();
        }
        else
        {
            _playQueue.SetSongs(Songs);
            _playQueue.SelectSong(song.Id);
            if (!string.IsNullOrEmpty(song.FilePath))
                await _audioPlayer.PlayAsync(song.FilePath);
            _ = RecordPlayAsync(song);
            SongPlayRequested?.Invoke(song);
        }
    }

    private async Task RecordPlayAsync(Song song)
    {
        try { await _db.RecordPlayAsync(song.Id); }
        catch { }
    }
}
