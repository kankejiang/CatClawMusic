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
/// 同时维护歌曲/专辑/艺术家数量统计、Tab 切换状态与网络协议筛选。
/// </summary>
public partial class LibraryViewModel : ObservableObject
{
    private readonly MusicDatabase _db;
    private readonly PlayQueue _queue;

    // === Observable Properties ===

    /// <summary>当前 Tab 下的全部歌曲集合（应用搜索/协议过滤前）</summary>
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

    /// <summary>网络协议选择器是否可见（在 Network Tab 且有多个协议时显示）</summary>
    [ObservableProperty]
    private bool _isNetworkTabVisible;

    /// <summary>是否配置了至少一个网络协议（控制“网络音乐”Tab 是否显示）</summary>
    [ObservableProperty]
    private bool _hasNetworkProtocols;

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

    /// <summary>可选网络协议列表（第一项始终为“全部”，后续为已启用协议名称）</summary>
    [ObservableProperty]
    private List<string> _protocolOptions = new();

    /// <summary>当前选中的协议索引（0 = 全部）</summary>
    [ObservableProperty]
    private int _selectedProtocolIndex;

    /// <summary>缓存已启用的协议类型列表，与 ProtocolOptions 索引对齐（第0位为 null 表示全部）</summary>
    private List<ProtocolType?> _protocolTypes = new();

    /// <summary>缓存所有网络歌曲（未过滤），用于切换协议时快速过滤</summary>
    private List<Song> _allNetworkSongs = new();

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

    public LibraryViewModel(MusicDatabase db, PlayQueue queue)
    {
        _db = db;
        _queue = queue;

        SwitchTabCommand = new RelayCommand<string>(SwitchTab);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        SortCommand = new RelayCommand(ShowSortDialog);
        ClearCommand = new RelayCommand(ConfirmClear);
    }

    /// <summary>
    /// 初始化/刷新网络协议列表。
    /// 从数据库读取已启用的协议，更新 HasNetworkProtocols 和 ProtocolOptions。
    /// 如果当前在 Network Tab 但所有协议都已删除，自动切回 Local Tab。
    /// </summary>
    public async Task RefreshProtocolsAsync()
    {
        var enabled = await _db.GetEnabledProtocolsAsync();

        _protocolTypes = new List<ProtocolType?> { null }; // 第一项：全部
        var options = new List<string> { "全部" };

        if (enabled.Contains(ProtocolType.WebDAV))
        {
            _protocolTypes.Add(ProtocolType.WebDAV);
            options.Add("WebDAV");
        }
        if (enabled.Contains(ProtocolType.SMB))
        {
            _protocolTypes.Add(ProtocolType.SMB);
            options.Add("SMB");
        }
        if (enabled.Contains(ProtocolType.Navidrome))
        {
            _protocolTypes.Add(ProtocolType.Navidrome);
            options.Add("Navidrome");
        }

        ProtocolOptions = options;
        HasNetworkProtocols = options.Count > 1; // 有除"全部"之外的协议

        if (!HasNetworkProtocols && CurrentTab == "Network")
        {
            // 所有协议都已删除，切回本地
            SwitchTab("Local");
        }
        else if (HasNetworkProtocols && SelectedProtocolIndex >= options.Count)
        {
            SelectedProtocolIndex = 0;
        }

        // 如果在 Network Tab，重新应用协议过滤
        if (CurrentTab == "Network")
        {
            ApplyProtocolFilter();
        }
    }

    private void SwitchTab(string? tab)
    {
        if (string.IsNullOrEmpty(tab)) return;

        if (tab == "Network" && !HasNetworkProtocols) return;

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
            IsNetworkTabVisible = ProtocolOptions.Count > 2; // 超过"全部+一个协议"才显示选择器
            _ = LoadNetworkAsync();
        }
    }

    partial void OnSelectedProtocolIndexChanged(int value)
    {
        if (CurrentTab == "Network")
        {
            ApplyProtocolFilter();
        }
    }

    /// <summary>根据当前选中的协议过滤网络歌曲，并叠加搜索过滤</summary>
    private void ApplyProtocolFilter()
    {
        if (_allNetworkSongs.Count == 0)
        {
            Songs = new ObservableCollection<Song>();
            FilterSongs();
            return;
        }

        List<Song> filtered;
        if (SelectedProtocolIndex <= 0 || SelectedProtocolIndex >= _protocolTypes.Count)
        {
            // "全部" 或索引无效：显示所有网络歌曲
            filtered = _allNetworkSongs;
        }
        else
        {
            var protocol = _protocolTypes[SelectedProtocolIndex];
            filtered = _allNetworkSongs.Where(s => s.Protocol == protocol).ToList();
        }

        Songs = new ObservableCollection<Song>(filtered);
        FilterSongs();
    }

    /// <summary>异步加载本地音乐：从数据库读取歌曲并批量解析封面</summary>
    public async Task LoadLocalAsync()
    {
        try
        {
            IsLoading = true;
            StatusText = "正在加载本地音乐...";

            var songs = await _db.GetSongsWithDetailsAsync();

            await Task.Run(() => Services.CoverHelper.BatchResolveCovers(songs));

            _allNetworkSongs = new List<Song>();
            Songs = new ObservableCollection<Song>(songs);
            FilterSongs();
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

    /// <summary>异步加载网络缓存音乐：从数据库读取缓存网络歌曲，按协议过滤后展示</summary>
    public async Task LoadNetworkAsync()
    {
        try
        {
            IsLoading = true;
            StatusText = "正在加载网络音乐...";

            var songs = await _db.GetCachedNetworkSongsAsync();

            await Task.Run(() => Services.CoverHelper.BatchResolveCovers(songs));

            _allNetworkSongs = songs;
            IsNetworkTabVisible = ProtocolOptions.Count > 2;
            ApplyProtocolFilter();
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
        await RefreshProtocolsAsync();
        if (CurrentTab == "Local")
        {
            await LoadLocalAsync();
        }
        else
        {
            await LoadNetworkAsync();
        }
    }

    private CancellationTokenSource? _filterCts;

    partial void OnSearchQueryChanged(string value)
    {
        // 防抖 250ms 避免每次按键触发过滤
        _filterCts?.Cancel();
        _filterCts?.Dispose();
        _filterCts = new CancellationTokenSource();
        _ = FilterSongsAsync(_filterCts.Token);
    }

    /// <summary>过滤入口（fire-and-forget，避免UI线程死锁）</summary>
    private void FilterSongs() => _ = FilterSongsAsync(default);

    private async Task FilterSongsAsync(CancellationToken ct)
    {
        try
        {
            // 仅在搜索框有内容时启用防抖
            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                await Task.Delay(250, ct).ConfigureAwait(false);
            }

            var query = SearchQuery;
            var songs = Songs;

            // 后台线程执行 LINQ 过滤
            var filtered = await Task.Run(() =>
            {
                IEnumerable<Song> source = songs;
                if (!string.IsNullOrWhiteSpace(query))
                {
                    var q = query.ToLowerInvariant();
                    source = source.Where(s =>
                        (s.Title?.ToLowerInvariant().Contains(q) == true) ||
                        (s.Artist?.ToLowerInvariant().Contains(q) == true) ||
                        (s.Album?.ToLowerInvariant().Contains(q) == true)
                    );
                }
                return source.ToList();
            }, ct).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();

            // 回到主线程更新 ObservableCollection
            // 直接替换实例：触发 1 次 PropertyChanged → CollectionView 全量重建一次，
            // 比 Clear+Add (N 次 CollectionChanged → N 次布局刷新) 快得多
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (ct.IsCancellationRequested) return;
                FilteredSongs = new ObservableCollection<Song>(filtered);
                UpdateStats(); // 统一在此处更新歌曲/专辑/艺术家数量，避免竞态条件
                SectionTitle = string.IsNullOrWhiteSpace(SearchQuery)
                    ? "全部歌曲"
                    : $"搜索结果 ({FilteredSongs.Count})";
            });
        }
        catch (OperationCanceledException)
        {
            // 防抖正常行为
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Library] FilterSongs failed: {ex.Message}");
        }
    }

    private void UpdateStats()
    {
        SongCount = FilteredSongs.Count;
        AlbumCount = FilteredSongs
            .Select(s => s.Album ?? "未知专辑")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        ArtistCount = FilteredSongs
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
            _allNetworkSongs.Clear();
            StatusText = $"{(CurrentTab == "Local" ? "本地音乐库" : "网络音乐库")}已清空";
        }
        catch (Exception ex)
        {
            StatusText = $"清除失败: {ex.Message}";
        }
    }

    /// <summary>播放指定歌曲：将其加入播放队列并选中（实际音频播放由页面处理）</summary>
    public async Task PlaySongAsync(Song? song)
    {
        if (song == null) return;

        try
        {
            _queue.SetSongs([.. FilteredSongs]);
            _queue.SelectSong(song.Id);

            StatusText = $"正在播放: {song.Title}";
        }
        catch (Exception ex)
        {
            StatusText = $"播放失败: {ex.Message}";
        }
    }

}
