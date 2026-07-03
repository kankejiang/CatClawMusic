using CatClawMusic.Data;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CatClawMusic.Maui.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly MusicDatabase _db;
    private readonly IThemeService _themeService;
    private readonly IPermissionService _permissionService;
    private readonly IPluginManager _pluginManager;
    private readonly IAgentService _agentService;
    private readonly IUpdateService _updateService;

    [ObservableProperty]
    private string _darkModeIcon = "ic_system_mode";

    [ObservableProperty]
    private string _localMusicStatus = "加载中...";

    [ObservableProperty]
    private string _remoteMusicStatus = "加载中...";

    [ObservableProperty]
    private string _pluginStatus = "加载中...";

    [ObservableProperty]
    private string _aiStatus = "未配置";

    [ObservableProperty]
    private string _permissionStatus = "检测中...";

    [ObservableProperty]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    private string _permissionStatusColor = "#4CAF50";

    // === Commands ===
    
    public IRelayCommand ToggleDarkModeCommand { get; }
    public IAsyncRelayCommand LoadStatusCommand { get; }

    public event EventHandler<string>? NavigationRequested;

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
            System.Diagnostics.Debug.WriteLine($"[SettingsViewModel] 加载状态失败: {ex.Message}");
            LocalMusicStatus = "状态加载失败";
            RemoteMusicStatus = "状态加载失败";
            PluginStatus = "状态加载失败";
            AiStatus = "状态加载失败";
            PermissionStatus = "状态加载失败";
            PermissionStatusColor = "#F44336";
        }
    }

    public void CheckForUpdates()
    {
        IsUpdateAvailable = !string.IsNullOrWhiteSpace(_updateService.GetPendingVersion());
    }

    public void ClearUpdateRedDot()
    {
        IsUpdateAvailable = false;
    }

    public void NavigateTo(string page)
    {
        NavigationRequested?.Invoke(this, page);
    }

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
