using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

/// <summary>权限管理页面，用于查看与配置应用所需的各项权限状态。</summary>
public partial class PermissionManagementPage : ContentPage
{
    private readonly PermissionManagementViewModel _vm;

    /// <summary>初始化 <see cref="PermissionManagementPage"/> 类的新实例，并绑定对应的视图模型。</summary>
    /// <param name="vm">权限管理页面对应的视图模型。</param>
    public PermissionManagementPage(PermissionManagementViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = _vm;
    }

    /// <summary>当页面显示在屏幕上时触发，加载并刷新权限状态信息。</summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try { await _vm.OnAppearingAsync(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[PermissionPage] OnAppearing: {ex.Message}"); }
    }
}
