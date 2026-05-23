using System.Collections.ObjectModel;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Data;
using CatClawMusic.Core.Models;
using CatClawMusic.UI.Platforms.Android;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoreConnectionProfile = CatClawMusic.Core.Models.ConnectionProfile;

namespace CatClawMusic.UI.ViewModels;

/// <summary>
/// 设置ViewModel，管理WebDAV连接配置和音乐文件夹路径
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly INetworkFileService? _networkService;
    private readonly MusicDatabase? _database;
    private readonly IDialogService _dialogService;

    /// <summary>
    /// WebDAV服务器主机地址
    /// </summary>
    [ObservableProperty] private string _host = "";
    /// <summary>
    /// WebDAV服务器端口
    /// </summary>
    [ObservableProperty] private string _port = "5005";
    /// <summary>
    /// 用户名
    /// </summary>
    [ObservableProperty] private string _userName = "";
    /// <summary>
    /// 密码
    /// </summary>
    [ObservableProperty] private string _password = "";
    /// <summary>
    /// WebDAV基础路径
    /// </summary>
    [ObservableProperty] private string _basePath = "/";
    /// <summary>
    /// 缓存大小（GB）
    /// </summary>
    [ObservableProperty] private double _cacheSizeGB = 1;
    /// <summary>
    /// 是否仅在WiFi下缓存
    /// </summary>
    [ObservableProperty] private bool _onlyWiFiCache = true;
    /// <summary>
    /// 音乐文件夹路径
    /// </summary>
    [ObservableProperty] private string _musicFolder = "";
    /// <summary>
    /// 是否正在测试连接
    /// </summary>
    [ObservableProperty] private bool _isTesting;
    /// <summary>
    /// 状态文本
    /// </summary>
    [ObservableProperty] private string _statusText = "";

    /// <summary>
    /// 音乐文件夹列表
    /// </summary>
    public ObservableCollection<string> MusicFolders { get; } = new();

    private List<string> _folderUris = new();

    /// <summary>
    /// 缓存大小显示文本（如 "1 GB"）
    /// </summary>
    public string CacheSizeText => $"{CacheSizeGB:F0} GB";

    /// <summary>
    /// 缓存大小变化时同步更新显示文本
    /// </summary>
    partial void OnCacheSizeGBChanged(double value) => OnPropertyChanged(nameof(CacheSizeText));

    /// <summary>
    /// 无参构造函数
    /// </summary>
    public SettingsViewModel() : this(null, null, null!) { }

    /// <summary>
    /// 初始化设置ViewModel，从SharedPreferences加载音乐文件夹
    /// </summary>
    public SettingsViewModel(INetworkFileService? networkService, MusicDatabase? database, IDialogService dialogService)
    {
        _networkService = networkService;
        _database = database;
        _dialogService = dialogService;

        var ctx = global::Android.App.Application.Context;
        var prefs = ctx.GetSharedPreferences("catclaw_prefs", global::Android.Content.FileCreationMode.Private)!;
        MusicFolder = prefs.GetString("music_folder", "") ?? "";

        _folderUris = FolderPicker.GetSavedFolderUris();
        foreach (var uri in _folderUris)
            MusicFolders.Add(GetFolderDisplayName(uri));
    }

    /// <summary>
    /// 从文件夹URI中提取显示名称
    /// </summary>
    private static string GetFolderDisplayName(string uri)
    {
        try
        {
            var u = global::Android.Net.Uri.Parse(uri);
            var lastSegment = u.LastPathSegment ?? "";
            // 去除 content://.../tree/primary%3A xxx 中的 primary%3A 前缀
            var decoded = global::Android.Net.Uri.Decode(lastSegment);
            // 取最后一段路径名
            return decoded.Split('/').LastOrDefault() ?? decoded;
        }
        catch { return uri; }
    }

    /// <summary>
    /// 打开文件夹选择器添加音乐文件夹
    /// </summary>
    [RelayCommand]
    private async Task AddMusicFolder()
    {
        var uri = await FolderPicker.PickFolderAsync();
        if (!string.IsNullOrWhiteSpace(uri))
        {
            var name = GetFolderDisplayName(uri);
            if (!MusicFolders.Contains(name))
            {
                MusicFolders.Add(name);
                _folderUris.Add(uri);
            }
            MusicFolder = uri;
            OnPropertyChanged(nameof(MusicFolder));
        }
    }

    /// <summary>
    /// 清除所有音乐文件夹
    /// </summary>
    [RelayCommand]
    private void ClearMusicFolders()
    {
        FolderPicker.ClearFolders();
        MusicFolders.Clear();
        _folderUris.Clear();
        MusicFolder = "";
        OnPropertyChanged(nameof(MusicFolder));
    }

    /// <summary>
    /// 移除指定索引的音乐文件夹
    /// </summary>
    /// <param name="index">文件夹索引</param>
    public void RemoveMusicFolder(int index)
    {
        if (index < 0 || index >= MusicFolders.Count) return;
        var uri = index < _folderUris.Count ? _folderUris[index] : "";
        MusicFolders.RemoveAt(index);
        if (index < _folderUris.Count) _folderUris.RemoveAt(index);
        if (!string.IsNullOrEmpty(uri))
        {
            FolderPicker.RemoveSavedFolder(uri);
            _ = CleanupDeletedFolderSongsAsync();
        }
    }

    private async Task CleanupDeletedFolderSongsAsync()
    {
        try
        {
            if (_database == null) return;
            await _database.EnsureInitializedAsync();
            var remainingUris = FolderPicker.GetSavedFolderUris();
            var retainPaths = new HashSet<string>();
            foreach (var u in remainingUris)
                retainPaths.Add(u);
            await _database.RemoveStaleSongsAsync(SongSource.Local, retainPaths);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Settings] 清理已删除文件夹歌曲失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 测试WebDAV服务器连接
    /// </summary>
    [RelayCommand]
    private async Task TestConnection()
    {
        if (_networkService == null) { await _dialogService.ShowAlertAsync("错误", "网络服务未初始化"); return; }
        if (string.IsNullOrWhiteSpace(Host)) { await _dialogService.ShowAlertAsync("提示", "请输入主机地址"); return; }
        IsTesting = true; StatusText = "正在测试连接...";
        try
        {
            var (success, message) = await _networkService.TestConnectionAsync(BuildProfile());
            StatusText = message;
            await _dialogService.ShowAlertAsync(success ? "成功" : "失败", message);
        }
        catch (Exception ex) { StatusText = "连接失败"; await _dialogService.ShowAlertAsync("错误", $"连接测试失败: {ex.Message}"); }
        finally { IsTesting = false; }
    }

    /// <summary>
    /// 保存WebDAV连接配置到数据库
    /// </summary>
    [RelayCommand]
    private async Task SaveConnection()
    {
        if (_database == null) { await _dialogService.ShowAlertAsync("错误", "数据库未初始化"); return; }
        try
        {
            await _database.EnsureInitializedAsync();
            await _database.SaveConnectionProfileAsync(BuildProfile());
            StatusText = "配置已保存";
            await _dialogService.ShowAlertAsync("成功", "连接配置已保存");
        }
        catch (Exception ex) { StatusText = "保存失败"; await _dialogService.ShowAlertAsync("错误", $"保存失败: {ex.Message}"); }
    }

    /// <summary>
    /// 清除音乐缓存目录
    /// </summary>
    [RelayCommand]
    private async Task ClearCache()
    {
        try
        {
            var cacheDir = Path.Combine(global::Android.App.Application.Context.CacheDir!.AbsolutePath, "music_cache");
            if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, true);
            StatusText = "缓存已清除";
            await _dialogService.ShowAlertAsync("成功", "缓存已清除");
        }
        catch (Exception ex) { StatusText = "清除失败"; await _dialogService.ShowAlertAsync("错误", $"清除缓存失败: {ex.Message}"); }
    }

    private CoreConnectionProfile BuildProfile() => new()
    {
        Protocol = ProtocolType.WebDAV, Host = Host.Trim(),
        Port = int.TryParse(Port, out var p) ? p : 5005,
        UserName = UserName, Password = Password, BasePath = BasePath, IsEnabled = true
    };
}
