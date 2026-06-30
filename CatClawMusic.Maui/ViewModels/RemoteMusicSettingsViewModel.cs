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

    /// <summary>已配置的连接列表</summary>
    public ObservableCollection<ConnectionProfile> Profiles { get; } = new();

    /// <summary>协议类型选项</summary>
    public ObservableCollection<string> ProtocolOptions { get; } =
        new() { "WebDAV", "Navidrome (Subsonic)", "SMB" };

    [ObservableProperty]
    private int _cachedSongCount;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private bool _isTesting;

    // ── 新建/编辑表单字段 ──
    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private int _editingProfileId;

    [ObservableProperty]
    private string _formName = "";

    [ObservableProperty]
    private int _formProtocolIndex;

    [ObservableProperty]
    private string _formHost = "";

    [ObservableProperty]
    private int _formPort = 5005;

    [ObservableProperty]
    private string _formUserName = "";

    [ObservableProperty]
    private string _formPassword = "";

    [ObservableProperty]
    private string _formBasePath = "/";

    [ObservableProperty]
    private bool _formUseHttps;

    [ObservableProperty]
    private string _formShareName = "";

    [ObservableProperty]
    private string _formDomainName = "";

    /// <summary>当前表单是否为 SMB 协议（用于显隐 SMB 专用字段）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWebDavForm))]
    [NotifyPropertyChangedFor(nameof(IsNavidromeForm))]
    private bool _isSmbForm;

    /// <summary>当前表单是否为 WebDAV 协议</summary>
    public bool IsWebDavForm => FormProtocolIndex == (int)ProtocolType.WebDAV;

    /// <summary>当前表单是否为 Navidrome 协议</summary>
    public bool IsNavidromeForm => FormProtocolIndex == (int)ProtocolType.Navidrome;

    /// <summary>编辑面板标题</summary>
    public string FormTitle => EditingProfileId == 0 ? "新建连接" : "编辑连接";

    public RemoteMusicSettingsViewModel(
        MusicDatabase db,
        INetworkMusicService networkMusicService,
        ISubsonicService subsonicService,
        IEnumerable<INetworkFileService> fileServices)
    {
        _db = db;
        _networkMusicService = networkMusicService;
        _subsonicService = subsonicService;
        _fileServices = fileServices;
    }

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
        FormName = "";
        FormProtocolIndex = 0;
        FormHost = "";
        FormPort = 5005;
        FormUserName = "";
        FormPassword = "";
        FormBasePath = "/";
        FormUseHttps = false;
        FormShareName = "";
        FormDomainName = "";
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
            var cached = await _db.GetCachedNetworkSongsAsync();
            foreach (var s in cached)
                await _db.DeleteCachedSongAsync(s.Id);
            await RefreshAsync();
            await ToastAsync("已清空缓存");
        }
        catch (Exception ex)
        {
            await ToastAsync($"清空失败：{ex.Message}");
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
}
