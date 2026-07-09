using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

/// <summary>桌面歌词设置页面：管理桌面歌词开关、字号、颜色、锁定、背景透明度等。</summary>
public partial class DesktopLyricPage : ContentPage
{
    private readonly DesktopLyricViewModel _vm;

    /// <summary>初始化 <see cref="DesktopLyricPage"/> 类的新实例。</summary>
    public DesktopLyricPage(DesktopLyricViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = _vm;
    }

    /// <summary>页面显示时检查权限状态。</summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try { await _vm.OnAppearingAsync(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"DesktopLyricPage OnAppearing: {ex.Message}"); }
    }
}
