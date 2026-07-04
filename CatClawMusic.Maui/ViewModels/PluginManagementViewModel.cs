using System.Collections.ObjectModel;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>
/// 插件管理页 ViewModel：展示插件列表、启用/禁用开关、状态标签、基础信息。
/// </summary>
public partial class PluginManagementViewModel : ObservableObject
{
    private readonly IPluginManager _pluginManager;

    /// <summary>插件项展示列表</summary>
    public ObservableCollection<PluginItemView> Plugins { get; } = new();

    /// <summary>是否正在刷新插件列表</summary>
    [ObservableProperty]
    private bool _isRefreshing;

    /// <summary>插件汇总文本（如“共 N 个插件，已启用 M 个”）</summary>
    [ObservableProperty]
    private string _summary = "加载中...";

    /// <summary>
    /// 初始化 <see cref="PluginManagementViewModel"/> 实例。
    /// </summary>
    /// <param name="pluginManager">插件管理器，用于读取与切换插件状态</param>
    public PluginManagementViewModel(IPluginManager pluginManager)
    {
        _pluginManager = pluginManager;
    }

    /// <summary>页面出现时刷新插件列表</summary>
    public async Task OnAppearingAsync()
    {
        await RefreshAsync();
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
            System.Diagnostics.Debug.WriteLine($"[PluginManagement] Refresh 失败: {ex.Message}");
        }
        finally
        {
            IsRefreshing = false;
        }
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
            System.Diagnostics.Debug.WriteLine($"[PluginManagement] Toggle 失败: {ex.Message}");
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
