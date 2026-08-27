using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

/// <summary>插件市场页面：浏览 GitHub 托管的市场清单并一键安装/更新插件。</summary>
public partial class PluginMarketPage : ContentPage
{
    private readonly PluginMarketViewModel _vm;

    public PluginMarketPage(PluginMarketViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = _vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try { await _vm.OnAppearingAsync(); }
        catch (Exception ex) { Log.Debug("PluginMarketPage.xaml", $"[PluginMarket] OnAppearing: {ex.Message}"); }
    }

    /// <summary>打开插件源码仓库页（系统浏览器）。</summary>
    private async void OnRepoLinkTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (sender is BindableObject bo && bo.BindingContext is MarketPluginItem item
                && Uri.TryCreate(item.Repo, UriKind.Absolute, out var uri))
            {
                await Browser.Default.OpenAsync(uri, BrowserLaunchMode.External);
            }
        }
        catch (Exception ex)
        {
            Log.Debug("PluginMarketPage.xaml", $"[PluginMarket] 打开仓库失败: {ex.Message}");
        }
    }
}
