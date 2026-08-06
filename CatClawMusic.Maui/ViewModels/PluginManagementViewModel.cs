using System.Collections.ObjectModel;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>
/// 插件管理页 ViewModel：展示已安装插件列表、启用/禁用开关、状态标签，并支持从本地文件添加插件。
/// （在线插件商店已于 2026-08-06 移除：多市场源/联网拉取在国内网络不可靠，改为本地 .dll/.ccp 导入。）
/// </summary>
public partial class PluginManagementViewModel : ObservableObject
{
    private readonly IPluginManager _pluginManager;

    /// <summary>插件项展示列表（已安装）</summary>
    public ObservableCollection<PluginItemView> Plugins { get; } = new();

    /// <summary>是否正在刷新插件列表</summary>
    [ObservableProperty]
    private bool _isRefreshing;

    /// <summary>插件汇总文本（如"共 N 个插件，已启用 M 个"）</summary>
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

    /// <summary>从本地文件添加插件（.dll / .ccp），安装后刷新列表</summary>
    [RelayCommand]
    public async Task AddPluginAsync()
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "选择插件文件",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    [DevicePlatform.WinUI] = new[] { ".dll", ".ccp" },
                    [DevicePlatform.Android] = new[] { "application/octet-stream", "application/x-msdownload", "*/*" },
                }),
            });
            if (result == null) return;

            var progress = new Progress<(string, int)>(_ => { /* 无进度 UI，忽略 */ });
            var info = await _pluginManager.InstallFromLocalFileAsync(result.FullPath, progress);
            if (info != null)
            {
                await RefreshAsync();
                await ShowAlertAsync("插件", $"已安装「{info.DisplayName}」v{info.Version}");
            }
            else
            {
                await ShowAlertAsync("插件", "安装失败：插件文件无效或格式不受支持（仅支持 .dll / .ccp）");
            }
        }
        catch (Exception ex)
        {
            Log.Debug("PluginManagementViewModel", $"[PluginManagement] 添加插件失败: {ex.Message}");
            await ShowAlertAsync("插件", $"添加失败：{ex.Message}");
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

    private static Page? CurrentPage()
    {
        try { return Application.Current?.Windows.FirstOrDefault()?.Page; } catch { return null; }
    }

    private async Task ShowAlertAsync(string title, string message)
    {
        try
        {
            var page = CurrentPage();
            if (page != null) await page.DisplayAlertAsync(title, message, "确定");
        }
        catch { }
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
