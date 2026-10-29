using CatClawMusic.Core.Interfaces;
using CatClawMusic.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CatClawMusic.WinUI.Views;

/// <summary>插件管理页：安装 / 启用停用 / 卸载。</summary>
public sealed partial class PluginsView : UserControl
{
    private readonly INavigationService _navigation;

    public PluginsView(PluginViewModel viewModel, INavigationService navigation)
    {
        ViewModel = viewModel;
        _navigation = navigation;

        InitializeComponent();

        Loaded += (_, _) =>
        {
            ViewModel.Load();
            UpdateState();
        };

        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(PluginViewModel.IsEmpty) or nameof(PluginViewModel.IsBusy))
                UpdateState();
        };
    }

    public PluginViewModel ViewModel { get; }

    private async void OnBackClick(object sender, RoutedEventArgs e) => await _navigation.GoBackAsync();

    private async void OnInstallClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.InstallAsync();
        UpdateState();
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e) =>
        ViewModel.OpenPluginFolderCommand.Execute(null);

    private void OnPluginToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle && toggle.DataContext is PluginRow row && row.IsEnabled != toggle.IsOn)
            ViewModel.SetEnabled(row, toggle.IsOn);
    }

    private async void OnUninstallClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not PluginRow row)
            return;

        var dialog = new ContentDialog
        {
            Title = "卸载插件",
            Content = $"确定卸载「{row.Name}」吗？卸载后重启应用生效。",
            PrimaryButtonText = "卸载",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        await ViewModel.UninstallAsync(row);
        UpdateState();
    }

    private void UpdateState()
    {
        BusyRing.IsActive = ViewModel.IsBusy;

        var empty = ViewModel.IsEmpty;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        PluginsList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
    }
}
