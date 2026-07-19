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
using Android.OS;
#endif

namespace CatClawMusic.Maui.ViewModels;

public partial class LibraryViewModel : ObservableObject
{
    private readonly MusicDatabase _db;
    private readonly PlayQueue _queue;
    private readonly ExploreDataService? _exploreDataService;

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
    private string _localTabColor = "#8C7BFF";

    [ObservableProperty]
    private string _networkTabColor = "#3D3D3D";

    [ObservableProperty]
    private bool _isNetworkTabVisible;

    [ObservableProperty]
    private bool _hasNetworkProtocols;

    [ObservableProperty]
    private int _songCount;

    [ObservableProperty]
    private int _albumCount;

    [ObservableProperty]
    private int _artistCount;

    [ObservableProperty]
    private string _sectionTitle = "全部歌曲";

    [ObservableProperty]
    private List<string> _protocolOptions = new();

    [ObservableProperty]
    private int _selectedProtocolIndex;

    private List<ProtocolType?> _protocolTypes = new();
    private List<Song> _allNetworkSongs = new();

    [ObservableProperty]
    private string _discoverSource = "auto";

    [ObservableProperty]
    private bool _hasLocalMusic = true;

    public string DiscoverSourceDisplayText => DiscoverSource switch
    {
        "auto" => "自动",
        "local" => "本地",
        "network" => "网络",
        "all" => "混合",
        _ => "自动"
    };

    public IRelayCommand<string> SwitchTabCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IRelayCommand SortCommand { get; }
    public IRelayCommand ClearCommand { get; }

    public event EventHandler? ShowSortDialogRequested;
    public event EventHandler? ClearDataRequested;
    public event Action<Song>? SongPlayRequested;
    public event Action? DiscoverSourceChanged;

    public LibraryViewModel(MusicDatabase db, PlayQueue queue, ExploreDataService? exploreDataService = null)
    {
        _db = db;
        _queue = queue;
        _exploreDataService = exploreDataService;

        DiscoverSource = Preferences.Default.Get("discover_source", "auto");
        _exploreDataService?.SetSourceFilter(GetEffectiveDiscoverSource());

        SwitchTabCommand = new RelayCommand<string>(SwitchTab);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        SortCommand = new RelayCommand(ShowSortDialog);
        ClearCommand = new RelayCommand(ConfirmClear);
    }

    public string GetEffectiveDiscoverSource()
    {
        if (DiscoverSource != "auto") return DiscoverSource;
        if (HasNetworkProtocols && !HasLocalMusic) return "network";
        return "local";
    }

    partial void OnDiscoverSourceChanged(string value)
    {
        OnPropertyChanged(nameof(DiscoverSourceDisplayText));
        _exploreDataService?.SetSourceFilter(GetEffectiveDiscoverSource());
    }

    partial void OnHasLocalMusicChanged(bool value)
    {
        if (DiscoverSource == "auto")
        {
            _exploreDataService?.SetSourceFilter(GetEffectiveDiscoverSource());
            DiscoverSourceChanged?.Invoke();
        }
    }

    public void SetDiscoverSource(string? source)
    {
        if (string.IsNullOrEmpty(source) || source == DiscoverSource) return;
        DiscoverSource = source;
        Preferences.Default.Set("discover_source", source);
        DiscoverSourceChanged?.Invoke();
    }

    public async Task RefreshProtocolsAsync()
    {
        var enabled = await _db.GetEnabledProtocolsAsync();

        _protocolTypes = new List<ProtocolType?> { null };
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
        var oldHasNetwork = HasNetworkProtocols;
        HasNetworkProtocols = options.Count > 1;

        if (!HasNetworkProtocols && CurrentTab == "Network")
        {
            SwitchTab("Local");
        }
        else if (HasNetworkProtocols && SelectedProtocolIndex >= options.Count)
        {
            SelectedProtocolIndex = 0;
        }

        try
        {
            var localCount = await _db.GetLocalSongCountAsync();
            HasLocalMusic = localCount > 0;
        }
        catch { }

        if (DiscoverSource == "auto" && (oldHasNetwork != HasNetworkProtocols))
        {
            _exploreDataService?.SetSourceFilter(GetEffectiveDiscoverSource());
            DiscoverSourceChanged?.Invoke();
        }

        if (CurrentTab == "Network")
        {
            ApplyProtocolFilter();
        }
    }

    public void SwitchTab(string? tab)
    {
        if (string.IsNullOrEmpty(tab)) return;
        if (tab == "Network" && !HasNetworkProtocols) return;

        CurrentTab = tab;

        if (tab == "Local")
        {
            LocalTabColor = "#8C7BFF";
            NetworkTabColor = "#3D3D3D";
            IsNetworkTabVisible = false;
            _ = LoadLocalAsync();
        }
        else
        {
            LocalTabColor = "#3D3D3D";
            NetworkTabColor = "#8C7BFF";
            IsNetworkTabVisible = ProtocolOptions.Count > 2;
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

    public async Task LoadLocalAsync()
    {
        try
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                IsLoading = true;
                StatusText = "正在加载本地音乐...";
            });

            var songs = await _db.GetSongsWithDetailsAsync();

            // 先渲染列表（占位封面），封面在后台分块解析，不再阻塞首屏显示
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                _allNetworkSongs = new List<Song>();
                Songs = new ObservableCollection<Song>(songs);
                FilterSongs();
                StatusText = $"已加载 {Songs.Count} 首歌曲";
                IsLoading = false;
            });

            // 后台分块解析封面（不阻塞 UI；封面就绪后通过 Song.CoverArtPath 的 INPC 自动刷新单元格）
            _ = Task.Run(async () => await Services.CoverHelper.BatchResolveCoversAsync(songs));
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                StatusText = $"加载失败: {ex.Message}";
                IsLoading = false;
            });
        }
    }

    public async Task LoadNetworkAsync()
    {
        try
        {
            IsLoading = true;
            StatusText = "正在加载网络音乐...";

            var songs = await _db.GetCachedNetworkSongsAsync();

            _allNetworkSongs = songs;
            IsNetworkTabVisible = ProtocolOptions.Count > 2;
            ApplyProtocolFilter();
            StatusText = $"已加载 {Songs.Count} 首网络歌曲";

            // 封面在后台分块解析，不阻塞列表显示
            _ = Task.Run(async () => await Services.CoverHelper.BatchResolveCoversAsync(songs));
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
            await LoadLocalAsync();
        else
            await LoadNetworkAsync();
    }

    private CancellationTokenSource? _filterCts;

    partial void OnSearchQueryChanged(string value)
    {
        _filterCts?.Cancel();
        _filterCts?.Dispose();
        _filterCts = new CancellationTokenSource();
        _ = FilterSongsAsync(_filterCts.Token);
    }

    private void FilterSongs() => _ = FilterSongsAsync(default);

    private async Task FilterSongsAsync(CancellationToken ct)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                await Task.Delay(250, ct).ConfigureAwait(false);
            }

            var query = SearchQuery;
            var songs = Songs;

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

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (ct.IsCancellationRequested) return;
                FilteredSongs = new ObservableCollection<Song>(filtered);
                UpdateStats();
                SectionTitle = string.IsNullOrWhiteSpace(SearchQuery)
                    ? "全部歌曲"
                    : $"搜索结果 ({FilteredSongs.Count})";
            });
        }
        catch (System.OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Debug("LibraryViewModel", $"[Library] FilterSongs failed: {ex.Message}");
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
                await _db.ClearLocalSongsAsync();
            else
                await _db.ClearCachedNetworkSongsAsync();

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

    // ═══════════════════════════════════════════════════════
    // 音乐库总览属性
    // ═══════════════════════════════════════════════════════

    [ObservableProperty]
    private int _totalSongCount;

    [ObservableProperty]
    private int _totalArtistCount;

    [ObservableProperty]
    private int _totalAlbumCount;

    [ObservableProperty]
    private double _totalHours;

    [ObservableProperty]
    private int _libraryCount;

    [ObservableProperty]
    private string _totalMusicSizeText = "0 GB";

    [ObservableProperty]
    private string _freeSpaceText = "0 GB";

    [ObservableProperty]
    private int _folderCount;

    [ObservableProperty]
    private string _totalSizeText = "0 GB";

    [ObservableProperty]
    private string _lastScanText = "尚未扫描";

    [ObservableProperty]
    private bool _isSynced = true;

    [ObservableProperty]
    private bool _autoScanEnabled = true;

    [ObservableProperty]
    private string _excludeFoldersText = "Android/、录音、*.tmp";

    [ObservableProperty]
    private int _scanProgress;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _scanStatusText = "";

    [ObservableProperty]
    private int _scannedCount;

    [ObservableProperty]
    private int _scanTotalCount;

    public string ScanCountText => ScanTotalCount > 0 ? $"{ScannedCount} / {ScanTotalCount}" : "";

    [ObservableProperty]
    private ObservableCollection<LibraryCardItem> _libraryCards = new();

    [ObservableProperty]
    private ObservableCollection<FormatSizeItem> _formatSizeItems = new();

    [ObservableProperty]
    private ObservableCollection<RecentAddItem> _recentAddItems = new();

    /// <summary>数据洞察：环形图数据集（音频格式 / 音频音质 / 艺术家歌曲）。</summary>
    [ObservableProperty]
    private ObservableCollection<PieDataset> _pieDatasets = new();

    [ObservableProperty]
    private int _localSongCount;

    [ObservableProperty]
    private int _networkSongCount;

    [ObservableProperty]
    private int _favoriteCount;

    [ObservableProperty]
    private int _recentPlayCount;

    [ObservableProperty]
    private int _trashCount;

    [RelayCommand]
    public void ToggleAutoScan()
    {
        AutoScanEnabled = !AutoScanEnabled;
        Preferences.Default.Set("auto_scan", AutoScanEnabled);
    }

    partial void OnScannedCountChanged(int value)
    {
        OnPropertyChanged(nameof(ScanCountText));
    }

    partial void OnScanTotalCountChanged(int value)
    {
        OnPropertyChanged(nameof(ScanCountText));
    }

    public async Task LoadOverviewDataAsync()
    {
        try
        {
            // 阶段1：全部聚合移到后台线程执行，避免主线程做大量 LINQ/字符串统计（GetTopFolder 等）
            // 导致进入音乐库时主线程被拖垮、触发 ANR（日志：seq=4887 阻塞约 11s）。
            var (localSongCount, networkSongCount, favoriteCount, recentPlayCount,
                 totalSongCount, totalAlbumCount, totalArtistCount, totalHours,
                 totalMusicSizeText, folderCount, freeSpaceText,
                 formatGroups, recent, libraryCards, pieDatasets) = await Task.Run(async () =>
            {
                var allSongs = await _db.GetSongsWithDetailsAsync();
                var localSongs = allSongs.Where(s => s.Source == SongSource.Local).ToList();
                var netSongs = allSongs.Where(s => s.Source != SongSource.Local).ToList();

                var locCount = localSongs.Count;
                var netCount = netSongs.Count;
                var favCount = await _db.GetFavoriteCountAsync();
                var recCount = await _db.GetRecentPlayCountAsync();

                var tSong = allSongs.Count;
                var tAlbum = allSongs.Select(s => s.AlbumId).Distinct().Count();
                var tArtist = allSongs.Select(s => s.ArtistId).Distinct().Count();
                var tHours = allSongs.Sum(s => s.Duration) / 3600.0;

                var totalBytes = localSongs.Sum(s => s.FileSize);
                var musicSizeText = FormatSize(totalBytes);

                var fCount = localSongs.Select(s => GetTopFolder(s.FilePath)).Distinct().Count();
                var freeTxt = GetFreeSpaceText();

                var fGroups = localSongs
                    .GroupBy(s => GetFileExtension(s.FilePath))
                    .Select(g => new FormatSizeItem(
                        g.Key,
                        g.Sum(s => s.FileSize),
                        GetFormatColor(g.Key)))
                    .OrderByDescending(x => x.SizeBytes)
                    .ToList();

                if (fGroups.Count > 0)
                {
                    var maxSize = fGroups.Max(x => x.SizeBytes);
                    foreach (var item in fGroups)
                        item.MaxSizeBytes = maxSize;
                }

                var recItems = localSongs
                    .OrderByDescending(s => s.DateAdded)
                    .Take(8)
                    .Select((s, i) =>
                    {
                        var (c1, c2) = GetGradientColors(i);
                        return new RecentAddItem(
                            s.Title ?? "未知歌曲",
                            s.Artist ?? "未知艺术家",
                            c1, c2);
                    })
                    .ToList();

                var networkOnline = HasNetworkProtocols && netCount > 0;
                var lastSync = "今天 09:14";
                try
                {
                    var lastScan = Preferences.Default.Get("last_scan_time", 0L);
                    if (lastScan > 0)
                    {
                        var dt = DateTimeOffset.FromUnixTimeSeconds(lastScan).LocalDateTime;
                        lastSync = dt.ToString("HH:mm");
                    }
                }
                catch { }

                var cards = new ObservableCollection<LibraryCardItem>
                {
                    new("本地音乐库", $"{locCount} 首 · {fCount} 个文件夹 · 今天 {lastSync} 扫描", "已同步", "ok",
                        "linear-gradient(135deg,#6250F6,#8C7BFF)", "ic_folder.svg", "local"),
                    new("网络音乐库", networkOnline ? $"{netCount} 首 · 已连接" : "未配置",
                        networkOnline ? "在线" : "离线", networkOnline ? "on" : "off",
                        "linear-gradient(135deg,#1E9FE0,#55D6FF)", "ic_wifi.svg", "network"),
                    new("我喜欢的", $"{favCount} 首 · 智能歌单", "", "",
                        "linear-gradient(135deg,#FF5C8A,#FF7AAE)", "ic_favorite.svg", "favorite"),
                    new("最近播放", $"{recCount} 首 · 自动记录", "", "",
                        "linear-gradient(135deg,#7A6CF0,#A78BFA)", "ic_history.svg", "recent"),
                };

                return (localSongCount: locCount, networkSongCount: netCount, favoriteCount: favCount,
                        recentPlayCount: recCount, totalSongCount: tSong, totalAlbumCount: tAlbum,
                        totalArtistCount: tArtist, totalHours: tHours, totalMusicSizeText: musicSizeText,
                        folderCount: fCount, freeSpaceText: freeTxt, formatGroups: fGroups,
                        recent: recItems, libraryCards: cards, pieDatasets: ComputePieDatasets(localSongs));
            });

            // 阶段2：主线程更新 UI 属性
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                LocalSongCount = localSongCount;
                NetworkSongCount = networkSongCount;
                FavoriteCount = favoriteCount;
                RecentPlayCount = recentPlayCount;
                TrashCount = 0;

                TotalSongCount = totalSongCount;
                TotalAlbumCount = totalAlbumCount;
                TotalArtistCount = totalArtistCount;
                TotalHours = totalHours;

                TotalMusicSizeText = totalMusicSizeText;
                FolderCount = folderCount;
                FreeSpaceText = freeSpaceText;

                FormatSizeItems = new ObservableCollection<FormatSizeItem>(formatGroups);
                RecentAddItems = new ObservableCollection<RecentAddItem>(recent);
                LibraryCards = libraryCards;
                LibraryCount = libraryCards.Count;
                PieDatasets = new ObservableCollection<PieDataset>(pieDatasets);
                LoadScanInfo();
            });
        }
        catch (Exception ex)
        {
            Log.Debug("LibraryViewModel", $"[Library] LoadOverviewDataAsync failed: {ex.Message}");
        }
    }

    private string GetFreeSpaceText()
    {
#if ANDROID
        try
        {
            var path = global::Android.App.Application.Context.GetExternalFilesDir(null)?.AbsolutePath;
            if (!string.IsNullOrEmpty(path))
            {
                var statFs = new StatFs(path);
                // 必须用 Long 属性：AvailableBlocks/BlockSize 是 int，大容量下会溢出成负数。
                var availableBytes = statFs.AvailableBlocksLong * statFs.BlockSizeLong;
                return FormatSize(availableBytes);
            }
        }
        catch { }
#endif
        try
        {
            var drive = DriveInfo.GetDrives().FirstOrDefault(d => d.DriveType == DriveType.Fixed && d.IsReady);
            if (drive != null)
            {
                return FormatSize(drive.AvailableFreeSpace);
            }
        }
        catch { }
        return "-- GB";
    }

    private void LoadScanInfo()
    {
        var lastScan = Preferences.Default.Get("last_scan_time", 0L);
        if (lastScan > 0)
        {
            var dt = DateTimeOffset.FromUnixTimeSeconds(lastScan).LocalDateTime;
            var added = Preferences.Default.Get("last_scan_added", 0);
            var elapsed = Preferences.Default.Get("last_scan_elapsed", 0);
            LastScanText = $"{dt:MM-dd HH:mm} · 耗时 {elapsed}s · 新增 {added} 首";
        }
        AutoScanEnabled = Preferences.Default.Get("auto_scan", true);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1073741824) return $"{bytes / 1073741824.0:F1} GB";
        if (bytes >= 1048576) return $"{bytes / 1048576.0:F1} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
    }

    private static string FormatTimeAgo(long unixTimestamp)
    {
        if (unixTimestamp <= 0) return "未知";
        var dt = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp).LocalDateTime;
        var diff = DateTime.Now - dt;
        if (diff.TotalDays < 1) return "今天";
        if (diff.TotalDays < 2) return "昨天";
        if (diff.TotalDays < 7) return $"{(int)diff.TotalDays} 天前";
        if (diff.TotalDays < 30) return $"{(int)(diff.TotalDays / 7)} 周前";
        return dt.ToString("MM-dd");
    }

    private static string GetTopFolder(string path)
    {
        if (string.IsNullOrEmpty(path)) return "未知";
        var dir = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dir)) return path;
        var parts = dir.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 2) return dir;
        return "/" + string.Join("/", parts.Take(3));
    }

    private static string GetFileExtension(string path)
    {
        if (string.IsNullOrEmpty(path)) return "未知";
        var ext = Path.GetExtension(path).ToUpperInvariant();
        return string.IsNullOrEmpty(ext) ? "未知" : ext.TrimStart('.');
    }

    private static Color GetFormatColor(string ext) => ext.ToUpperInvariant() switch
    {
        "MP3" => Color.FromArgb("#8C7BFF"),
        "FLAC" => Color.FromArgb("#55D6FF"),
        "M4A" => Color.FromArgb("#FF7AAE"),
        "WAV" or "APE" => Color.FromArgb("#7AF0C8"),
        "OGG" => Color.FromArgb("#FFB36B"),
        _ => Color.FromArgb("#A78BFA")
    };

    private static readonly (Color C1, Color C2)[] CoverPalettes = {
        (Color.FromArgb("#8C7BFF"), Color.FromArgb("#55D6FF")),
        (Color.FromArgb("#FF7AAE"), Color.FromArgb("#FFB36B")),
        (Color.FromArgb("#55D6FF"), Color.FromArgb("#7AF0C8")),
        (Color.FromArgb("#A78BFA"), Color.FromArgb("#F0ABFC")),
        (Color.FromArgb("#5EEAD4"), Color.FromArgb("#60A5FA")),
        (Color.FromArgb("#FF6C5C"), Color.FromArgb("#FFB36B")),
        (Color.FromArgb("#818CF8"), Color.FromArgb("#A78BFA")),
        (Color.FromArgb("#F472B6"), Color.FromArgb("#FB7185"))
    };

    private static (Color C1, Color C2) GetGradientColors(int index) =>
        CoverPalettes[Math.Abs(index) % CoverPalettes.Length];

    // ═══════════════════════════════════════════════════════
    // 数据洞察：环形图数据集聚合
    // ═══════════════════════════════════════════════════════

    private static readonly Color[] PiePalette = {
        Color.FromArgb("#8C7BFF"), Color.FromArgb("#55D6FF"), Color.FromArgb("#FF7AAE"),
        Color.FromArgb("#7AF0C8"), Color.FromArgb("#FFB36B"), Color.FromArgb("#F0ABFC"),
        Color.FromArgb("#5EEAD4"), Color.FromArgb("#A78BFA")
    };

    /// <summary>音频音质分桶的稳定配色，避免按哈希取色导致撞色/每次刷新颜色跳变。</summary>
    private static readonly Dictionary<string, Color> QualityColorMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["无损"] = Color.FromArgb("#7AF0C8"),
        ["320 kbps"] = Color.FromArgb("#8C7BFF"),
        ["256 kbps"] = Color.FromArgb("#55D6FF"),
        ["192 kbps"] = Color.FromArgb("#FF7AAE"),
        ["128 kbps"] = Color.FromArgb("#FFB36B"),
        ["未知"] = Color.FromArgb("#8D93B7"),
        ["其他"] = Color.FromArgb("#A78BFA")
    };

    private static List<PieDataset> ComputePieDatasets(List<Song> localSongs)
    {
        var datasets = new List<PieDataset>();

        // 1) 音频格式（按歌曲数量，颜色与存储占用卡一致）
        var fmtSegs = localSongs
            .GroupBy(s => GetFileExtension(s.FilePath))
            .Select(g => new PieSegmentData(g.Key, g.Count(), GetFormatColor(g.Key)))
            .OrderByDescending(x => x.Count)
            .ToList();
        datasets.Add(new PieDataset("音频格式", fmtSegs.Sum(x => x.Count), fmtSegs));

        // 2) 音频音质（按比特率分桶，颜色用稳定映射，不随刷新跳变）
        var qualitySegs = localSongs
            .GroupBy(s => QualityBucket(s))
            .Select(g => new PieSegmentData(g.Key, g.Count(),
                QualityColorMap.TryGetValue(g.Key, out var c) ? c : PiePalette[Math.Abs(g.Key.GetHashCode()) % PiePalette.Length]))
            .OrderByDescending(x => x.Count)
            .ToList();
        datasets.Add(new PieDataset("音频音质", qualitySegs.Sum(x => x.Count), qualitySegs));

        // 3) 艺术家歌曲（Top5 + 其他）
        var artistGroups = localSongs
            .GroupBy(s => string.IsNullOrEmpty(s.Artist) ? "未知艺术家" : s.Artist)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToList();
        int artistTotal = artistGroups.Sum(x => x.Count);
        var topArtists = artistGroups.Take(5).ToList();
        int topSum = topArtists.Sum(x => x.Count);
        var artistSegs = topArtists
            .Select((a, i) => new PieSegmentData(a.Name, a.Count, PiePalette[i % PiePalette.Length]))
            .ToList();
        int other = artistTotal - topSum;
        if (other > 0)
            artistSegs.Add(new PieSegmentData("其他", other, Color.FromArgb("#8D93B7")));
        datasets.Add(new PieDataset("艺术家歌曲", artistTotal, artistSegs));

        return datasets;
    }

    private static string QualityBucket(Song s)
    {
        var ext = GetFileExtension(s.FilePath);
        if (IsLossless(ext)) return "无损";
        var b = s.Bitrate;
        if (b <= 0) return "未知";
        if (b >= 300) return "320 kbps";
        if (b >= 240) return "256 kbps";
        if (b >= 160) return "192 kbps";
        if (b >= 100) return "128 kbps";
        return "其他";
    }

    private static bool IsLossless(string ext) => ext.ToUpperInvariant() switch
    {
        "FLAC" or "APE" or "WAV" or "AIFF" or "DSD" or "TTA" or "WV" => true,
        _ => false
    };
}

public class LibraryCardItem
{
    public string Name { get; }
    public string Subtitle { get; }
    public string StatusText { get; }
    public string StatusType { get; }
    public string IconBackground { get; }
    public string IconSource { get; }
    public string Target { get; }

    public LibraryCardItem(string name, string subtitle, string statusText, string statusType,
        string iconBackground, string iconSource, string target)
    {
        Name = name;
        Subtitle = subtitle;
        StatusText = statusText;
        StatusType = statusType;
        IconBackground = iconBackground;
        IconSource = iconSource;
        Target = target;
    }
}

public class FormatSizeItem : ObservableObject
{
    public string Name { get; }
    public long SizeBytes { get; }
    public Color Color { get; }
    public string SizeText => FormatSize(SizeBytes);

    private long _maxSizeBytes;
    public long MaxSizeBytes
    {
        get => _maxSizeBytes;
        set => SetProperty(ref _maxSizeBytes, value);
    }

    public double Progress => _maxSizeBytes > 0 ? (double)SizeBytes / _maxSizeBytes : 0;

    public FormatSizeItem(string name, long sizeBytes, Color color)
    {
        Name = name;
        SizeBytes = sizeBytes;
        Color = color;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1073741824) return $"{bytes / 1073741824.0:F1} GB";
        if (bytes >= 1048576) return $"{bytes / 1048576.0:F1} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
    }
}

public class RecentAddItem
{
    public string Title { get; }
    public string Artist { get; }
    public Color CoverColor1 { get; }
    public Color CoverColor2 { get; }
    public string Initial { get; }

    public RecentAddItem(string title, string artist, Color coverColor1, Color coverColor2)
    {
        Title = title;
        Artist = artist;
        CoverColor1 = coverColor1;
        CoverColor2 = coverColor2;
        Initial = string.IsNullOrEmpty(title) ? "♪" : title.Trim()[0].ToString().ToUpper();
    }
}

public class FolderInfo
{
    public string Path { get; }
    public int SongCount { get; }
    public string SizeText { get; }

    public FolderInfo(string path, int songCount, string sizeText)
    {
        Path = path;
        SongCount = songCount;
        SizeText = sizeText;
    }
}

public class GenreBarData
{
    public string Name { get; }
    public int Count { get; }

    public GenreBarData(string name, int count)
    {
        Name = name;
        Count = count;
    }
}

public class RecentSongItem
{
    public string Title { get; }
    public string Artist { get; }
    public string TimeAgo { get; }
    public Color CoverColor { get; }
    public string Initial { get; }

    public RecentSongItem(string title, string artist, string timeAgo, Color coverColor)
    {
        Title = title;
        Artist = artist;
        TimeAgo = timeAgo;
        CoverColor = coverColor;
        Initial = string.IsNullOrEmpty(title) ? "♪" : title.Trim()[0].ToString().ToUpper();
    }
}

public class ArtistRankItem
{
    public string Name { get; }
    public int SongCount { get; }
    public double Percentage { get; }
    public Color AvatarColor { get; }
    public string Initial { get; }
    public string SubInfo => $"{SongCount} 首歌曲";

    public ArtistRankItem(string name, int songCount, double percentage, Color avatarColor)
    {
        Name = name;
        SongCount = songCount;
        Percentage = percentage;
        AvatarColor = avatarColor;
        Initial = string.IsNullOrEmpty(name) ? "♪" : name.Trim()[0].ToString().ToUpper();
    }
}

public class PieSegmentData
{
    public string Name { get; }
    public int Count { get; }
    public Color Color { get; }

    public PieSegmentData(string name, int count, Color color)
    {
        Name = name;
        Count = count;
        Color = color;
    }
}

/// <summary>数据洞察环形图的一个数据集（如「音频格式」），含若干分段与总计。</summary>
public class PieDataset
{
    public string Name { get; }
    public int Total { get; }
    public IReadOnlyList<PieSegmentData> Segments { get; }

    public PieDataset(string name, int total, IEnumerable<PieSegmentData> segments)
    {
        Name = name;
        Total = total;
        Segments = segments.ToList();
    }
}
