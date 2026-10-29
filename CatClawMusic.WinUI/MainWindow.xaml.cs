using CatClawMusic.WinUI.Infrastructure;
using CatClawMusic.WinUI.Services;
using CatClawMusic.WinUI.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace CatClawMusic.WinUI;

/// <summary>
/// 主窗口（对照 MAUI 的 <c>DesktopBlankPage</c>）：自绘标题栏 + 左侧导航 + 内容区 + 底部播放条。
/// 页面切换走 <see cref="INavigationHost"/>，导航服务与页面互不直接依赖。
/// </summary>
public sealed partial class MainWindow : Window, INavigationHost
{
    private readonly WinUiNavigationService _navigation;
    private readonly WinUiThemeService _theme;

    /// <summary>代码里同步 NavigationView 选中项时，抑制一次 SelectionChanged 回调，避免递归。</summary>
    private bool _syncingSelection;

    public MainWindow()
    {
        InitializeComponent();

        Title = "猫爪音乐";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarRow);
        AppWindow.Resize(new SizeInt32(1440, 900));

        _navigation = App.Services.GetRequiredService<WinUiNavigationService>();
        _theme = App.Services.GetRequiredService<WinUiThemeService>();

        _navigation.Attach(this);
        App.Services.GetRequiredService<WinUiDialogService>().Attach(() => RootGrid.XamlRoot);

        _theme.Applied += ApplyTitleBarTheme;
        _theme.Attach(RootGrid);

        ShowRoute(AppRoutes.Discover);
    }

    public void ShowRoute(string route)
    {
        UserControl? view = route switch
        {
            AppRoutes.Discover => App.Services.GetRequiredService<DiscoverView>(),
            AppRoutes.Playlists => App.Services.GetRequiredService<PlaylistsView>(),
            AppRoutes.Library => App.Services.GetRequiredService<LibraryView>(),
            AppRoutes.Settings => App.Services.GetRequiredService<SettingsView>(),
            _ => null,
        };

        if (view is null)
            return;

        ContentHost.Content = view;
        UpdateNavSelection(route);
    }

    public void SwitchTab(int tabIndex)
    {
        var route = tabIndex switch
        {
            0 => AppRoutes.Discover,
            1 => AppRoutes.Playlists,
            2 => AppRoutes.Library,
            _ => AppRoutes.Discover,
        };

        ShowRoute(route);
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_syncingSelection)
            return;

        if (args.SelectedItemContainer?.Tag is string route)
            ShowRoute(route);
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e) => ShowRoute(AppRoutes.Settings);

    /// <summary>同步左侧栏选中态：设置页不参与 NavigationView 的选中模型，单独高亮。</summary>
    private void UpdateNavSelection(string route)
    {
        _syncingSelection = true;
        Nav.SelectedItem = route == AppRoutes.Settings ? null : FindNavItem(route);
        _syncingSelection = false;

        SettingsButton.Background = route == AppRoutes.Settings
            ? GetBrush("AccentSoftBrush")
            : new SolidColorBrush(Colors.Transparent);
    }

    private static Brush GetBrush(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush
            ? brush
            : new SolidColorBrush(Colors.Transparent);

    private NavigationViewItem? FindNavItem(string route)
    {
        foreach (var item in Nav.MenuItems.OfType<NavigationViewItem>())
        {
            if (item.Tag as string == route)
                return item;
        }

        return null;
    }

    /// <summary>系统窗口按钮在深色背景下要换成浅色前景，否则几乎看不见。</summary>
    private void ApplyTitleBarTheme()
    {
        var dark = _theme.IsEffectivelyDarkNow;
        var titleBar = AppWindow.TitleBar;

        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonForegroundColor = dark ? Colors.White : Windows.UI.Color.FromArgb(0xFF, 0x4A, 0x3A, 0x44);
        titleBar.ButtonInactiveForegroundColor = dark ? Colors.Gray : Windows.UI.Color.FromArgb(0xFF, 0x86, 0x6B, 0x77);
        titleBar.ButtonHoverBackgroundColor = dark
            ? Windows.UI.Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)
            : Windows.UI.Color.FromArgb(0x14, 0x00, 0x00, 0x00);
        titleBar.ButtonHoverForegroundColor = dark ? Colors.White : Windows.UI.Color.FromArgb(0xFF, 0x4A, 0x3A, 0x44);
    }
}
