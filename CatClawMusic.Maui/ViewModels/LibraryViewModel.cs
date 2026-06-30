using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Maui;

#if ANDROID
using Android.Media;
using Android.Net;
#endif

namespace CatClawMusic.Maui.ViewModels;

public partial class LibraryViewModel : ObservableObject
{
    private readonly MusicDatabase _db;
    private readonly PlayQueue _queue;

    // === Observable Properties ===
    
    [ObservableProperty]
    private ObservableCollection<Song> _songs = new();

    [ObservableProperty]
    private ObservableCollection<Song> _filteredSongs = new();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _searchQuery = "";

    [ObservableProperty]
    private string _statusText = "加载中...";

    [ObservableProperty]
    private string _currentTab = "Local";

    [ObservableProperty]
    private string _localTabColor = "#9B7ED8";

    [ObservableProperty]
    private string _networkTabColor = "#3D3D3D";

    [ObservableProperty]
    private bool _isNetworkTabVisible;

    [ObservableProperty]
    private int _songCount;

    [ObservableProperty]
    private int _albumCount;

    [ObservableProperty]
    private int _artistCount;

    [ObservableProperty]
    private string _sectionTitle = "全部歌曲";

    [ObservableProperty]
    private List<string> _protocolOptions = new() { "HTTP", "HTTPS" };

    [ObservableProperty]
    private int _selectedProtocolIndex;

    // === Commands ===
    
    public IRelayCommand<string> SwitchTabCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IRelayCommand SortCommand { get; }
    public IRelayCommand ClearCommand { get; }

    public event EventHandler? ShowSortDialogRequested;
    public event EventHandler? ClearDataRequested;
    public event Action<Song>? SongPlayRequested;
    
    public LibraryViewModel(MusicDatabase db, PlayQueue queue)
    {
        _db = db;
        _queue = queue;

        // Initialize commands
        SwitchTabCommand = new RelayCommand<string>(SwitchTab);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        SortCommand = new RelayCommand(ShowSortDialog);
        ClearCommand = new RelayCommand(ConfirmClear);
    }

    private void SwitchTab(string? tab)
    {
        if (string.IsNullOrEmpty(tab)) return;

        CurrentTab = tab;
        
        if (tab == "Local")
        {
            LocalTabColor = "#9B7ED8";
            NetworkTabColor = "#3D3D3D";
            IsNetworkTabVisible = false;
            _ = LoadLocalAsync();
        }
        else
        {
            LocalTabColor = "#3D3D3D";
            NetworkTabColor = "#9B7ED8";
            IsNetworkTabVisible = true;
            _ = LoadNetworkAsync();
        }
    }

    public async Task LoadLocalAsync()
    {
        try
        {
            IsLoading = true;
            StatusText = "正在加载本地音乐...";

            var songs = await _db.GetSongsWithDetailsAsync();

            // 批量解析封面（磁盘缓存命中快，首次提取嵌入封面）
            await Task.Run(() => Services.CoverHelper.BatchResolveCovers(songs));

            Songs = new ObservableCollection<Song>(songs);
            FilterSongs();
            UpdateStats();
            StatusText = $"已加载 {Songs.Count} 首歌曲";
        }
        catch (Exception ex)
        {
            StatusText = $"加载失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task LoadNetworkAsync()
    {
        try
        {
            IsLoading = true;
            StatusText = "正在加载网络音乐...";

            var songs = await _db.GetCachedNetworkSongsAsync();

            // 批量解析封面
            await Task.Run(() => Services.CoverHelper.BatchResolveCovers(songs));

            Songs = new ObservableCollection<Song>(songs);
            FilterSongs();
            UpdateStats();
            StatusText = $"已加载 {Songs.Count} 首网络歌曲";
        }
        catch (Exception ex)
        {
            StatusText = $"加载失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task RefreshAsync()
    {
        if (CurrentTab == "Local")
        {
            await LoadLocalAsync();
        }
        else
        {
            await LoadNetworkAsync();
        }
    }

    private void FilterSongs()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            FilteredSongs = new ObservableCollection<Song>(Songs);
        }
        else
        {
            var query = SearchQuery.ToLowerInvariant();
            var filtered = Songs.Where(s =>
                (s.Title?.ToLowerInvariant().Contains(query) == true) ||
                (s.Artist?.ToLowerInvariant().Contains(query) == true) ||
                (s.Album?.ToLowerInvariant().Contains(query) == true)
            ).ToList();
            FilteredSongs = new ObservableCollection<Song>(filtered);
        }
        
        SongCount = FilteredSongs.Count;
        SectionTitle = string.IsNullOrWhiteSpace(SearchQuery) ? "全部歌曲" : $"搜索结果 ({FilteredSongs.Count})";
    }

    private void UpdateStats()
    {
        SongCount = Songs.Count;
        AlbumCount = Songs
            .Select(s => s.Album ?? "未知专辑")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        ArtistCount = Songs
            .Select(s => s.Artist ?? "未知艺术家")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    private void ShowSortDialog()
    {
        ShowSortDialogRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ConfirmClear()
    {
        ClearDataRequested?.Invoke(this, EventArgs.Empty);
    }

    public void ApplySort(string sortBy)
    {
        var songs = FilteredSongs.ToList();
        var sorted = sortBy switch
        {
            "文件名" => songs.OrderBy(s => Path.GetFileNameWithoutExtension(s.FilePath ?? "")).ToList(),
            "入库时间" => songs.OrderByDescending(s => s.DateAdded).ToList(),
            "文件大小" => songs.OrderByDescending(s => s.FileSize).ToList(),
            "文件夹" => songs.OrderBy(s => Path.GetDirectoryName(s.FilePath ?? "")).ToList(),
            "艺术家" => songs.OrderBy(s => s.Artist ?? "").ToList(),
            "标题" => songs.OrderBy(s => s.Title ?? "").ToList(),
            _ => songs
        };

        FilteredSongs = new ObservableCollection<Song>(sorted);
    }

    public async Task ClearSongsAsync()
    {
        try
        {
            if (CurrentTab == "Local")
            {
                await _db.ClearLocalSongsAsync();
            }
            else
            {
                await _db.ClearCachedNetworkSongsAsync();
            }
            
            Songs.Clear();
            FilteredSongs.Clear();
            StatusText = $"{(CurrentTab == "Local" ? "本地音乐库" : "网络音乐库")}已清空";
        }
        catch (Exception ex)
        {
            StatusText = $"清除失败: {ex.Message}";
        }
    }

    public async Task PlaySongAsync(Song? song)
    {
        if (song == null) return;

        try
        {
            _queue.SetSongs([.. Songs]);
            _queue.SelectSong(song.Id);
            
            // Note: Audio playback would be handled by the Page
            StatusText = $"正在播放: {song.Title}";
        }
        catch (Exception ex)
        {
            StatusText = $"播放失败: {ex.Message}";
        }
    }

    partial void OnSearchQueryChanged(string value)
    {
        FilterSongs();
    }

}
