using System.Diagnostics;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.WinUI.Infrastructure;
using CatClawMusic.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CatClawMusic.WinUI.Views;

public sealed partial class SettingsView : UserControl
{
    /// <summary>与 <see cref="DarkModeOptions"/> 的三项一一对应。</summary>
    private static readonly DarkModeSetting[] DarkModeOrder =
    [
        DarkModeSetting.FollowSystem,
        DarkModeSetting.Light,
        DarkModeSetting.Dark,
    ];

    private readonly WinUiThemeService _theme;
    private readonly INavigationService _navigation;
    private bool _syncingSelection;

    public SettingsView(WinUiThemeService theme, INavigationService navigation)
    {
        _theme = theme;
        _navigation = navigation;
        InitializeComponent();

        DataPathText.Text = AppPaths.DataDirectory;
        LogPathText.Text = AppPaths.LogDirectory;
        VersionText.Text = $"猫爪音乐 {GetType().Assembly.GetName().Version?.ToString(3) ?? "0.1.0"}（WinUI 3）";

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _syncingSelection = true;
        DarkModeOptions.SelectedIndex = Math.Max(0, Array.IndexOf(DarkModeOrder, _theme.DarkModeSetting));
        _syncingSelection = false;
    }

    private void OnDarkModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || !IsLoaded)
            return;

        var index = DarkModeOptions.SelectedIndex;
        if (index < 0 || index >= DarkModeOrder.Length)
            return;

        _theme.SetDarkModeSetting(DarkModeOrder[index]);
    }

    private async void OnOpenPluginsClick(object sender, RoutedEventArgs e) =>
        await _navigation.NavigateToAsync(AppRoutes.Plugins);

    private void OnOpenDataFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = AppPaths.DataDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Log.Warn("settings", $"打开数据目录失败：{ex.Message}");
        }
    }
}
