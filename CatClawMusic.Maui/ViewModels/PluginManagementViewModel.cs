using System.Collections.ObjectModel;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Maui.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>
/// 插件管理页 ViewModel：展示插件列表、启用/禁用开关、状态标签、基础信息，以及「插件商店」在线安装。
/// </summary>
public partial class PluginManagementViewModel : ObservableObject
{
    private readonly IPluginManager _pluginManager;
    private readonly PluginStoreService _storeService;

    /// <summary>插件项展示列表</summary>
    public ObservableCollection<PluginItemView> Plugins { get; } = new();

    /// <summary>插件商店可用插件列表</summary>
    public ObservableCollection<StoreItemView> StoreItems { get; } = new();

    /// <summary>是否正在刷新插件列表</summary>
    [ObservableProperty]
    private bool _isRefreshing;

    /// <summary>是否正在加载插件商店</summary>
    [ObservableProperty]
    private bool _isLoadingStore;

    /// <summary>插件商店加载状态文本</summary>
    [ObservableProperty]
    private string _storeStatus = "";

    /// <summary>插件汇总文本（如“共 N 个插件，已启用 M 个”）</summary>
    [ObservableProperty]
    private string _summary = "加载中...";

    /// <summary>
    /// 初始化 <see cref="PluginManagementViewModel"/> 实例。
    /// </summary>
    /// <param name="pluginManager">插件管理器，用于读取与切换插件状态</param>
    /// <param name="storeService">插件商店服务，用于拉取在线插件清单</param>
    public PluginManagementViewModel(IPluginManager pluginManager, PluginStoreService storeService)
    {
        _pluginManager = pluginManager;
        _storeService = storeService;
    }

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

            await Task.CompletedTask;
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

    /// <summary>从 GitHub 商店清单加载可用插件</summary>
    [RelayCommand]
    public async Task LoadStoreAsync()
    {
        if (IsLoadingStore) return;
        IsLoadingStore = true;
        StoreStatus = "正在加载插件商店...";
        try
        {
            var items = await _storeService.FetchAsync();
            StoreItems.Clear();
            foreach (var item in items)
                StoreItems.Add(new StoreItemView(item, false));
            RefreshStoreInstalledStates();
            StoreStatus = items.Count > 0 ? $"商店共 {items.Count} 个插件" : "商店暂无可安装插件";
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

    /// <summary>从插件商店安装指定插件：下载 .dll → 安装 → 刷新列表</summary>
    [RelayCommand]
    public async Task InstallFromStoreAsync(StoreItemView? item)
    {
        if (item == null || item.IsInstalled || string.IsNullOrWhiteSpace(item.InstallUrl)) return;
        item.IsInstalling = true;
        item.ProgressText = "准备安装...";
        try
        {
            var progress = new Progress<(string, int)>(t =>
            {
                item.ProgressText = t.Item1;
                item.Progress = t.Item2 / 100.0; // MAUI ProgressBar 范围为 0-1
            });

            var dllPath = await _storeService.DownloadPluginAsync(item.InstallUrl, progress);
            item.ProgressText = "正在安装...";
            var info = await _pluginManager.InstallFromLocalFileAsync(dllPath, progress);
            try { if (File.Exists(dllPath)) File.Delete(dllPath); } catch { }

            if (info != null)
            {
                // 同一 .dll 可能含多个插件（如 OnlineMusic 含 5 源），安装后同步全部条目为已安装，防重复下载
                RefreshStoreInstalledStates();
                item.ProgressText = "已安装";
                await RefreshAsync();
            }
            else
            {
                item.ProgressText = "安装失败";
            }
        }
        catch (Exception ex)
        {
            item.ProgressText = $"安装失败：{ex.Message}";
            Log.Debug("PluginManagementViewModel", $"[PluginManagement] 商店安装失败: {ex.Message}");
        }
        finally
        {
            item.IsInstalling = false;
        }
    }

    /// <summary>按当前已安装插件同步商店条目的"已安装"状态（同一 .dll 可含多插件）</summary>
    private void RefreshStoreInstalledStates()
    {
        var all = _pluginManager.GetAllPlugins();
        foreach (var s in StoreItems)
            s.IsInstalled = all.Any(p => string.Equals(p.PluginTypeId, s.Id, StringComparison.OrdinalIgnoreCase));
    }

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
}

/// <summary>插件商店展示项（含安装状态与进度）</summary>
public partial class StoreItemView : ObservableObject
{
    /// <summary>商店条目原始数据</summary>
    public PluginStoreItem Item { get; }

    public string Id => Item.Id;
    public string Name => Item.Name;
    public string Version => Item.Version;
    public string Author => Item.Author;
    public string Description => Item.Description;
    public string Category => Item.Category;
    public string Icon => Item.Icon;
    public string InstallUrl => Item.InstallUrl;

    /// <summary>是否已安装</summary>
    [ObservableProperty]
    private bool _isInstalled;

    /// <summary>是否正在安装</summary>
    [ObservableProperty]
    private bool _isInstalling;

    /// <summary>安装进度（0-1，MAUI ProgressBar 范围）</summary>
    [ObservableProperty]
    private double _progress;

    /// <summary>安装进度文本</summary>
    [ObservableProperty]
    private string _progressText = "";

    public StoreItemView(PluginStoreItem item, bool isInstalled)
    {
        Item = item;
        IsInstalled = isInstalled;
    }
}

/// <summary>插件展示项</summary>
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
    /// <summary>插件分类展示文本（歌词/协议/封面/音效/菜单/其他）</summary>
    public string CategoryText => Info.Category switch
    {
        PluginCategory.LyricsProvider => "歌词",
        PluginCategory.ProtocolProvider => "协议",
        PluginCategory.CoverProvider => "封面",
        PluginCategory.AudioEnhancer => "音效",
        PluginCategory.MenuContributor => "菜单",
        _ => "其他"
    };
    /// <summary>插件图标 Emoji（缺省为 🧩）</summary>
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

    /// <summary>
    /// 初始化 <see cref="PluginItemView"/> 实例。
    /// </summary>
    /// <param name="info">插件元数据</param>
    /// <param name="isEnabled">是否已启用</param>
    public PluginItemView(PluginInfo info, bool isEnabled)
    {
        Info = info;
        IsEnabled = isEnabled;
    }
}
