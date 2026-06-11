using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using Android.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Data;
using CatClawMusic.UI.Helpers;
using CatClawMusic.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using AndroidX.AppCompat.Widget;

namespace CatClawMusic.UI.Fragments;

/// <summary>
/// 远程音乐服务二级菜单 — 带快捷开关和状态指示
/// </summary>
public class RemoteMusicFragment : SettingsSubPageFragment
{
    // 状态色
    private static readonly Color ColorConnected  = Color.ParseColor("#90D5AC"); // 绿
    private static readonly Color ColorDisconnected = Color.ParseColor("#C0C0C0"); // 灰
    private static readonly Color ColorError      = Color.ParseColor("#FF8A8A"); // 红

    private View? _dotNavidrome;
    private TextView? _tvNavidromeStatus;
    private SwitchCompat? _swNavidrome;

    private View? _dotWebdav;
    private TextView? _tvWebdavStatus;
    private SwitchCompat? _swWebdav;

    private View? _dotSmb;
    private TextView? _tvSmbStatus;
    private SwitchCompat? _swSmb;

    private ConnectionProfile? _navidromeProfile;
    private ConnectionProfile? _webdavProfile;
    private ConnectionProfile? _smbProfile;
    private bool _isLoading; // 防止开关回调在加载数据时触发

    protected override string GetTitle() => "远程音乐服务";

    /// <summary>
    /// 创建远程音乐服务视图
    /// </summary>
    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
        => inflater.Inflate(Resource.Layout.fragment_remote_music, container, false)!;

    /// <summary>
    /// 子视图创建完成后初始化控件引用和事件绑定
    /// </summary>
    protected override void OnSubViewCreated(View view, Bundle? state)
    {
        var nav = MainApplication.Services.GetRequiredService<INavigationService>();

        // Navidrome
        _dotNavidrome = view.FindViewById<View>(Resource.Id.dot_navidrome);
        _tvNavidromeStatus = view.FindViewById<TextView>(Resource.Id.tv_navidrome_status);
        _swNavidrome = view.FindViewById<SwitchCompat>(Resource.Id.sw_navidrome);

        var cardNavidrome = view.FindViewById<View>(Resource.Id.card_navidrome);
        if (cardNavidrome != null)
            cardNavidrome.SetOnClickListener(new ClickListener(() => nav.PushFragment("NavidromeSettings")));

        if (_swNavidrome != null)
            _swNavidrome.CheckedChange += OnNavidromeSwitchChanged;

        // WebDAV
        _dotWebdav = view.FindViewById<View>(Resource.Id.dot_webdav);
        _tvWebdavStatus = view.FindViewById<TextView>(Resource.Id.tv_webdav_status);
        _swWebdav = view.FindViewById<SwitchCompat>(Resource.Id.sw_webdav);

        var cardWebdav = view.FindViewById<View>(Resource.Id.card_webdav);
        if (cardWebdav != null)
            cardWebdav.SetOnClickListener(new ClickListener(() => nav.PushFragment("WebDavSettings")));

        if (_swWebdav != null)
            _swWebdav.CheckedChange += OnWebdavSwitchChanged;

        // SMB
        _dotSmb = view.FindViewById<View>(Resource.Id.dot_smb);
        _tvSmbStatus = view.FindViewById<TextView>(Resource.Id.tv_smb_status);
        _swSmb = view.FindViewById<SwitchCompat>(Resource.Id.sw_smb);

        var cardSmb = view.FindViewById<View>(Resource.Id.card_smb);
        if (cardSmb != null)
            cardSmb.SetOnClickListener(new ClickListener(() => nav.PushFragment("SmbSettings")));

        if (_swSmb != null)
            _swSmb.CheckedChange += OnSmbSwitchChanged;

        // 加载已保存的连接配置
        _ = LoadProfilesAsync();
    }

    /// <summary>
    /// 异步加载连接配置并更新UI状态
    /// </summary>
    private async Task LoadProfilesAsync()
    {
        _isLoading = true;
        try
        {
            var db = MainApplication.Services.GetRequiredService<MusicDatabase>();
            await db.EnsureInitializedAsync();
            var profiles = await db.GetConnectionProfilesAsync();

            _navidromeProfile = profiles.FirstOrDefault(p => p.Protocol == ProtocolType.Navidrome);
            _webdavProfile = profiles.FirstOrDefault(p => p.Protocol == ProtocolType.WebDAV);
            _smbProfile = profiles.FirstOrDefault(p => p.Protocol == ProtocolType.SMB);

            Activity?.RunOnUiThread(() =>
            {
                // Navidrome
                if (_navidromeProfile != null && !string.IsNullOrWhiteSpace(_navidromeProfile.Host))
                {
                    var addr = $"{_navidromeProfile.Host}:{_navidromeProfile.Port}";
                    if (_navidromeProfile.IsEnabled)
                    {
                        SetDotColor(_dotNavidrome, ColorConnected);
                        _tvNavidromeStatus?.SetText($"✅ {_navidromeProfile.Name} | {addr}", TextView.BufferType.Normal);
                        _tvNavidromeStatus?.SetTextColor(Color.ParseColor("#2D7A50"));
                    }
                    else
                    {
                        SetDotColor(_dotNavidrome, ColorDisconnected);
                        _tvNavidromeStatus?.SetText($"{_navidromeProfile.Name} | {addr}", TextView.BufferType.Normal);
                        _tvNavidromeStatus?.SetTextColor(new Color(UiHelper.ResolveThemeColor(Context!, Resource.Attribute.catClawTextHint, Color.ParseColor("#B0A8BA").ToArgb())));
                    }
                    if (_swNavidrome != null) _swNavidrome.Checked = _navidromeProfile.IsEnabled;
                }
                else
                {
                    SetDotColor(_dotNavidrome, ColorDisconnected);
                    _tvNavidromeStatus?.SetText("未配置", TextView.BufferType.Normal);
                    _tvNavidromeStatus?.SetTextColor(new Color(UiHelper.ResolveThemeColor(Context!, Resource.Attribute.catClawTextHint, Color.ParseColor("#B0A8BA").ToArgb())));
                    if (_swNavidrome != null) _swNavidrome.Checked = false;
                }

                // WebDAV
                if (_webdavProfile != null && !string.IsNullOrWhiteSpace(_webdavProfile.Host))
                {
                    var addr = $"{_webdavProfile.Host}:{_webdavProfile.Port}";
                    if (_webdavProfile.IsEnabled)
                    {
                        SetDotColor(_dotWebdav, ColorConnected);
                        _tvWebdavStatus?.SetText($"✅ {_webdavProfile.Name} | {addr}", TextView.BufferType.Normal);
                        _tvWebdavStatus?.SetTextColor(Color.ParseColor("#2D7A50"));
                    }
                    else
                    {
                        SetDotColor(_dotWebdav, ColorDisconnected);
                        _tvWebdavStatus?.SetText($"{_webdavProfile.Name} | {addr}", TextView.BufferType.Normal);
                        _tvWebdavStatus?.SetTextColor(new Color(UiHelper.ResolveThemeColor(Context!, Resource.Attribute.catClawTextHint, Color.ParseColor("#B0A8BA").ToArgb())));
                    }
                    if (_swWebdav != null) _swWebdav.Checked = _webdavProfile.IsEnabled;
                }
                else
                {
                    SetDotColor(_dotWebdav, ColorDisconnected);
                    _tvWebdavStatus?.SetText("未配置", TextView.BufferType.Normal);
                    _tvWebdavStatus?.SetTextColor(new Color(UiHelper.ResolveThemeColor(Context!, Resource.Attribute.catClawTextHint, Color.ParseColor("#B0A8BA").ToArgb())));
                    if (_swWebdav != null) _swWebdav.Checked = false;
                }

                // SMB
                if (_smbProfile != null && !string.IsNullOrWhiteSpace(_smbProfile.Host))
                {
                    var addr = $"{_smbProfile.Host}:{_smbProfile.Port}";
                    if (_smbProfile.IsEnabled)
                    {
                        SetDotColor(_dotSmb, ColorConnected);
                        _tvSmbStatus?.SetText($"✅ {_smbProfile.Name} | {addr}", TextView.BufferType.Normal);
                        _tvSmbStatus?.SetTextColor(Color.ParseColor("#2D7A50"));
                    }
                    else
                    {
                        SetDotColor(_dotSmb, ColorDisconnected);
                        _tvSmbStatus?.SetText($"{_smbProfile.Name} | {addr}", TextView.BufferType.Normal);
                        _tvSmbStatus?.SetTextColor(new Color(UiHelper.ResolveThemeColor(Context!, Resource.Attribute.catClawTextHint, Color.ParseColor("#B0A8BA").ToArgb())));
                    }
                    if (_swSmb != null) _swSmb.Checked = _smbProfile.IsEnabled;
                }
                else
                {
                    SetDotColor(_dotSmb, ColorDisconnected);
                    _tvSmbStatus?.SetText("未配置", TextView.BufferType.Normal);
                    _tvSmbStatus?.SetTextColor(new Color(UiHelper.ResolveThemeColor(Context!, Resource.Attribute.catClawTextHint, Color.ParseColor("#B0A8BA").ToArgb())));
                    if (_swSmb != null) _swSmb.Checked = false;
                }
            });
        }
        catch { }
        finally { _isLoading = false; }
    }

    /// <summary>
    /// Navidrome开关切换时保存启用状态并刷新UI
    /// </summary>
    private async void OnNavidromeSwitchChanged(object? sender, CompoundButton.CheckedChangeEventArgs e)
    {
        if (_isLoading || _navidromeProfile == null) return;
        _navidromeProfile.IsEnabled = e.IsChecked;
        await SaveProfileAsync(_navidromeProfile);
        LibraryViewModel.NotifyProtocolChanged(this);
        await LoadProfilesAsync();
    }

    private async void OnWebdavSwitchChanged(object? sender, CompoundButton.CheckedChangeEventArgs e)
    {
        if (_isLoading || _webdavProfile == null) return;
        _webdavProfile.IsEnabled = e.IsChecked;
        await SaveProfileAsync(_webdavProfile);
        LibraryViewModel.NotifyProtocolChanged(this);
        await LoadProfilesAsync();
    }

    private async void OnSmbSwitchChanged(object? sender, CompoundButton.CheckedChangeEventArgs e)
    {
        if (_isLoading || _smbProfile == null) return;
        _smbProfile.IsEnabled = e.IsChecked;
        await SaveProfileAsync(_smbProfile);
        LibraryViewModel.NotifyProtocolChanged(this);
        await LoadProfilesAsync();
    }

    /// <summary>
    /// 保存连接配置到数据库
    /// </summary>
    private static async Task SaveProfileAsync(ConnectionProfile profile)
    {
        try
        {
            var db = MainApplication.Services.GetRequiredService<MusicDatabase>();
            await db.SaveConnectionProfileAsync(profile);
        }
        catch { }
    }

    /// <summary>
    /// 设置圆点视图的颜色
    /// </summary>
    private static void SetDotColor(View? dot, Color color)
    {
        if (dot == null) return;
        var bg = new GradientDrawable();
        bg.SetColor(color);
        bg.SetCornerRadius(4f); // 小圆点
        dot.Background = bg;
    }

    /// <summary>
    /// 通用点击监听器，封装 Action 回调
    /// </summary>
    private class ClickListener : Java.Lang.Object, View.IOnClickListener
    {
        private readonly Action _action;
        /// <summary>
        /// 使用指定的操作初始化点击监听器
        /// </summary>
        public ClickListener(Action action) => _action = action;
        /// <summary>
        /// 触发点击回调
        /// </summary>
        public void OnClick(View? v) => _action();
    }
}
