using System.Collections.ObjectModel;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Maui.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>
/// 插件管理页 ViewModel：插件列表（启用/禁用）、插件商店（搜索/分类/安装/更新/卸载/兼容性检查/多市场源）。
/// 参考 AstrBot 插件市场：索引 JSON 多源订阅 + 版本兼容声明 + 安装/更新/卸载闭环。
/// </summary>
public partial class PluginManagementViewModel : ObservableObject
{
    private readonly IPluginManager _pluginManager;
    private readonly PluginStoreService _storeService;

    /// <summary>插件项展示列表（已安装）</summary>
    public ObservableCollection<PluginItemView> Plugins { get; } = new();

    /// <summary>商店全量条目（合并多源、按 id 去重取最高版本）</summary>
    public ObservableCollection<StoreItemView> StoreItems { get; } = new();

    /// <summary>商店过滤后的条目（搜索 + 分类）</summary>
    public ObservableCollection<StoreItemView> FilteredStoreItems { get; } = new();

    /// <summary>分类筛选 chips（首项"全部"；选中态驱动 UI 高亮）</summary>
    public ObservableCollection<CategoryChipView> CategoryChips { get; } = new();

    /// <summary>自定义市场源列表</summary>
    public ObservableCollection<string> CustomSources { get; } = new();

    /// <summary>是否正在刷新插件列表</summary>
    [ObservableProperty]
    private bool _isRefreshing;

    /// <summary>是否正在加载插件商店</summary>
    [ObservableProperty]
    private bool _isLoadingStore;

    /// <summary>插件商店加载状态文本</summary>
    [ObservableProperty]
    private string _storeStatus = "";

    /// <summary>搜索关键字</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilter))]
    private string _searchText = "";

    /// <summary>当前选中的分类（"全部" = 不过滤）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilter))]
    private string _selectedCategory = "全部";

    /// <summary>插件汇总文本（如"共 N 个插件，已启用 M 个"）</summary>
    [ObservableProperty]
    private string _summary = "加载中...";

    /// <summary>是否有搜索或分类筛选生效（用于显示"清除筛选"）</summary>
    public bool HasActiveFilter => !string.IsNullOrWhiteSpace(SearchText) || SelectedCategory != "全部";

    /// <summary>
    /// 初始化 <see cref="PluginManagementViewModel"/> 实例。
    /// </summary>
    /// <param name="pluginManager">插件管理器</param>
    /// <param name="storeService">插件商店服务</param>
    public PluginManagementViewModel(IPluginManager pluginManager, PluginStoreService storeService)
    {
        _pluginManager = pluginManager;
        _storeService = storeService;
        foreach (var s in _storeService.GetCustomSources())
            CustomSources.Add(s);
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedCategoryChanged(string value) => ApplyFilter();

    /// <summary>页面出现时刷新插件列表与商店</summary>
    public async Task OnAppearingAsync()
    {
        await RefreshAsync();
        await LoadStoreAsync();
    }

    /// <summary>刷新插件列表</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsRefreshing) return;
        IsRefreshing = true;
        try
        {
            Plugins.Clear();
            var list = _pluginManager.GetAllPlugins();
            foreach (var p in list)
                Plugins.Add(new PluginItemView(p, _pluginManager.IsPluginEnabled(p.PluginTypeId)));

            var enabled = Plugins.Count(x => x.IsEnabled);
            Summary = Plugins.Count > 0
                ? $"共 {Plugins.Count} 个插件，已启用 {enabled} 个"
                : "当前没有可用插件";
        }
        catch (Exception ex)
        {
            Summary = $"加载失败：{ex.Message}";
            Log.Debug("PluginManagementViewModel", $"[PluginManagement] Refresh 失败: {ex.Message}");
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    /// <summary>从全部市场源加载插件商店（合并去重），并重建分类选项与过滤结果</summary>
    [RelayCommand]
    public async Task LoadStoreAsync()
    {
        if (IsLoadingStore) return;
        IsLoadingStore = true;
        StoreStatus = "正在加载插件商店...";
        try
        {
            var items = await _storeService.FetchAllAsync();
            StoreItems.Clear();
            foreach (var item in items)
                StoreItems.Add(new StoreItemView(item, false));
            RefreshStoreInstalledStates();

            // 重建分类 chips（保留当前选中，若已不存在则回"全部"）
            var cats = items.Select(x => x.Category).Where(c => !string.IsNullOrWhiteSpace(c))
                            .Distinct().OrderBy(c => c).ToList();
            var keepSelected = SelectedCategory != "全部" && cats.Contains(SelectedCategory) ? SelectedCategory : "全部";
            CategoryChips.Clear();
            CategoryChips.Add(new CategoryChipView("全部", "全部" == keepSelected));
            foreach (var c in cats)
                CategoryChips.Add(new CategoryChipView(c, string.Equals(c, keepSelected, StringComparison.OrdinalIgnoreCase)));
            SelectedCategory = keepSelected;

            ApplyFilter();
            StoreStatus = items.Count > 0 ? $"商店共 {items.Count} 个插件（{_storeService.GetAllSourceUrls().Length} 个市场源）" : "商店暂无可安装插件";
        }
        catch (Exception ex)
        {
            StoreStatus = $"商店加载失败：{ex.Message}";
            Log.Debug("PluginManagementViewModel", $"[PluginManagement] 商店加载失败: {ex.Message}");
        }
        finally
        {
            IsLoadingStore = false;
        }
    }

    /// <summary>按搜索关键字 + 分类过滤商店条目</summary>
    private void ApplyFilter()
    {
        FilteredStoreItems.Clear();
        var keyword = SearchText?.Trim() ?? string.Empty;
        foreach (var s in StoreItems)
        {
            if (SelectedCategory != "全部" && !string.Equals(s.Category, SelectedCategory, StringComparison.OrdinalIgnoreCase))
                continue;
            if (keyword.Length > 0)
            {
                var hay = $"{s.Name} {s.Author} {s.Description} {s.ShortDescription} {string.Join(" ", s.Item.Tags)}";
                if (hay.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
            }
            FilteredStoreItems.Add(s);
        }
    }

    /// <summary>选择分类</summary>
    [RelayCommand]
    public void SelectCategory(string? category)
    {
        if (string.IsNullOrEmpty(category)) return;
        SelectedCategory = category;
        foreach (var chip in CategoryChips)
            chip.IsSelected = string.Equals(chip.Text, category, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>清除搜索与分类筛选</summary>
    [RelayCommand]
    public void ClearFilter()
    {
        SearchText = string.Empty;
        SelectedCategory = "全部";
    }

    /// <summary>安装：兼容性检查 → 确认 → 下载（稳定文件名）→ sha256 校验 → 安装 → 刷新</summary>
    [RelayCommand]
    public async Task InstallAsync(StoreItemView? item)
    {
        if (item == null || item.IsInstalled || item.IsInstalling || string.IsNullOrWhiteSpace(item.InstallUrl)) return;

        var (ok, msg) = _storeService.CheckCompatibility(item.Item);
        if (!ok && !await ConfirmAsync("插件兼容性提示", $"{msg}。\n\n仍要安装吗？", "无视警告，继续安装", "取消"))
            return;

        await InstallInternalAsync(item);
    }

    /// <summary>更新：卸载旧包（同 dll 全部条目）→ 下载新版 → 校验 → 安装 → 刷新</summary>
    [RelayCommand]
    public async Task UpdateAsync(StoreItemView? item)
    {
        if (item == null || !item.HasUpdate || item.IsInstalling || string.IsNullOrWhiteSpace(item.InstallUrl)) return;

        var (ok, msg) = _storeService.CheckCompatibility(item.Item);
        if (!ok && !await ConfirmAsync("插件兼容性提示", $"{msg}。\n\n仍要更新吗？", "无视警告，继续更新", "取消"))
            return;

        item.IsInstalling = true;
        item.ProgressText = "正在卸载旧版本...";
        try
        {
            await UninstallPackageAsync(item);
            await InstallInternalAsync(item);
        }
        catch (Exception ex)
        {
            item.ProgressText = $"更新失败：{ex.Message}";
            Log.Debug("PluginManagementViewModel", $"[PluginManagement] 更新失败: {ex.Message}");
        }
        finally
        {
            item.IsInstalling = false;
        }
    }

    /// <summary>卸载：按包维度卸载（同一 dll 下所有插件条目一起移除）</summary>
    [RelayCommand]
    public async Task UninstallAsync(StoreItemView? item)
    {
        if (item == null || !item.IsInstalled || item.IsInstalling) return;
        if (!await ConfirmAsync("卸载插件", $"确定卸载「{item.Name}」吗？", "卸载", "取消"))
            return;

        item.IsInstalling = true;
        item.ProgressText = "正在卸载...";
        try
        {
            await UninstallPackageAsync(item);
            RefreshStoreInstalledStates();
            item.ProgressText = "已卸载";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            item.ProgressText = $"卸载失败：{ex.Message}";
            Log.Debug("PluginManagementViewModel", $"[PluginManagement] 卸载失败: {ex.Message}");
        }
        finally
        {
            item.IsInstalling = false;
        }
    }

    /// <summary>核心安装流程（下载 → 校验 → 安装 → 刷新状态）</summary>
    private async Task InstallInternalAsync(StoreItemView item)
    {
        item.ProgressText = "准备安装...";
        var progress = new Progress<(string, int)>(t =>
        {
            item.ProgressText = t.Item1;
            item.Progress = t.Item2 / 100.0;
        });

        var dllPath = await _storeService.DownloadPluginAsync(item.InstallUrl, progress, item.PackageFileName);
        if (!_storeService.VerifyHash(dllPath, item.FileHash))
        {
            try { if (File.Exists(dllPath)) File.Delete(dllPath); } catch { }
            throw new InvalidOperationException("安装包校验失败（sha256 不匹配）");
        }

        item.ProgressText = "正在安装...";
        var info = await _pluginManager.InstallFromLocalFileAsync(dllPath, progress);
        try { if (File.Exists(dllPath)) File.Delete(dllPath); } catch { }

        if (info != null)
        {
            RefreshStoreInstalledStates();
            item.ProgressText = "已安装";
            await RefreshAsync();
        }
        else
        {
            item.ProgressText = "安装失败";
        }
    }

    /// <summary>卸载同一 dll 包下所有插件条目（同 dll 多插件场景，如 OnlineMusic 含 5 源）</summary>
    private async Task UninstallPackageAsync(StoreItemView item)
    {
        var dll = item.AssemblyPathOfPackage;
        var targets = _pluginManager.GetAllPlugins()
            .Where(p => p.CanUninstall
                        && (string.IsNullOrEmpty(dll) || string.Equals(p.AssemblyPath, dll, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        // 若未定位到 dll（如包内插件各自独立 dll），至少卸载与商店条目同 id 的插件
        if (targets.Count == 0)
        {
            var byId = _pluginManager.GetAllPlugins()
                .FirstOrDefault(p => string.Equals(p.PluginTypeId, item.Id, StringComparison.OrdinalIgnoreCase));
            if (byId != null) targets.Add(byId);
        }
        foreach (var p in targets)
            await _pluginManager.UninstallPluginAsync(p.PluginTypeId);
    }

    /// <summary>按已安装插件同步商店条目的安装状态与可更新状态（同一 dll 可含多插件）</summary>
    private void RefreshStoreInstalledStates()
    {
        var all = _pluginManager.GetAllPlugins();
        foreach (var s in StoreItems)
        {
            var matched = all.Where(p => string.Equals(p.PluginTypeId, s.Id, StringComparison.OrdinalIgnoreCase)).ToList();
            s.IsInstalled = matched.Count > 0;
            s.InstalledVersion = matched.FirstOrDefault()?.Version ?? string.Empty;
            s.AssemblyPathOfPackage = matched.FirstOrDefault()?.AssemblyPath;
            s.HasUpdate = s.IsInstalled
                          && CompareVersions(s.Version, s.InstalledVersion) > 0
                          && !string.IsNullOrWhiteSpace(s.InstallUrl);
        }
    }

    private static int CompareVersions(string? a, string? b)
    {
        if (Version.TryParse(a?.TrimStart('v', 'V'), out var va)
            && Version.TryParse(b?.TrimStart('v', 'V'), out var vb))
            return va.CompareTo(vb);
        return string.CompareOrdinal(a ?? string.Empty, b ?? string.Empty);
    }

    // ═══════════════════════════════════════
    // 市场源管理
    // ═══════════════════════════════════════

    /// <summary>添加自定义市场源（GitHub raw / 任意 JSON 清单 URL）</summary>
    [RelayCommand]
    public async Task AddSourceAsync(string? url = null)
    {
        if (string.IsNullOrWhiteSpace(url))
            url = await PromptAsync("添加市场源", "输入插件商店清单 URL（如 https://raw.githubusercontent.com/.../plugins.json）", "添加");
        if (string.IsNullOrWhiteSpace(url)) return;

        url = url.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            await ConfirmAsync("无效的地址", "请输入完整的 http(s) 地址", "知道了", "取消");
            return;
        }
        if (!CustomSources.Contains(url))
        {
            CustomSources.Add(url);
            _storeService.SaveCustomSources(CustomSources);
            await LoadStoreAsync();
        }
    }

    /// <summary>移除自定义市场源</summary>
    [RelayCommand]
    public void RemoveSource(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        CustomSources.Remove(url);
        _storeService.SaveCustomSources(CustomSources);
        _ = LoadStoreAsync();
    }

    // ═══════════════════════════════════════
    // 已安装插件启用/禁用
    // ═══════════════════════════════════════

    /// <summary>切换插件启用状态</summary>
    [RelayCommand]
    public void ToggleEnabled(PluginItemView? item)
    {
        if (item == null) return;
        try
        {
            var newState = !item.IsEnabled;
            _pluginManager.SetPluginEnabled(item.PluginTypeId, newState);
            item.IsEnabled = newState;
            UpdateSummary();
        }
        catch (Exception ex)
        {
            Log.Debug("PluginManagementViewModel", $"[PluginManagement] Toggle 失败: {ex.Message}");
        }
    }

    private void UpdateSummary()
    {
        var enabled = Plugins.Count(x => x.IsEnabled);
        Summary = Plugins.Count > 0
            ? $"共 {Plugins.Count} 个插件，已启用 {enabled} 个"
            : "当前没有可用插件";
    }

    // ═══════════════════════════════════════
    // 对话框辅助（兼容 Windows/Android 的 Page 入口）
    // ═══════════════════════════════════════

    private static Page? CurrentPage()
    {
        try { return Application.Current?.Windows.FirstOrDefault()?.Page; } catch { return null; }
    }

    private async Task<bool> ConfirmAsync(string title, string message, string accept, string cancel)
    {
        try
        {
            var page = CurrentPage();
            if (page == null) return true;
            return await page.DisplayAlertAsync(title, message, accept, cancel);
        }
        catch { return true; }
    }

    private async Task<string?> PromptAsync(string title, string message, string accept)
    {
        try
        {
            var page = CurrentPage();
            if (page == null) return null;
            return await page.DisplayPromptAsync(title, message, accept, "取消");
        }
        catch { return null; }
    }
}

/// <summary>插件商店展示项（含安装状态、可更新状态、兼容性、进度）</summary>
public partial class StoreItemView : ObservableObject
{
    /// <summary>商店条目原始数据</summary>
    public PluginStoreItem Item { get; }

    public string Id => Item.Id;
    public string Name => Item.Name;
    public string Version => Item.Version;
    public string Author => Item.Author;
    public string Description => Item.Description;
    /// <summary>卡片短描述（缺省回退完整描述）</summary>
    public string ShortDescription => string.IsNullOrWhiteSpace(Item.ShortDescription) ? Item.Description : Item.ShortDescription;
    public string Category => Item.Category;
    public string Icon => Item.Icon;
    public string LogoUrl => Item.LogoUrl;
    /// <summary>标签展示文本（空格分隔）</summary>
    public string TagsText => Item.Tags != null ? string.Join(" ", Item.Tags) : string.Empty;
    public string InstallUrl => Item.InstallUrl;
    public string FileHash => Item.FileHash;
    public string SourceName => Item.SourceName;
    /// <summary>稳定包文件名（用于覆盖安装）</summary>
    public string PackageFileName => Item.PackageFileName;

    /// <summary>是否已安装</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowInstallButton))]
    [NotifyPropertyChangedFor(nameof(ShowInstalledLabel))]
    [NotifyPropertyChangedFor(nameof(ShowUninstallButton))]
    private bool _isInstalled;

    /// <summary>已安装版本（空 = 未安装）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    [NotifyPropertyChangedFor(nameof(StateColor))]
    private string _installedVersion = string.Empty;

    /// <summary>是否有可更新版本（商店版本 &gt; 已装版本）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    [NotifyPropertyChangedFor(nameof(StateColor))]
    [NotifyPropertyChangedFor(nameof(ShowUpdateButton))]
    [NotifyPropertyChangedFor(nameof(ShowInstalledLabel))]
    private bool _hasUpdate;

    /// <summary>包所在 dll 路径（用于包维度卸载/更新）</summary>
    [ObservableProperty]
    private string? _assemblyPathOfPackage;

    /// <summary>是否正在安装/更新/卸载</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowInstallButton))]
    [NotifyPropertyChangedFor(nameof(ShowUpdateButton))]
    [NotifyPropertyChangedFor(nameof(ShowInstalledLabel))]
    [NotifyPropertyChangedFor(nameof(ShowUninstallButton))]
    private bool _isInstalling;

    /// <summary>安装进度（0-1）</summary>
    [ObservableProperty]
    private double _progress;

    /// <summary>安装进度文本</summary>
    [ObservableProperty]
    private string _progressText = "";

    [ObservableProperty]
    private string _compatibilityHint = "";

    /// <summary>显示"安装"按钮（未安装且不在安装中）</summary>
    public bool ShowInstallButton => !IsInstalled && !IsInstalling;
    /// <summary>显示"更新"按钮（已安装且有新版本且不在操作中）</summary>
    public bool ShowUpdateButton => IsInstalled && HasUpdate && !IsInstalling;
    /// <summary>显示"已安装"状态标签（已安装且无更新且不在操作中）</summary>
    public bool ShowInstalledLabel => IsInstalled && !HasUpdate && !IsInstalling;
    /// <summary>显示"卸载"按钮（已安装且不在操作中）</summary>
    public bool ShowUninstallButton => IsInstalled && !IsInstalling;

    /// <summary>状态文本：可更新（vN→vM）/ 已安装 / 安装</summary>
    public string StateText => HasUpdate ? $"可更新 v{InstalledVersion}→v{Version}" : (IsInstalled ? "已安装" : "安装");
    /// <summary>状态颜色：可更新青 / 已安装绿 / 默认主色</summary>
    public string StateColor => HasUpdate ? "#55D6FF" : (IsInstalled ? "#4CAF50" : "#8C7BFF");

    public StoreItemView(PluginStoreItem item, bool isInstalled)
    {
        Item = item;
        IsInstalled = isInstalled;
    }
}

/// <summary>插件展示项（已安装列表）</summary>
public partial class PluginItemView : ObservableObject
{
    /// <summary>插件元数据信息</summary>
    public PluginInfo Info { get; }

    /// <summary>插件类型 ID</summary>
    public string PluginTypeId => Info.PluginTypeId;
    /// <summary>展示名称</summary>
    public string DisplayName => Info.DisplayName;
    /// <summary>插件版本</summary>
    public string Version => Info.Version;
    /// <summary>插件作者</summary>
    public string Author => Info.Author;
    /// <summary>插件描述（优先使用元数据描述，回退到插件实例描述）</summary>
    public string Description => string.IsNullOrWhiteSpace(Info.Description) ? Info.Plugin.Description : Info.Description;
    /// <summary>插件分类展示文本</summary>
    public string CategoryText => Info.Category switch
    {
        PluginCategory.LyricsProvider => "歌词",
        PluginCategory.ProtocolProvider => "协议",
        PluginCategory.CoverProvider => "封面",
        PluginCategory.AudioEnhancer => "音效",
        PluginCategory.MenuContributor => "菜单",
        PluginCategory.OnlineMusic => "在线音源",
        _ => "其他"
    };
    /// <summary>插件图标 Emoji（缺省 🧩）</summary>
    public string IconEmoji => string.IsNullOrWhiteSpace(Info.IconEmoji) ? "🧩" : Info.IconEmoji;
    /// <summary>插件来源展示文本（内置 / 已安装）</summary>
    public string SourceText => Info.Source == PluginSource.BuiltIn ? "内置" : "已安装";

    /// <summary>该插件是否已启用</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(StatusColor))]
    private bool _isEnabled;

    /// <summary>状态展示文本（已启用 / 已禁用）</summary>
    public string StatusText => IsEnabled ? "已启用" : "已禁用";
    /// <summary>状态展示颜色（已启用绿色 / 已禁用灰色）</summary>
    public string StatusColor => IsEnabled ? "#4CAF50" : "#9E9E9E";

    public PluginItemView(PluginInfo info, bool isEnabled)
    {
        Info = info;
        IsEnabled = isEnabled;
    }
}

/// <summary>分类筛选 chip（选中态驱动高亮，样式与排序/筛选 chips 一致）</summary>
public partial class CategoryChipView : ObservableObject
{
    /// <summary>分类名称</summary>
    public string Text { get; }

    /// <summary>是否选中</summary>
    [ObservableProperty]
    private bool _isSelected;

    public CategoryChipView(string text, bool isSelected)
    {
        Text = text;
        IsSelected = isSelected;
    }
}
