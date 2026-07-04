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

/// <summary>
/// 音乐库页 ViewModel：管理本地与网络缓存歌曲的加载、搜索过滤、排序、清空与播放等交互，
/// 同时维护歌曲/专辑/艺术家数量统计与 Tab 切换状态。
/// </summary>
public partial class LibraryViewModel : ObservableObject
{
    private readonly MusicDatabase _db;
    private readonly PlayQueue _queue;

    // === Observable Properties ===

    /// <summary>当前 Tab 下的全部歌曲集合（应用搜索过滤前）</summary>
    [ObservableProperty]
    private ObservableCollection<Song> _songs = new();

    /// <summary>经过搜索过滤后的歌曲集合（绑定到列表 UI）</summary>
    [ObservableProperty]
    private ObservableCollection<Song> _filteredSongs = new();

    /// <summary>是否正在加载歌曲</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>搜索关键字</summary>
    [ObservableProperty]
    private string _searchQuery = "";

    /// <summary>状态文本（用于向用户展示加载进度或结果）</summary>
    [ObservableProperty]
    private string _statusText = "加载中...";

    /// <summary>当前 Tab 名称（"Local" 或 "Network"）</summary>
    [ObservableProperty]
    private string _currentTab = "Local";

    /// <summary>“本地”Tab 的颜色</summary>
    [ObservableProperty]
    private string _localTabColor = "#9B7ED8";

    /// <summary>“网络”Tab 的颜色</summary>
    [ObservableProperty]
    private string _networkTabColor = "#3D3D3D";

    /// <summary>“网络”Tab 是否处于可见/选中状态</summary>
    [ObservableProperty]
    private bool _isNetworkTabVisible;

    /// <summary>当前 Tab 下的歌曲数量</summary>
    [ObservableProperty]
    private int _songCount;

    /// <summary>当前 Tab 下的专辑数量</summary>
    [ObservableProperty]
    private int _albumCount;

    /// <summary>当前 Tab 下的艺术家数量</summary>
    [ObservableProperty]
    private int _artistCount;

    /// <summary>分区标题（如“全部歌曲”或“搜索结果 (N)”）</summary>
    [ObservableProperty]
    private string _sectionTitle = "全部歌曲";

    /// <summary>可选协议列表（用于网络音乐配置展示）</summary>
    [ObservableProperty]
    private List<string> _protocolOptions = new() { "HTTP", "HTTPS" };

    /// <summary>当前选中的协议索引</summary>
    [ObservableProperty]
    private int _selectedProtocolIndex;

    // === Commands ===

    /// <summary>切换 Tab 命令（参数为 "Local" 或 "Network"）</summary>
    public IRelayCommand<string> SwitchTabCommand { get; }
    /// <summary>刷新当前 Tab 数据命令</summary>
    public IAsyncRelayCommand RefreshCommand { get; }
    /// <summary>弹出排序对话框命令</summary>
    public IRelayCommand SortCommand { get; }
    /// <summary>清空当前 Tab 数据命令（弹出确认）</summary>
    public IRelayCommand ClearCommand { get; }

    /// <summary>请求弹出排序对话框时触发，供页面订阅</summary>
    public event EventHandler? ShowSortDialogRequested;
    /// <summary>请求清空数据时触发，供页面订阅以弹窗确认</summary>
    public event EventHandler? ClearDataRequested;
    /// <summary>请求播放某首歌曲时触发，供外部页面订阅以同步 UI 状态</summary>
    public event Action<Song>? SongPlayRequested;

    /// <summary>
    /// 初始化 <see cref="LibraryViewModel"/> 实例，并创建各交互命令。
    /// </summary>
    /// <param name="db">音乐数据库访问对象</param>
    /// <param name="queue">播放队列</param>
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

    /// <summary>异步加载本地音乐：从数据库读取歌曲并批量解析封面</summary>
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

    /// <summary>异步加载网络缓存音乐：从数据库读取缓存网络歌曲并批量解析封面</summary>
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

    /// <summary>对当前过滤后的歌曲按指定方式排序</summary>
    /// <param name="sortBy">排序方式（文件名/入库时间/文件大小/文件夹/艺术家/标题）</param>
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

    /// <summary>清空当前 Tab 的歌曲数据（本地或网络缓存）</summary>
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

    /// <summary>播放指定歌曲：将其加入播放队列并选中（实际音频播放由页面处理）</summary>
    /// <param name="song">要播放的歌曲，为空则忽略</param>
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
