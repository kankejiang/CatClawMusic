using CatClawMusic.Maui.ViewModels;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Maui.Pages;

/// <summary>
/// 猫爪圈设置页：总开关、设备名、共享设置、扫描邻近设备并浏览/直传歌曲。
/// </summary>
public partial class ClawCircleSettingsPage : ContentPage
{
    private readonly ClawCircleSettingsViewModel _vm;

    /// <summary>初始化 <see cref="ClawCircleSettingsPage"/> 类的新实例。</summary>
    public ClawCircleSettingsPage(ClawCircleSettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = _vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await _vm.OnAppearingAsync();
        }
        catch (Exception ex)
        {
            Log.Debug("ClawCircleSettingsPage.xaml", $"[ClawCirclePage] OnAppearing 错误: {ex.Message}");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        try { _vm.Dispose(); } catch { }
    }
}
