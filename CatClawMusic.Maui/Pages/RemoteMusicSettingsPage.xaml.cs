using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

/// <summary>
/// 远程音乐服务设置页面：配置 WebDAV / Navidrome / SMB 连接
/// </summary>
public partial class RemoteMusicSettingsPage : ContentPage
{
    private readonly RemoteMusicSettingsViewModel _vm;

    /// <summary>
    /// 初始化 <see cref="RemoteMusicSettingsPage"/> 实例
    /// </summary>
    /// <param name="vm">远程音乐设置 ViewModel</param>
    public RemoteMusicSettingsPage(RemoteMusicSettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = _vm;
    }

    /// <summary>页面出现时刷新数据</summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try { await _vm.OnAppearingAsync(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[RemoteMusicPage] OnAppearing: {ex.Message}"); }
    }
}
