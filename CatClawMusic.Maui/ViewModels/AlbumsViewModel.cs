using CatClawMusic.Core.Models;
using CatClawMusic.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>
/// 专辑列表页 ViewModel：从本地数据库加载所有专辑，支持搜索、来源筛选、排序和视图切换。
/// </summary>
public partial class AlbumsViewModel : ObservableObject
{
    private readonly ExploreDataService _exploreData;

    // === 数据源 ===
    private List<AlbumWithCount> _allAlbums = new();

    /// <summary>专辑集合（含每张专辑的歌曲数量与示例封面）</summary>
    [ObservableProperty]
    private ObservableCollection<AlbumWithCount> _albums = new();

    /// <summary>筛选后的专辑列表（用于列表视图）</summary>
    [ObservableProperty]
    private ObservableCollection<AlbumWithCount> _filteredAlbums = new();

    /// <summary>年代分组（用于网格视图）</summary>
    [ObservableProperty]
    private ObservableCollection<EraGroup> _eraGroups = new();

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
    private bool _isGridView = true;

    /// <summary>当前选中的专辑</summary>
    [ObservableProperty]
    private AlbumWithCount? _selectedAlbum;

    // === Hero 统计数据 ===
    [ObservableProperty] private int _totalAlbums;
    [ObservableProperty] private int _totalSongs;
    [ObservableProperty] private int _totalArtists;

    // === 筛选与排序 ===
    [ObservableProperty] private string _currentFilter = "all";
    [ObservableProperty] private string _currentEra = "all";
    [ObservableProperty] private string _currentSort = "default";

    /// <summary>来源筛选选项</summary>
    [ObservableProperty]
    private ObservableCollection<FilterChip> _sourceFilters = new();

    /// <summary>排序选项</summary>
    [ObservableProperty]
    private ObservableCollection<SortOption> _sortOptions = new();

    /// <summary>年代 rail 选项</summary>
    [ObservableProperty]
    private ObservableCollection<EraRailItem> _eraRailItems = new();

    // === 颜色绑定 ===
    [ObservableProperty] private Color _gridButtonColor;
    [ObservableProperty] private Color _listButtonColor;

    // === 静态数据 ===
    private static readonly string[] EraOrder = { "2020s", "2010s", "2000s", "1990s", "1980s", "1970s", "更早", "未知" };
    private static readonly Color AccentColor = Color.FromArgb("#8C7BFF");
    private static readonly Color TransparentColor = Colors.Transparent;

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
            new("default", "默认", true),
            new("year", "年份新→旧", false),
            new("name", "名称 A-Z", false),
            new("count", "歌曲数", false),
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

            var albums = await _exploreData.GetAllAlbumsAsync();
            _allAlbums = albums;

            // 批量解析封面
            await Task.Run(() =>
            {
                var pending = new Dictionary<int, Song>();
                foreach (var album in _allAlbums)
                {
                    if (album.SampleSongId > 0)
                    {
                        var cachedPath = Services.CoverHelper.GetCachedPath(album.SampleSongId);
                        if (File.Exists(cachedPath))
                        {
                            album.CoverArtPath = cachedPath;
                            continue;
                        }
                    }

                    if (album.SampleSongId > 0 && !string.IsNullOrEmpty(album.SampleFilePath)
                        && !pending.ContainsKey(album.SampleSongId))
                    {
                        pending[album.SampleSongId] = new Song { Id = album.SampleSongId, FilePath = album.SampleFilePath };
                    }
                }

                if (pending.Count > 0)
                {
                    Services.CoverHelper.BatchResolveCovers(pending.Values);
                    foreach (var album in _allAlbums)
                    {
                        if (string.IsNullOrEmpty(album.CoverArtPath) && album.SampleSongId > 0
                            && pending.TryGetValue(album.SampleSongId, out var s)
                            && !string.IsNullOrEmpty(s.CoverArtPath))
                        {
                            album.CoverArtPath = s.CoverArtPath;
                        }
                    }
                }
            });

            // 更新统计
            TotalAlbums = _allAlbums.Count;
            TotalSongs = _allAlbums.Sum(a => a.SongCount);
            TotalArtists = _allAlbums.Select(a => a.ArtistName).Distinct().Count();

            // 构建年代 rail
            BuildEraRail();

            // 应用筛选和排序
            ApplyFiltersAndSort();

            StatusText = $"共 {TotalAlbums} 张专辑";
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

    /// <summary>构建年代 rail 数据</summary>
    private void BuildEraRail()
    {
        var eraCounts = new Dictionary<string, int> { { "all", _allAlbums.Count } };

        foreach (var era in EraOrder)
        {
            var count = _allAlbums.Count(a => GetEra(a.Year) == era);
            if (count > 0)
                eraCounts[era] = count;
        }

        EraRailItems = new ObservableCollection<EraRailItem>();
        EraRailItems.Add(new EraRailItem("all", "全", CurrentEra == "all"));

        foreach (var era in EraOrder)
        {
            if (eraCounts.TryGetValue(era, out var count) && count > 0)
            {
                EraRailItems.Add(new EraRailItem(era, era.Replace("s", "").Replace("更早", "旧").Replace("未知", "?"), CurrentEra == era));
            }
        }
    }

    /// <summary>根据年份获取年代分组</summary>
    private static string GetEra(int? year)
    {
        if (!year.HasValue || year < 1960) return "未知";
        var y = year.Value;
        if (y >= 2020) return "2020s";
        if (y >= 2010) return "2010s";
        if (y >= 2000) return "2000s";
        if (y >= 1990) return "1990s";
        if (y >= 1980) return "1980s";
        if (y >= 1970) return "1970s";
        return "更早";
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

    /// <summary>选择年代</summary>
    public void SelectEra(string eraKey)
    {
        CurrentEra = eraKey;
        foreach (var item in EraRailItems)
            item.IsActive = item.Key == eraKey;
        OnPropertyChanged(nameof(EraRailItems));
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

    /// <partial name="IsGridView"/>变化时更新颜色</summary>
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

        // 2. 年代筛选
        if (CurrentEra != "all")
            result = result.Where(a => GetEra(a.Year) == CurrentEra);

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
            "year" => result.OrderByDescending(a => a.Year ?? 0),
            "name" => result.OrderBy(a => a.Title, StringComparer.CurrentCultureIgnoreCase),
            "count" => result.OrderByDescending(a => a.SongCount),
            "play" => result.OrderByDescending(a => 0), // TODO: 添加播放次数
            _ => result.OrderByDescending(a => a.Year ?? 0).ThenBy(a => a.Title, StringComparer.CurrentCultureIgnoreCase)
        };

        var filtered = result.ToList();
        FilteredAlbums = new ObservableCollection<AlbumWithCount>(filtered);

        // 构建网格视图的年代分组
        if (IsGridView)
        {
            BuildEraGroups(filtered);
        }
    }

    /// <summary>构建年代分组（网格视图）</summary>
    private void BuildEraGroups(List<AlbumWithCount> albums)
    {
        var groups = new ObservableCollection<EraGroup>();

        foreach (var era in EraOrder)
        {
            var items = albums.Where(a => GetEra(a.Year) == era).ToList();
            if (items.Count > 0)
            {
                groups.Add(new EraGroup(era, items.Count, items));
            }
        }

        EraGroups = groups;
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
        }

        public Color TextColor => IsActive ? Color.FromArgb("#EAF0FF") : Color.FromArgb("#67729B");
        public Color BackgroundColor => IsActive ? Color.FromArgb("#1AFFFFFF") : TransparentColor;
        public Color BorderColor => IsActive ? Color.FromArgb("#4DFFFFFF") : TransparentColor;
    }

    /// <summary>年代 rail 选项模型</summary>
    public partial class EraRailItem : ObservableObject
    {
        public string Key { get; }
        public string Label { get; }

        [ObservableProperty]
        private bool _isActive;

        public EraRailItem(string key, string label, bool active)
        {
            Key = key;
            Label = label;
            IsActive = active;
        }

        public Color BackgroundColor => IsActive ? AccentColor : TransparentColor;
        public Color TextColor => IsActive ? Colors.White : Color.FromArgb("#67729B");
    }

    /// <summary>年代分组（网格视图）</summary>
    public class EraGroup
    {
        public string Era { get; }
        public int Count { get; }
        public List<AlbumWithCount> Items { get; }

        public EraGroup(string era, int count, List<AlbumWithCount> items)
        {
            Era = era;
            Count = count;
            Items = items;
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
