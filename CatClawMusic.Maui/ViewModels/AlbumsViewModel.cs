using CatClawMusic.Core.Models;
using CatClawMusic.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>
/// 专辑列表页 ViewModel：从本地数据库加载所有专辑，支持搜索、来源筛选、A-Z 字母索引、排序和视图切换。
/// 布局与排序逻辑与 <see cref="ArtistsViewModel"/> 保持一致。
/// </summary>
public partial class AlbumsViewModel : ObservableObject
{
    private readonly ExploreDataService _exploreData;

    // === 数据源 ===
    private List<AlbumWithCount> _allAlbums = new();

    /// <summary>筛选后的专辑列表（列表视图 + 网格视图共用）</summary>
    [ObservableProperty]
    private ObservableCollection<AlbumWithCount> _filteredAlbums = new();

    // === UI 状态 ===
    /// <summary>是否正在加载专辑数据</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>状态文本</summary>
    [ObservableProperty]
    private string _statusText = "加载中...";

    /// <summary>是否显示搜索框</summary>
    [ObservableProperty]
    private bool _isSearchVisible;

    /// <summary>搜索关键词</summary>
    [ObservableProperty]
    private string _searchQuery = "";

    /// <summary>是否为网格视图（false则为列表视图）</summary>
    [ObservableProperty]
    private bool _isGridView = false;

    /// <summary>当前选中的专辑</summary>
    [ObservableProperty]
    private AlbumWithCount? _selectedAlbum;

    // === Hero 统计数据 ===
    [ObservableProperty] private int _totalAlbums;
    [ObservableProperty] private int _totalSongs;
    [ObservableProperty] private int _totalArtists;

    // === 筛选与排序 ===
    [ObservableProperty] private string _currentFilter = "all";
    [ObservableProperty] private string _currentLetter = "";
    [ObservableProperty] private string _currentSort = "name";

    /// <summary>来源筛选选项</summary>
    [ObservableProperty]
    private ObservableCollection<FilterChip> _sourceFilters = new();

    /// <summary>排序选项</summary>
    [ObservableProperty]
    private ObservableCollection<SortOption> _sortOptions = new();

    /// <summary>A-Z 字母 rail 选项</summary>
    [ObservableProperty]
    private ObservableCollection<LetterRailItem> _letterRailItems = new();

    // === 颜色绑定 ===
    [ObservableProperty] private Color _gridButtonColor;
    [ObservableProperty] private Color _listButtonColor;

    // === 静态数据 ===
    private static readonly Color AccentColor = Color.FromArgb("#8C7BFF");
    private static readonly Color TransparentColor = Colors.Transparent;

    // === 跨实例静态缓存：进入页面时若底层数据未变，直接复用已处理好的集合，实现"秒开" ===
    private static readonly object _cacheLock = new();
    private static List<AlbumWithCount>? _cachedAllAlbums;
    private static ObservableCollection<AlbumWithCount>? _cachedFilteredAlbums;
    private static ObservableCollection<LetterRailItem>? _cachedLetterRailItems;
    private static int _cachedTotalAlbums;
    private static int _cachedTotalSongs;
    private static int _cachedTotalArtists;

    /// <summary>
    /// 初始化 <see cref="AlbumsViewModel"/> 实例。
    /// </summary>
    /// <param name="exploreData">探索页数据服务，用于读取专辑聚合数据</param>
    public AlbumsViewModel(ExploreDataService exploreData)
    {
        _exploreData = exploreData;
        InitializeFilterChips();
        InitializeSortOptions();
        UpdateViewToggleColors();
    }

    /// <summary>初始化筛选 chip</summary>
    private void InitializeFilterChips()
    {
        SourceFilters = new ObservableCollection<FilterChip>
        {
            new("all", "全部", true),
            new("local", "本地", false),
            new("network", "网络", false),
        };
    }

    /// <summary>初始化排序选项</summary>
    private void InitializeSortOptions()
    {
        SortOptions = new ObservableCollection<SortOption>
        {
            new("name", "名称 A-Z", true),
            new("count", "歌曲数", false),
            new("year", "年份", false),
            new("play", "最常听", false),
        };
    }

    /// <summary>异步加载所有专辑</summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            IsLoading = true;
            StatusText = "正在加载专辑...";

            // 1) 快速路径：仅做 SQL 查询 + 聚合（无文件 IO），在后台线程完成
            var albums = await Task.Run(() => _exploreData.GetAllAlbumsAsync());
            _allAlbums = albums;

            // 2) 若底层数据未变化（ExploreDataService 命中缓存，返回同一实例），
            //    直接复用已处理好的列表/字母索引，主线程零重建 → 进入页面秒开。
            bool instant;
            lock (_cacheLock)
                instant = ReferenceEquals(albums, _cachedAllAlbums)
                          && _cachedFilteredAlbums != null
                          && _cachedLetterRailItems != null;

            if (instant)
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    TotalAlbums = _cachedTotalAlbums;
                    TotalSongs = _cachedTotalSongs;
                    TotalArtists = _cachedTotalArtists;
                    LetterRailItems = _cachedLetterRailItems!;
                    FilteredAlbums = _cachedFilteredAlbums!;
                    StatusText = $"共 {TotalAlbums} 张专辑";
                    IsLoading = false;
                });
            }
            else
            {
                // 3) 立即渲染列表（占位图），不让封面解析阻塞首屏
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    TotalAlbums = _allAlbums.Count;
                    TotalSongs = _allAlbums.Sum(a => a.SongCount);
                    TotalArtists = _allAlbums.Select(a => a.ArtistName).Distinct().Count();
                    BuildLetterRail();
                    ApplyFiltersAndSort();
                    StatusText = $"共 {TotalAlbums} 张专辑";
                    IsLoading = false;
                });

                // 缓存本次处理好的集合，供下次进入复用
                lock (_cacheLock)
                {
                    _cachedAllAlbums = albums;
                    _cachedFilteredAlbums = FilteredAlbums;
                    _cachedLetterRailItems = LetterRailItems;
                    _cachedTotalAlbums = TotalAlbums;
                    _cachedTotalSongs = TotalSongs;
                    _cachedTotalArtists = TotalArtists;
                }
            }

            // 4) 后台渐进式解析封面（不阻塞列表渲染），封面就绪后通过 INPC 自动刷新可见 cell
            _ = Task.Run(async () => await ResolveCoversInBackground(_allAlbums));
        }
        catch (Exception ex)
        {
            StatusText = $"加载失败: {ex.Message}";
            IsLoading = false;
        }
    }

    /// <summary>使静态缓存失效：扫描后数据变化，下次进入重新构建。</summary>
    public static void InvalidateCache()
    {
        lock (_cacheLock)
        {
            _cachedAllAlbums = null;
            _cachedFilteredAlbums = null;
            _cachedLetterRailItems = null;
        }
    }

    /// <summary>
    /// 后台渐进式解析专辑封面：先把磁盘已缓存的封面直接赋值（即时显示），
    /// 其余未缓存的再分批提取内嵌封面并写入缓存，全程不阻塞 UI 线程。
    /// </summary>
    private async Task ResolveCoversInBackground(List<AlbumWithCount> albums)
    {
        try
        {
            // SongId -> Album 映射，便于解析完成后回填封面并触发 INPC
            var bySongId = albums
                .Where(a => a.SampleSongId > 0)
                .GroupBy(a => a.SampleSongId)
                .ToDictionary(g => g.Key, g => g.First());

            var pending = new List<Song>();
            foreach (var album in albums)
            {
                if (album.SampleSongId <= 0) continue;

                // 磁盘缓存命中：直接赋值，立刻可见
                var cachedPath = Services.CoverHelper.GetCachedPath(album.SampleSongId, Services.CoverHelper.ThumbnailSize);
                if (File.Exists(cachedPath))
                {
                    album.CoverArtPath = cachedPath;
                    continue;
                }

                if (!string.IsNullOrEmpty(album.SampleFilePath)
                    && !pending.Exists(s => s.Id == album.SampleSongId))
                {
                    pending.Add(new Song { Id = album.SampleSongId, FilePath = album.SampleFilePath });
                }
            }

            if (pending.Count == 0) return;

            // 分块异步提取，避免一次性并行解码成千上万个音频文件导致主线程被拖垮
            await Services.CoverHelper.BatchResolveCoversAsync(pending);

            // 回填封面（INPC 让可见 cell 自动刷新）
            foreach (var s in pending)
            {
                if (!string.IsNullOrEmpty(s.CoverArtPath) && bySongId.TryGetValue(s.Id, out var a))
                    a.CoverArtPath = s.CoverArtPath;
            }
        }
        catch { /* 封面解析失败不应影响列表展示 */ }
    }

    /// <summary>构建 A-Z 字母 rail 数据</summary>
    private void BuildLetterRail()
    {
        var letters = _allAlbums
            .Select(a => GetIndexLetter(a.Title))
            .Distinct()
            .OrderBy(l => l, new LetterComparer())
            .ToList();

        var items = new ObservableCollection<LetterRailItem>();
        foreach (var letter in letters)
        {
            items.Add(new LetterRailItem(letter, letter == CurrentLetter));
        }

        LetterRailItems = items;
    }

    /// <summary>获取专辑标题的索引字母（中文取首字，英文取首字母大写，数字/符号归 #）</summary>
    private static string GetIndexLetter(string? title)
    {
        if (string.IsNullOrEmpty(title)) return "#";
        var c = title.Trim()[0];
        if (char.IsAsciiLetter(c)) return char.ToUpperInvariant(c).ToString();
        if (char.IsDigit(c)) return "#";
        return c.ToString(); // 中文直接用首字
    }

    /// <summary>字母排序比较器：A-Z → 中文（按 Unicode 码点）→ #</summary>
    private sealed class LetterComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return 1;
            if (y == null) return -1;

            int rank(string s) => s switch
            {
                "#" => 2,
                _ when s.Length == 1 && char.IsAsciiLetter(s[0]) => 0,
                _ => 1
            };

            int rx = rank(x), ry = rank(y);
            if (rx != ry) return rx.CompareTo(ry);
            return string.Compare(x, y, StringComparison.Ordinal);
        }
    }

    /// <summary>选择筛选条件</summary>
    public void SelectFilter(string filterKey)
    {
        CurrentFilter = filterKey;
        foreach (var chip in SourceFilters)
            chip.IsActive = chip.FilterKey == filterKey;
        OnPropertyChanged(nameof(SourceFilters));
        ApplyFiltersAndSort();
    }

    /// <summary>选择字母</summary>
    public void SelectLetter(string letter)
    {
        CurrentLetter = CurrentLetter == letter ? "" : letter;
        foreach (var item in LetterRailItems)
            item.IsActive = item.Key == CurrentLetter;
        OnPropertyChanged(nameof(LetterRailItems));
        ApplyFiltersAndSort();
    }

    /// <summary>选择排序方式</summary>
    public void SelectSort(string sortKey)
    {
        CurrentSort = sortKey;
        foreach (var option in SortOptions)
            option.IsActive = option.Key == sortKey;
        OnPropertyChanged(nameof(SortOptions));
        ApplyFiltersAndSort();
    }

    /// <summary>IsGridView 变化时更新颜色</summary>
    partial void OnIsGridViewChanged(bool value) => UpdateViewToggleColors();

    /// <summary>更新视图切换按钮颜色</summary>
    private void UpdateViewToggleColors()
    {
        GridButtonColor = IsGridView ? AccentColor : TransparentColor;
        ListButtonColor = !IsGridView ? AccentColor : TransparentColor;
    }

    /// <summary>搜索查询变化时触发筛选</summary>
    partial void OnSearchQueryChanged(string value) => ApplyFiltersAndSort();

    /// <summary>应用筛选、排序并刷新视图</summary>
    private void ApplyFiltersAndSort()
    {
        IEnumerable<AlbumWithCount> result = _allAlbums;

        // 1. 来源筛选
        result = CurrentFilter switch
        {
            "local" => result.Where(a => GetAlbumSource(a) == "本地"),
            "network" => result.Where(a => GetAlbumSource(a) == "网络"),
            _ => result
        };

        // 2. 字母筛选
        if (!string.IsNullOrEmpty(CurrentLetter))
        {
            result = result.Where(a => GetIndexLetter(a.Title) == CurrentLetter);
        }

        // 3. 搜索筛选
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            var q = SearchQuery.ToLowerInvariant();
            result = result.Where(a =>
                (a.Title?.ToLowerInvariant().Contains(q) ?? false) ||
                (a.ArtistName?.ToLowerInvariant().Contains(q) ?? false));
        }

        // 4. 排序
        result = CurrentSort switch
        {
            "name" => result.OrderBy(a => a.Title, new TitleComparer()),
            "count" => result.OrderByDescending(a => a.SongCount),
            "year" => result.OrderByDescending(a => a.Year ?? 0),
            "play" => result.OrderByDescending(a => 0), // TODO: 添加播放次数
            _ => result.OrderBy(a => a.Title, new TitleComparer())
        };

        FilteredAlbums = new ObservableCollection<AlbumWithCount>(result.ToList());
    }

    /// <summary>标题排序比较器：英文 A-Z 优先，中文按 Unicode 码点，数字/符号最后</summary>
    private sealed class TitleComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return 1;
            if (y == null) return -1;

            int rank(string s)
            {
                if (string.IsNullOrEmpty(s)) return 3;
                var c = s.Trim()[0];
                if (char.IsAsciiLetter(c)) return 0;
                if (c >= 0x4E00 && c <= 0x9FFF) return 1; // CJK
                return 2;
            }

            int rx = rank(x), ry = rank(y);
            if (rx != ry) return rx.CompareTo(ry);
            return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>获取专辑来源类型（本地/网络）</summary>
    private static string GetAlbumSource(AlbumWithCount album)
    {
        // 根据 SampleFilePath 判断来源
        if (!string.IsNullOrEmpty(album.SampleFilePath))
        {
            if (album.SampleFilePath.StartsWith("content://") ||
                album.SampleFilePath.StartsWith("file://") ||
                (!album.SampleFilePath.StartsWith("http") && !album.SampleFilePath.StartsWith("smb://")))
            {
                return "本地";
            }
        }
        return "网络";
    }

    // === 辅助数据类 ===

    /// <summary>筛选 chip 模型</summary>
    public partial class FilterChip : ObservableObject
    {
        public string FilterKey { get; }
        public string Label { get; }

        [ObservableProperty]
        private bool _isActive;

        public FilterChip(string key, string label, bool active)
        {
            FilterKey = key;
            Label = label;
            IsActive = active;
        }

        public Color BackgroundColor => IsActive ? AccentColor : TransparentColor;
        public Color TextColor => IsActive ? Colors.White : Color.FromArgb("#A8B4D8");
        public Color BorderColor => IsActive ? TransparentColor : Color.FromArgb("#33FFFFFF");

        partial void OnIsActiveChanged(bool value)
        {
            OnPropertyChanged(nameof(BackgroundColor));
            OnPropertyChanged(nameof(TextColor));
            OnPropertyChanged(nameof(BorderColor));
        }
    }

    /// <summary>排序选项模型</summary>
    public partial class SortOption : ObservableObject
    {
        public string Key { get; }
        public string Label { get; }

        [ObservableProperty]
        private bool _isActive;

        public SortOption(string key, string label, bool active)
        {
            Key = key;
            Label = label;
            IsActive = active;
            SubscribeTheme();
        }

        ~SortOption() => UnsubscribeTheme();

        // 主题色从 Application.Current.Resources 实时读取，跟随 ThemeService 主题切换。
        private static Color Accent() => (Color)(Application.Current?.Resources["PrimaryColor"] ?? Color.FromArgb("#8C7BFF"));
        private static Color AccentDark() => (Color)(Application.Current?.Resources["PrimaryDarkColor"] ?? Color.FromArgb("#6250F6"));
        private static Color AccentLight() => (Color)(Application.Current?.Resources["PrimaryLightColor"] ?? Color.FromArgb("#B7AEFF"));
        private static Color TextHint() => (Color)(Application.Current?.Resources["TextHintColor"] ?? Color.FromArgb("#8D93B7"));

        public Color BackgroundColor => IsActive
            ? Accent()
            : AccentLight().WithAlpha(0.18f);
        public Color TextColor => IsActive ? Colors.White : TextHint();
        public Color BorderColor => IsActive
            ? AccentDark()
            : AccentLight().WithAlpha(0.30f);

        partial void OnIsActiveChanged(bool value)
        {
            OnPropertyChanged(nameof(BackgroundColor));
            OnPropertyChanged(nameof(TextColor));
            OnPropertyChanged(nameof(BorderColor));
        }

        private void SubscribeTheme()
        {
            if (Application.Current != null)
                Application.Current.RequestedThemeChanged += OnAppThemeChanged;
        }
        private void UnsubscribeTheme()
        {
            if (Application.Current != null)
                Application.Current.RequestedThemeChanged -= OnAppThemeChanged;
        }
        private void OnAppThemeChanged(object? sender, AppThemeChangedEventArgs e)
        {
            OnPropertyChanged(nameof(BackgroundColor));
            OnPropertyChanged(nameof(TextColor));
            OnPropertyChanged(nameof(BorderColor));
        }
    }

    /// <summary>A-Z 字母 rail 选项模型</summary>
    public partial class LetterRailItem : ObservableObject
    {
        public string Key { get; }
        public string Label { get; }

        [ObservableProperty]
        private bool _isActive;

        public LetterRailItem(string key, bool active)
        {
            Key = key;
            Label = key;
            IsActive = active;
        }

        public Color BackgroundColor => IsActive ? Color.FromArgb("#8C7BFF33") : TransparentColor;
        public Color TextColor => IsActive ? Colors.White : Color.FromArgb("#7A85B0");

        partial void OnIsActiveChanged(bool value)
        {
            OnPropertyChanged(nameof(BackgroundColor));
            OnPropertyChanged(nameof(TextColor));
        }
    }
}

/// <summary>专辑显示扩展（用于绑定封面初始字符和占位颜色）</summary>
public static class AlbumDisplayExtensions
{
    private static readonly string[] Palettes = {
        "#8C7BFF,#55D6FF", "#FF7AAE,#FFB36B", "#55D6FF,#7AF0C8", "#A78BFA,#F0ABFC",
        "#5EEAD4,#60A5FA", "#FBBF24,#FB7185", "#818CF8,#22D3EE", "#F472B6,#C084FC"
    };

    /// <summary>获取专辑封面初始字符</summary>
    public static string GetInitial(this AlbumWithCount album) =>
        string.IsNullOrEmpty(album.Title) ? "♪" : album.Title.Trim()[0].ToString().ToUpper();

    /// <summary>获取占位渐变背景色</summary>
    public static Color GetPlaceholderColor(this AlbumWithCount album)
    {
        var index = Math.Abs(album.Id) % Palettes.Length;
        var colors = Palettes[index].Split(',');
        return Color.FromArgb(colors[0]);
    }

    /// <summary>获取子信息文本</summary>
    public static string GetSubInfo(this AlbumWithCount album)
    {
        var yearStr = album.Year.HasValue ? album.Year.Value.ToString() : "未知";
        return $"{yearStr} · {album.SongCount} 首";
    }
}
