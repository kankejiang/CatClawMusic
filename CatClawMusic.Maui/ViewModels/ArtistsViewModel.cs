using CatClawMusic.Core.Models;
using CatClawMusic.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>
/// 艺术家列表页 ViewModel：从本地数据库加载所有艺术家，支持搜索、来源筛选、排序、视图切换和字母索引。
/// </summary>
public partial class ArtistsViewModel : ObservableObject
{
    private readonly ExploreDataService _exploreData;

    // === 数据源 ===
    private List<ArtistWithCount> _allArtists = new();

    /// <summary>筛选后的艺术家列表</summary>
    [ObservableProperty]
    private ObservableCollection<ArtistWithCount> _filteredArtists = new();

    // === UI 状态 ===
    /// <summary>是否正在加载艺术家数据</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>状态文本</summary>
    [ObservableProperty]
    private string _statusText = "加载中...";

    /// <summary>搜索关键词</summary>
    [ObservableProperty]
    private string _searchQuery = "";

    /// <summary>是否为网格视图（false则为列表视图）</summary>
    [ObservableProperty]
    private bool _isGridView = true;

    /// <summary>当前选中的艺术家</summary>
    [ObservableProperty]
    private ArtistWithCount? _selectedArtist;

    /// <summary>是否显示搜索框</summary>
    [ObservableProperty]
    private bool _isSearchVisible = true;

    /// <summary>艺术家总数</summary>
    [ObservableProperty] private int _totalArtists;

    // === 最常聆听 ===
    [ObservableProperty] private ArtistWithCount? _mostPlayedArtist;
    [ObservableProperty] private bool _hasMostPlayed;
    [ObservableProperty] private string _mostPlayedName = "";
    [ObservableProperty] private string _mostPlayedSubInfo = "";
    [ObservableProperty] private string _mostPlayedInitial = "";
    [ObservableProperty] private bool _mostPlayedHasCover;
    [ObservableProperty] private ImageSource? _mostPlayedCover;
    [ObservableProperty] private Color _mostPlayedPlaceholderColor;

    // === 筛选与排序 ===
    [ObservableProperty] private string _currentFilter = "all";
    [ObservableProperty] private string _currentLetter = "";
    [ObservableProperty] private string _currentSort = "default";

    /// <summary>来源筛选选项</summary>
    [ObservableProperty]
    private ObservableCollection<FilterChip> _sourceFilters = new();

    /// <summary>排序选项</summary>
    [ObservableProperty]
    private ObservableCollection<SortOption> _sortOptions = new();

    /// <summary>字母 rail 选项</summary>
    [ObservableProperty]
    private ObservableCollection<LetterRailItem> _letterRailItems = new();

    // === 颜色绑定 ===
    [ObservableProperty] private Color _gridButtonColor;
    [ObservableProperty] private Color _listButtonColor;

    // === 静态数据 ===
    private static readonly Color AccentColor = Color.FromArgb("#8C7BFF");
    private static readonly Color TransparentColor = Colors.Transparent;

    /// <summary>
    /// 初始化 <see cref="ArtistsViewModel"/> 实例。
    /// </summary>
    /// <param name="exploreData">探索页数据服务，用于读取艺术家聚合数据</param>
    public ArtistsViewModel(ExploreDataService exploreData)
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
            new("songs", "歌曲数", false),
            new("plays", "最常听", false),
            new("name", "名称 A-Z", false),
        };
    }

    /// <summary>异步加载所有艺术家</summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            IsLoading = true;
            StatusText = "正在加载艺术家...";

            var artists = await _exploreData.GetAllArtistsAsync();
            _allArtists = artists;

            // 批量解析封面
            await Task.Run(() =>
            {
                var pending = new Dictionary<int, Song>();
                foreach (var artist in _allArtists)
                {
                    if (artist.SampleSongId > 0)
                    {
                        var cachedPath = Services.CoverHelper.GetCachedPath(artist.SampleSongId);
                        if (File.Exists(cachedPath))
                        {
                            artist.Cover = cachedPath;
                            continue;
                        }
                    }

                    if (artist.SampleSongId > 0 && !string.IsNullOrEmpty(artist.SampleFilePath)
                        && !pending.ContainsKey(artist.SampleSongId))
                    {
                        pending[artist.SampleSongId] = new Song { Id = artist.SampleSongId, FilePath = artist.SampleFilePath };
                    }
                }

                if (pending.Count > 0)
                {
                    Services.CoverHelper.BatchResolveCovers(pending.Values);
                    foreach (var artist in _allArtists)
                    {
                        if (string.IsNullOrEmpty(artist.Cover) && artist.SampleSongId > 0
                            && pending.TryGetValue(artist.SampleSongId, out var s)
                            && !string.IsNullOrEmpty(s.CoverArtPath))
                        {
                            artist.Cover = s.CoverArtPath;
                        }
                    }
                }
            });

            // 更新统计
            TotalArtists = _allArtists.Count;

            // 设置最常聆听
            SetMostPlayed();

            // 构建字母 rail
            BuildLetterRail();

            // 应用筛选和排序
            ApplyFiltersAndSort();

            StatusText = $"共 {TotalArtists} 位艺术家";
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

    /// <summary>设置最常聆听艺术家</summary>
    private void SetMostPlayed()
    {
        // 根据歌曲数和采样封面选择最常聆听的艺术家
        var top = _allArtists
            .Where(a => a.SongCount > 0)
            .OrderByDescending(a => a.SongCount)
            .FirstOrDefault();

        if (top != null)
        {
            MostPlayedArtist = top;
            MostPlayedName = top.Name;
            MostPlayedSubInfo = $"{top.SongCount} 首";
            MostPlayedInitial = string.IsNullOrEmpty(top.Name) ? "♪" : top.Name.Trim()[0].ToString().ToUpper();
            HasMostPlayed = true;
            MostPlayedPlaceholderColor = GetArtistPlaceholderColor(top.Id);

            if (!string.IsNullOrEmpty(top.Cover) && File.Exists(top.Cover))
            {
                MostPlayedCover = ImageSource.FromFile(top.Cover);
                MostPlayedHasCover = true;
            }
            else
            {
                MostPlayedHasCover = false;
            }
        }
        else
        {
            HasMostPlayed = false;
        }
    }

    /// <summary>构建字母 rail 数据</summary>
    private void BuildLetterRail()
    {
        var letters = _allArtists
            .Select(a => GetIndexLetter(a.Name))
            .Distinct()
            .OrderBy(l => l)
            .ToList();

        var items = new ObservableCollection<LetterRailItem>();
        foreach (var letter in letters)
        {
            items.Add(new LetterRailItem(letter, letter == CurrentLetter));
        }

        LetterRailItems = items;
    }

    /// <summary>获取艺术家名称的索引字母（中文取首字符，英文取首字母）</summary>
    private static string GetIndexLetter(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "#";
        var c = name.Trim().ToUpperInvariant()[0];
        if (c >= 'A' && c <= 'Z') return c.ToString();
        // 简单判断中文字符
        if (c >= 0x4E00 && c <= 0x9FFF) return "中";
        return "#";
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
        IEnumerable<ArtistWithCount> result = _allArtists;

        // 1. 来源筛选
        result = CurrentFilter switch
        {
            "local" => result.Where(a => GetArtistSource(a) == "本地"),
            "network" => result.Where(a => GetArtistSource(a) == "网络"),
            _ => result
        };

        // 2. 字母筛选
        if (!string.IsNullOrEmpty(CurrentLetter))
        {
            result = result.Where(a => GetIndexLetter(a.Name) == CurrentLetter);
        }

        // 3. 搜索筛选
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            var q = SearchQuery.ToLowerInvariant();
            result = result.Where(a =>
                (a.Name?.ToLowerInvariant().Contains(q) ?? false));
        }

        // 4. 排序
        result = CurrentSort switch
        {
            "songs" => result.OrderByDescending(a => a.SongCount),
            "plays" => result.OrderByDescending(a => 0), // TODO: 添加播放次数
            "name" => result.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => result.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
        };

        FilteredArtists = new ObservableCollection<ArtistWithCount>(result.ToList());
    }

    /// <summary>获取艺术家来源类型</summary>
    private static string GetArtistSource(ArtistWithCount artist)
    {
        if (!string.IsNullOrEmpty(artist.SampleFilePath))
        {
            if (artist.SampleFilePath.StartsWith("content://") ||
                artist.SampleFilePath.StartsWith("file://") ||
                (!artist.SampleFilePath.StartsWith("http") && !artist.SampleFilePath.StartsWith("smb://")))
            {
                return "本地";
            }
        }
        return "网络";
    }

    /// <summary>获取艺术家占位颜色</summary>
    private static Color GetArtistPlaceholderColor(int id)
    {
        var palettes = new[]
        {
            "#8C7BFF", "#FF7AAE", "#55D6FF", "#A78BFA",
            "#5EEAD4", "#FBBF24", "#818CF8", "#F472B6"
        };
        return Color.FromArgb(palettes[Math.Abs(id) % palettes.Length]);
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

        public Color TextColor => IsActive ? Color.FromArgb("#EAF0FF") : Color.FromArgb("#7A85B0");
        public Color BackgroundColor => IsActive ? Color.FromArgb("#33FFFFFF") : Color.FromArgb("#22FFFFFF");
        public Color BorderColor => IsActive ? AccentColor : TransparentColor;
    }

    /// <summary>字母 rail 选项模型</summary>
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
    }
}

/// <summary>艺术家显示扩展</summary>
public static class ArtistDisplayExtensions
{
    private static readonly string[] Palettes = {
        "#8C7BFF,#55D6FF", "#FF7AAE,#FFB36B", "#55D6FF,#7AF0C8", "#A78BFA,#F0ABFC",
        "#5EEAD4,#60A5FA", "#FBBF24,#FB7185", "#818CF8,#22D3EE", "#F472B6,#C084FC"
    };

    public static string GetInitial(this ArtistWithCount artist) =>
        string.IsNullOrEmpty(artist.Name) ? "♪" : artist.Name.Trim()[0].ToString().ToUpper();

    public static Color GetPlaceholderColor(this ArtistWithCount artist)
    {
        var index = Math.Abs(artist.Id) % Palettes.Length;
        var colors = Palettes[index].Split(',');
        return Color.FromArgb(colors[0]);
    }

    public static string GetSubInfo(this ArtistWithCount artist) =>
        $"{artist.SongCount} 首";
}
