using CatClawMusic.Maui.ViewModels;
using CatClawMusic.Core.Interfaces;

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
#if ANDROID || WINDOWS
        // 底部预留系统栏/dock 高度（车机 dock、Windows 任务栏、三键导航），
        // 避免滚动内容最后一张卡片被遮挡。顶部由 AppShell 的 SafeAreaPaddingBehavior 处理。
        SafeAreaHelper.SafeAreaChanged += OnSafeAreaChanged;
        Unloaded += (_, _) => SafeAreaHelper.SafeAreaChanged -= OnSafeAreaChanged;
        ApplyBottomSafeArea();
#endif
    }

#if ANDROID || WINDOWS
    private void OnSafeAreaChanged(object? sender, EventArgs e)
        => MainThread.BeginInvokeOnMainThread(ApplyBottomSafeArea);

    /// <summary>给内容栈底部叠加系统栏 inset（XAML 基础底部 padding 22 保持不变）。</summary>
    private void ApplyBottomSafeArea()
    {
        if (ContentStack == null) return;
        ContentStack.Padding = new Thickness(16, 8, 16, 22 + SafeAreaHelper.BottomInset);
    }
#endif

    /// <summary>页面显示时检查权限状态。</summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try { await _vm.OnAppearingAsync(); }
        catch (Exception ex) { Log.Debug("DesktopLyricPage.xaml", $"DesktopLyricPage OnAppearing: {ex.Message}"); }
    }
}
