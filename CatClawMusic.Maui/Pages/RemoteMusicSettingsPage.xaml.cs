using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

/// <summary>远程音乐设置页面，用于配置 WebDAV、Navidrome、SMB 等远程音乐源连接信息。</summary>
public partial class RemoteMusicSettingsPage : ContentPage
{
    private readonly RemoteMusicSettingsViewModel _vm;

    /// <summary>初始化 <see cref="RemoteMusicSettingsPage"/> 类的新实例，并绑定对应的视图模型。</summary>
    /// <param name="vm">远程音乐设置页面对应的视图模型。</param>
    public RemoteMusicSettingsPage(RemoteMusicSettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = _vm;
    }

    /// <summary>当页面显示在屏幕上时触发，加载并刷新远程音乐源的配置数据。</summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try { await _vm.OnAppearingAsync(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[RemoteMusicPage] OnAppearing: {ex.Message}"); }
    }

    /// <summary>点击选择远程路径按钮时触发，提示用户先填写主机地址并说明路径填写规则。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private async void OnSelectRemotePathClicked(object? sender, EventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_vm.FormHost))
            {
                await DisplayAlert("提示", "请先填写主机地址和端口，然后再选择路径", "确定");
                return;
            }

            await DisplayAlert("提示", "远程路径浏览需要先保存连接并测试连通后可用，请手动输入基础路径，例如：\n\n- WebDAV: /dav 或 /webdav\n- Navidrome: /rest\n- SMB: (留空或共享根目录)", "确定");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RemoteMusicPage] SelectPath error: {ex}");
        }
    }
}
