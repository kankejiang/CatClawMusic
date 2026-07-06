using System.Collections.ObjectModel;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

// 解决 CatClawMusic.Core.Models.ConnectionProfile 与 Microsoft.Maui.Networking.ConnectionProfile 的命名冲突
using ConnectionProfile = CatClawMusic.Core.Models.ConnectionProfile;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>
/// 远程音乐服务设置页 ViewModel：展示已配置的连接列表、缓存歌曲数量、
/// 支持新增 / 编辑 / 删除连接、测试连通性、清缓存。
/// </summary>
public partial class RemoteMusicSettingsViewModel : ObservableObject
{
    private readonly MusicDatabase _db;
    private readonly INetworkMusicService _networkMusicService;
    private readonly ISubsonicService _subsonicService;
    private readonly IEnumerable<INetworkFileService> _fileServices;
    private readonly IMusicLibraryService _musicLibrary;

    /// <summary>是否正在同步音乐库</summary>
    [ObservableProperty]
    private bool _isSyncing;

    /// <summary>同步状态文本</summary>
    [ObservableProperty]
    private string _syncStatus = "";

    /// <summary>已配置的连接列表</summary>
    public ObservableCollection<ConnectionProfile> Profiles { get; } = new();

    /// <summary>协议类型选项</summary>
    public ObservableCollection<string> ProtocolOptions { get; } =
        new() { "WebDAV", "Navidrome (Subsonic)", "SMB" };

    /// <summary>已缓存网络歌曲数量</summary>
    [ObservableProperty]
    private int _cachedSongCount;

    /// <summary>是否有已缓存的歌曲（控制清空按钮可用性）</summary>
    public bool HasCachedSongs => CachedSongCount > 0;

    partial void OnCachedSongCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasCachedSongs));
    }

    /// <summary>是否正在刷新列表</summary>
    [ObservableProperty]
    private bool _isRefreshing;

    /// <summary>是否正在测试连接</summary>
    [ObservableProperty]
    private bool _isTesting;

    // ── 新建/编辑表单字段 ──
    /// <summary>是否处于编辑表单状态</summary>
    [ObservableProperty]
    private bool _isEditing;

    /// <summary>当前编辑的连接 ID（0 表示新建）</summary>
    [ObservableProperty]
    private int _editingProfileId;

    /// <summary>表单 - 连接名称</summary>
    [ObservableProperty]
    private string _formName = "";

    /// <summary>表单 - 协议类型索引</summary>
    [ObservableProperty]
    private int _formProtocolIndex;

    /// <summary>表单 - 主机地址</summary>
    [ObservableProperty]
    private string _formHost = "";

    /// <summary>表单 - 端口号</summary>
    [ObservableProperty]
    private int _formPort = 5005;

    /// <summary>表单 - 用户名</summary>
    [ObservableProperty]
    private string _formUserName = "";

    /// <summary>表单 - 密码</summary>
    [ObservableProperty]
    private string _formPassword = "";

    /// <summary>表单 - 基础路径</summary>
    [ObservableProperty]
    private string _formBasePath = "/";

    /// <summary>表单 - 是否启用 HTTPS</summary>
    [ObservableProperty]
    private bool _formUseHttps;

    /// <summary>表单 - SMB 共享名</summary>
    [ObservableProperty]
    private string _formShareName = "";

    /// <summary>表单 - SMB 域名</summary>
    [ObservableProperty]
    private string _formDomainName = "";

    /// <summary>当前表单是否为 SMB 协议（用于显隐 SMB 专用字段）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWebDavForm))]
    [NotifyPropertyChangedFor(nameof(IsNavidromeForm))]
    private bool _isSmbForm;

    // ── 远程目录浏览相关字段 ──
    /// <summary>是否正在浏览远程目录</summary>
    [ObservableProperty]
    private bool _isBrowsing;

    /// <summary>是否正在加载目录列表</summary>
    [ObservableProperty]
    private bool _isLoadingDirs;

    /// <summary>当前浏览的远程路径</summary>
    [ObservableProperty]
    private string _browseCurrentPath = "/";

    /// <summary>远程目录浏览中的子目录列表</summary>
    public ObservableCollection<RemoteDirItem> DirectoryItems { get; } = new();

    /// <summary>当前表单是否为 WebDAV 协议</summary>
    public bool IsWebDavForm => FormProtocolIndex == (int)ProtocolType.WebDAV;

    /// <summary>当前表单是否为 Navidrome 协议</summary>
    public bool IsNavidromeForm => FormProtocolIndex == (int)ProtocolType.Navidrome;

    /// <summary>编辑面板标题</summary>
    public string FormTitle => EditingProfileId == 0 ? "新建连接" : "编辑连接";

    /// <summary>
    /// 初始化 <see cref="RemoteMusicSettingsViewModel"/> 实例。
    /// </summary>
    /// <param name="db">音乐数据库访问对象</param>
    /// <param name="networkMusicService">网络音乐服务，用于扫描远程音乐库</param>
    /// <param name="subsonicService">Subsonic/Navidrome 服务，用于连接测试</param>
    /// <param name="fileServices">网络文件服务集合（WebDAV / SMB 等）</param>
    /// <param name="musicLibrary">音乐库服务，用于导入扫描到的歌曲</param>
    public RemoteMusicSettingsViewModel(
        MusicDatabase db,
        INetworkMusicService networkMusicService,
        ISubsonicService subsonicService,
        IEnumerable<INetworkFileService> fileServices,
        IMusicLibraryService musicLibrary)
    {
        _db = db;
        _networkMusicService = networkMusicService;
        _subsonicService = subsonicService;
        _fileServices = fileServices;
        _musicLibrary = musicLibrary;
    }

    /// <summary>页面出现时刷新数据</summary>
    public async Task OnAppearingAsync()
    {
        await RefreshAsync();
    }

    /// <summary>刷新连接列表和缓存数量</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsRefreshing) return;
        IsRefreshing = true;
        try
        {
            await _db.EnsureInitializedAsync();
            var profiles = await _db.GetConnectionProfilesAsync();
            Profiles.Clear();
            foreach (var p in profiles)
                Profiles.Add(p);

            var cached = await _db.GetCachedNetworkSongsAsync();
            CachedSongCount = cached.Count;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RemoteMusic] Refresh 失败: {ex.Message}");
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    /// <summary>开始新建连接</summary>
    [RelayCommand]
    public void StartCreate()
    {
        EditingProfileId = 0;
        var defaultIndex = Profiles.Count(p => p.Name.StartsWith("我的音乐库")) + 1;
        FormName = Profiles.Count == 0 ? "我的音乐库" : $"我的音乐库 {defaultIndex}";
        FormProtocolIndex = 0;
        FormHost = "";
        FormPort = 5005;
        FormUserName = "";
        FormPassword = "";
        FormBasePath = "/";
        FormUseHttps = false;
        FormShareName = "";
        FormDomainName = "";
        IsSmbForm = false;
        IsEditing = true;
    }

    /// <summary>开始编辑指定连接</summary>
    [RelayCommand]
    public void StartEdit(ConnectionProfile? profile)
    {
        if (profile == null) return;
        EditingProfileId = profile.Id;
        FormName = profile.Name;
        FormProtocolIndex = (int)profile.Protocol;
        FormHost = profile.Host;
        FormPort = profile.Port;
        FormUserName = profile.UserName;
        FormPassword = profile.Password;
        FormBasePath = profile.BasePath;
        FormUseHttps = profile.UseHttps;
        FormShareName = profile.ShareName;
        FormDomainName = profile.DomainName;
        IsEditing = true;
    }

    /// <summary>取消编辑</summary>
    [RelayCommand]
    public void CancelEdit()
    {
        IsEditing = false;
    }

    /// <summary>保存当前表单的连接配置</summary>
    [RelayCommand]
    public async Task SaveProfileAsync()
    {
        if (string.IsNullOrWhiteSpace(FormName))
        {
            await ToastAsync("请填写连接名称");
            return;
        }
        if (string.IsNullOrWhiteSpace(FormHost))
        {
            await ToastAsync("请填写主机地址");
            return;
        }

        try
        {
            var profile = new ConnectionProfile
            {
                Id = EditingProfileId,
                Name = FormName.Trim(),
                Protocol = (ProtocolType)FormProtocolIndex,
                Host = FormHost.Trim(),
                Port = FormPort,
                UserName = FormUserName ?? "",
                Password = FormPassword ?? "",
                BasePath = string.IsNullOrWhiteSpace(FormBasePath) ? "/" : FormBasePath.Trim(),
                UseHttps = FormUseHttps,
                ShareName = FormShareName ?? "",
                DomainName = FormDomainName ?? "",
                IsEnabled = true,
                ApiVersion = "1.16.1",
                ClientName = "CatClawMusic",
                ServerType = 0
            };

            await _db.SaveConnectionProfileAsync(profile);
            IsEditing = false;
            await RefreshAsync();
            await ToastAsync(EditingProfileId == 0 ? "已添加连接" : "已更新连接");
        }
        catch (Exception ex)
        {
            await ToastAsync($"保存失败：{ex.Message}");
        }
    }

    /// <summary>删除指定连接</summary>
    [RelayCommand]
    public async Task DeleteProfileAsync(ConnectionProfile? profile)
    {
        if (profile == null) return;
        var confirm = await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (Application.Current?.MainPage is Page page)
                return await page.DisplayAlert("确认删除",
                    $"确定要删除连接「{profile.Name}」吗？已缓存的歌曲不会被清除。",
                    "删除", "取消");
            return false;
        });
        if (!confirm) return;

        try
        {
            await _db.DeleteConnectionProfileAsync(profile.Id);
            await RefreshAsync();
            await ToastAsync("已删除连接");
        }
        catch (Exception ex)
        {
            await ToastAsync($"删除失败：{ex.Message}");
        }
    }

    /// <summary>测试指定连接的连通性</summary>
    [RelayCommand]
    public async Task TestProfileAsync(ConnectionProfile? profile)
    {
        if (profile == null || IsTesting) return;
        IsTesting = true;
        try
        {
            bool ok;
            string msg;
            if (profile.Protocol == ProtocolType.Navidrome)
            {
                var r = await _subsonicService.PingAsync(profile);
                ok = r.Success;
                msg = r.Message;
            }
            else
            {
                // WebDAV / SMB：找对应的服务
                var svc = profile.Protocol == ProtocolType.SMB
                    ? _fileServices.FirstOrDefault(s => s is SmbService)
                    : _fileServices.FirstOrDefault(s => s is WebDavService);
                if (svc == null)
                {
                    await ToastAsync("未找到对应协议服务");
                    return;
                }
                var r = await svc.TestConnectionAsync(profile);
                ok = r.Success;
                msg = r.Message;
            }
            await ToastAsync(ok ? $"连接成功 ✓ ({msg})" : $"连接失败：{msg}");
        }
        catch (Exception ex)
        {
            await ToastAsync($"测试异常：{ex.Message}");
        }
        finally
        {
            IsTesting = false;
        }
    }

    /// <summary>清空已缓存的网络歌曲</summary>
    [RelayCommand]
    public async Task ClearCacheAsync()
    {
        var confirm = await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (Application.Current?.MainPage is Page page)
                return await page.DisplayAlert("确认清空",
                    $"将清空已缓存的 {CachedSongCount} 首网络歌曲记录，本地文件不会被删除。是否继续？",
                    "清空", "取消");
            return false;
        });
        if (!confirm) return;

        try
        {
            await _db.ClearCachedNetworkSongsAsync();
            await RefreshAsync();
            await ToastAsync("已清空缓存");
        }
        catch (Exception ex)
        {
            await ToastAsync($"清空失败：{ex.Message}");
        }
    }

    /// <summary>同步指定连接的音乐库</summary>
    [RelayCommand]
    public async Task SyncProfileAsync(ConnectionProfile? profile)
    {
        if (profile == null || IsSyncing) return;

        var confirm = await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (Application.Current?.MainPage is Page page)
                return await page.DisplayAlert("同步音乐库",
                    $"即将从「{profile.Name}」扫描并同步音乐库，是否继续？",
                    "开始同步", "取消");
            return false;
        });
        if (!confirm) return;

        IsSyncing = true;
        SyncStatus = "正在连接...";
        try
        {
            var progress = new Progress<(int done, int total, string status)>(p =>
            {
                SyncStatus = p.status;
            });

            var songs = await _networkMusicService.ScanAsync(profile, progress);
            
            SyncStatus = $"发现 {songs.Count} 首歌曲，正在导入...";
            var imported = await _musicLibrary.ImportSongsAsync(songs);
            
            await RefreshAsync();
            await ToastAsync($"同步完成！共导入 {imported.Count} 首歌曲");
        }
        catch (Exception ex)
        {
            await ToastAsync($"同步失败：{ex.Message}");
        }
        finally
        {
            IsSyncing = false;
            SyncStatus = "";
        }
    }

    /// <summary>根据协议类型返回显示文本</summary>
    public static string GetProtocolText(ProtocolType protocol) => protocol switch
    {
        ProtocolType.WebDAV => "WebDAV",
        ProtocolType.Navidrome => "Navidrome",
        ProtocolType.SMB => "SMB",
        _ => protocol.ToString()
    };

    /// <summary>FormProtocolIndex 变化时同步 IsSmbForm</summary>
    partial void OnFormProtocolIndexChanged(int value)
    {
        IsSmbForm = value == (int)ProtocolType.SMB;
    }

    private static async Task ToastAsync(string message)
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (Application.Current?.MainPage is Page page)
                await page.DisplayAlert("提示", message, "确定");
        });
    }

    // ═══════════════════════════════════════
    // 远程目录浏览
    // ═══════════════════════════════════════

    /// <summary>
    /// 打开远程目录浏览器：根据当前表单信息构建临时连接配置，
    /// 测试连通后列出根目录下的子目录。
    /// </summary>
    [RelayCommand]
    public async Task BrowsePathAsync()
    {
        if (IsNavidromeForm)
        {
            await ToastAsync("Navidrome 协议无需选择基础路径");
            return;
        }

        if (string.IsNullOrWhiteSpace(FormHost))
        {
            await ToastAsync("请先填写主机地址");
            return;
        }

        IsBrowsing = true;
        BrowseCurrentPath = "/";
        DirectoryItems.Clear();
        await LoadDirectoriesAsync("/");
    }

    /// <summary>关闭远程目录浏览弹窗</summary>
    [RelayCommand]
    public void CloseBrowse()
    {
        IsBrowsing = false;
    }

    /// <summary>进入子目录</summary>
    [RelayCommand]
    public async Task OpenDirAsync(RemoteDirItem? item)
    {
        if (item == null) return;
        if (item.IsParent)
        {
            await GoUpAsync();
            return;
        }
        BrowseCurrentPath = item.Path;
        DirectoryItems.Clear();
        await LoadDirectoriesAsync(item.Path);
    }

    /// <summary>返回上一级目录</summary>
    [RelayCommand]
    public async Task GoUpAsync()
    {
        var parent = GetParentPath(BrowseCurrentPath);
        if (parent == BrowseCurrentPath) return;
        BrowseCurrentPath = parent;
        DirectoryItems.Clear();
        await LoadDirectoriesAsync(parent);
    }

    /// <summary>选择当前浏览路径作为基础路径并关闭弹窗</summary>
    [RelayCommand]
    public void SelectCurrentDir()
    {
        FormBasePath = BrowseCurrentPath;
        IsBrowsing = false;
    }

    /// <summary>根据当前表单配置加载指定远程路径下的子目录</summary>
    private async Task LoadDirectoriesAsync(string path)
    {
        IsLoadingDirs = true;
        try
        {
            var profile = BuildFormProfile();
            var svc = profile.Protocol == ProtocolType.SMB
                ? _fileServices.FirstOrDefault(s => s is SmbService)
                : _fileServices.FirstOrDefault(s => s is WebDavService);

            if (svc == null)
            {
                await ToastAsync("未找到对应协议服务");
                return;
            }

            svc.Configure(profile);
            var files = await svc.ListFilesAsync(path);
            var dirs = files.Where(f => f.IsDirectory).OrderBy(f => f.Name).ToList();

            DirectoryItems.Clear();

            // 添加返回上级项
            var parent = GetParentPath(path);
            if (parent != path)
            {
                DirectoryItems.Add(new RemoteDirItem
                {
                    Name = "← 返回上级",
                    Path = parent,
                    IsDirectory = true,
                    IsParent = true
                });
            }

            foreach (var d in dirs)
            {
                DirectoryItems.Add(new RemoteDirItem
                {
                    Name = d.Name,
                    Path = d.Path,
                    IsDirectory = true,
                    IsParent = false
                });
            }

            if (DirectoryItems.Count == 0 || (DirectoryItems.Count == 1 && DirectoryItems[0].IsParent))
            {
                DirectoryItems.Add(new RemoteDirItem
                {
                    Name = "（空目录）",
                    Path = path,
                    IsDirectory = false,
                    IsParent = false
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BrowsePath] LoadDirs error: {ex}");
            await ToastAsync($"加载目录失败：{ex.Message}");
        }
        finally
        {
            IsLoadingDirs = false;
        }
    }

    /// <summary>根据当前表单字段构建临时连接配置（用于目录浏览）</summary>
    private ConnectionProfile BuildFormProfile()
    {
        return new ConnectionProfile
        {
            Name = FormName,
            Protocol = (ProtocolType)FormProtocolIndex,
            Host = FormHost,
            Port = FormPort,
            UserName = FormUserName,
            Password = FormPassword,
            BasePath = "/",
            UseHttps = FormUseHttps,
            ShareName = FormShareName,
            DomainName = FormDomainName
        };
    }

    /// <summary>获取远程路径的父目录</summary>
    private static string GetParentPath(string path)
    {
        if (string.IsNullOrEmpty(path) || path == "/") return "/";
        var trimmed = path.TrimEnd('/');
        var idx = trimmed.LastIndexOf('/');
        if (idx <= 0) return "/";
        return trimmed[..idx];
    }
}

/// <summary>远程目录项（用于浏览弹窗显示）</summary>
public partial class RemoteDirItem : ObservableObject
{
    /// <summary>显示名称</summary>
    [ObservableProperty]
    private string _name = "";

    /// <summary>完整远程路径</summary>
    [ObservableProperty]
    private string _path = "";

    /// <summary>是否为目录</summary>
    [ObservableProperty]
    private bool _isDirectory;

    /// <summary>是否为"返回上级"项</summary>
    [ObservableProperty]
    private bool _isParent;
}
