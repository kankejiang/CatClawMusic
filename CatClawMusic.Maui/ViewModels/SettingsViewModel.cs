using CatClawMusic.Data;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>
/// 设置页 ViewModel：聚合展示本地音乐、远程音乐、插件、AI、权限、更新等模块状态，
/// 提供深色模式切换与各子页面导航入口。
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly MusicDatabase _db;
    private readonly IThemeService _themeService;
    private readonly IPermissionService _permissionService;
    private readonly IPluginManager _pluginManager;
    private readonly IAgentService _agentService;
    private readonly IUpdateService _updateService;

    /// <summary>深色模式图标（跟随系统/浅色/深色）</summary>
    [ObservableProperty]
    private string _darkModeIcon = "ic_system_mode";

    /// <summary>本地音乐状态文本</summary>
    [ObservableProperty]
    private string _localMusicStatus = "加载中...";

    /// <summary>远程音乐状态文本</summary>
    [ObservableProperty]
    private string _remoteMusicStatus = "加载中...";

    /// <summary>插件状态文本</summary>
    [ObservableProperty]
    private string _pluginStatus = "加载中...";

    /// <summary>AI 助手状态文本</summary>
    [ObservableProperty]
    private string _aiStatus = "未配置";

    /// <summary>权限状态文本</summary>
    [ObservableProperty]
    private string _permissionStatus = "检测中...";

    /// <summary>是否存在可用更新</summary>
    [ObservableProperty]
    private bool _isUpdateAvailable;

    /// <summary>权限状态展示颜色（绿色/橙色/红色）</summary>
    [ObservableProperty]
    private string _permissionStatusColor = "#4CAF50";

    // === Commands ===

    /// <summary>切换深色模式命令（在 跟随系统 → 浅色 → 深色 之间循环）</summary>
    public IRelayCommand ToggleDarkModeCommand { get; }
    /// <summary>加载各模块状态命令</summary>
    public IAsyncRelayCommand LoadStatusCommand { get; }

    /// <summary>请求导航到指定子页面时触发，供页面订阅</summary>
    public event EventHandler<string>? NavigationRequested;

    /// <summary>
    /// 初始化 <see cref="SettingsViewModel"/> 实例，创建交互命令并同步深色模式图标。
    /// </summary>
    /// <param name="db">音乐数据库访问对象</param>
    /// <param name="themeService">主题服务，用于切换深浅色模式</param>
    /// <param name="permissionService">权限服务，用于检测存储/悬浮窗等权限</param>
    /// <param name="pluginManager">插件管理器，用于读取插件状态</param>
    /// <param name="agentService">Agent 服务，用于读取当前 AI 助手配置</param>
    /// <param name="updateService">更新服务，用于检查可用更新</param>
    public SettingsViewModel(
        MusicDatabase db,
        IThemeService themeService,
        IPermissionService permissionService,
        IPluginManager pluginManager,
        IAgentService agentService,
        IUpdateService updateService)
    {
        _db = db;
        _themeService = themeService;
        _permissionService = permissionService;
        _pluginManager = pluginManager;
        _agentService = agentService;
        _updateService = updateService;

        ToggleDarkModeCommand = new RelayCommand(ToggleDarkMode);
        LoadStatusCommand = new AsyncRelayCommand(LoadStatusAsync);

        SyncDarkModeIcon();
    }

    /// <summary>切换深色模式设置（跟随系统 → 浅色 → 深色 → 跟随系统）</summary>
    private void ToggleDarkMode()
    {
        var next = _themeService.DarkModeSetting switch
        {
            DarkModeSetting.FollowSystem => DarkModeSetting.Light,
            DarkModeSetting.Light => DarkModeSetting.Dark,
            _ => DarkModeSetting.FollowSystem
        };

        _themeService.SetDarkModeSetting(next);
        _themeService.ApplyTheme();
        SyncDarkModeIcon();
    }

    /// <summary>
    /// 异步加载各模块状态：本地音乐数量、网络缓存数量、插件启用情况、AI 配置、
    /// 权限状态以及是否存在可用更新。
    /// </summary>
    public async Task LoadStatusAsync()
    {
        try
        {
            await _db.EnsureInitializedAsync();

            // Load local music status
            var localSongCount = await _db.GetLocalSongCountAsync();
            LocalMusicStatus = localSongCount > 0
                ? $"已添加音乐 | 共{localSongCount}首歌曲"
                : "尚未添加文件夹";

            var networkSongCount = (await _db.GetCachedNetworkSongsAsync()).Count;
            RemoteMusicStatus = networkSongCount > 0
                ? $"已缓存 {networkSongCount} 首网络歌曲"
                : "尚未缓存远程音乐";

            var plugins = _pluginManager.GetAllPlugins();
            var enabledPlugins = plugins.Count(p => _pluginManager.IsPluginEnabled(p.PluginTypeId));
            PluginStatus = plugins.Count > 0
                ? $"已启用 {enabledPlugins}/{plugins.Count} 个插件"
                : "当前没有可用插件";

            var currentAgent = _agentService.GetCurrentAgent();
            AiStatus = _agentService.IsConfigured
                ? $"已连接 {currentAgent.Name}"
                : "AI 助手未配置";

            await LoadPermissionStatusAsync();

            var pendingVersion = _updateService.GetPendingVersion();
            IsUpdateAvailable = !string.IsNullOrWhiteSpace(pendingVersion);
            SyncDarkModeIcon();
        }
        catch (Exception ex)
        {
            Log.Debug("SettingsViewModel", $"[SettingsViewModel] 加载状态失败: {ex.Message}");
            LocalMusicStatus = "状态加载失败";
            RemoteMusicStatus = "状态加载失败";
            PluginStatus = "状态加载失败";
            AiStatus = "状态加载失败";
            PermissionStatus = "状态加载失败";
            PermissionStatusColor = "#F44336";
        }
    }

    /// <summary>检查是否存在可用更新并刷新红点状态</summary>
    public void CheckForUpdates()
    {
        IsUpdateAvailable = !string.IsNullOrWhiteSpace(_updateService.GetPendingVersion());
    }

    /// <summary>清除更新红点（用户已知晓更新后调用）</summary>
    public void ClearUpdateRedDot()
    {
        IsUpdateAvailable = false;
    }

    /// <summary>触发导航请求到指定页面</summary>
    /// <param name="page">目标页面标识</param>
    public void NavigateTo(string page)
    {
        NavigationRequested?.Invoke(this, page);
    }

    /// <summary>
    /// 异步加载权限状态：检测存储、管理所有文件、悬浮窗权限，
    /// 并据此更新 <see cref="PermissionStatus"/> 与 <see cref="PermissionStatusColor"/>。
    /// </summary>
    private async Task LoadPermissionStatusAsync()
    {
        var storageGranted = await _permissionService.CheckStoragePermissionAsync();
        var manageStorageGranted = await _permissionService.CheckManageStoragePermissionAsync();
        var overlayGranted = await _permissionService.CheckOverlayPermissionAsync();

        if (storageGranted && manageStorageGranted && overlayGranted)
        {
            PermissionStatus = "权限已就绪";
            PermissionStatusColor = "#4CAF50";
            return;
        }

        if (storageGranted || manageStorageGranted || overlayGranted)
        {
            PermissionStatus = "部分权限待处理";
            PermissionStatusColor = "#FF9800";
            return;
        }

        PermissionStatus = "需要授权";
        PermissionStatusColor = "#F44336";
    }

    /// <summary>根据当前深色模式设置同步图标资源名称</summary>
    private void SyncDarkModeIcon()
    {
        DarkModeIcon = _themeService.DarkModeSetting switch
        {
            DarkModeSetting.Light => "ic_light_mode",
            DarkModeSetting.Dark => "ic_dark_mode",
            _ => "ic_system_mode"
        };
    }
}
