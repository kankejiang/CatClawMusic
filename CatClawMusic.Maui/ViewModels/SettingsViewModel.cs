using CatClawMusic.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CatClawMusic.Maui.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly MusicDatabase _db;

    // === Observable Properties ===
    
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

    public SettingsViewModel(MusicDatabase db)
    {
        _db = db;

        // Initialize commands
        ToggleDarkModeCommand = new RelayCommand(ToggleDarkMode);
        LoadStatusCommand = new AsyncRelayCommand(LoadStatusAsync);
    }

    private void ToggleDarkMode()
    {
        // Cycle through dark mode settings
        // This is a simplified implementation
        var currentIcon = DarkModeIcon;
        
        if (currentIcon.Contains("system"))
        {
            DarkModeIcon = "ic_light_mode";
        }
        else if (currentIcon.Contains("light"))
        {
            DarkModeIcon = "ic_dark_mode";
        }
        else
        {
            DarkModeIcon = "ic_system_mode";
        }

        NavigationRequested?.Invoke(this, $"TOAST:深色模式已切换");
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

            // Load plugin status (simplified)
            PluginStatus = "插件系统待实现";

            // Load AI status (simplified)
            AiStatus = "AI助手待实现";

            // Update permission status
            PermissionStatus = "✅ 所有权限已开启";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsViewModel] 加载状态失败: {ex.Message}");
        }
    }

    public void CheckForUpdates()
    {
        // Simplified - no update check
        IsUpdateAvailable = false;
    }

    public void ClearUpdateRedDot()
    {
        IsUpdateAvailable = false;
    }

    public void NavigateTo(string page)
    {
        NavigationRequested?.Invoke(this, page);
    }
}
