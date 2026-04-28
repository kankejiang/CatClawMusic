using CatClawMusic.UI.ViewModels;

namespace CatClawMusic.UI.Pages;

public partial class NowPlayingPage : ContentPage
{
    public NowPlayingPage()
    {
        InitializeComponent();
        var services = Application.Current?.Handler?.MauiContext?.Services;
        if (services != null)
        {
            BindingContext = services.GetRequiredService<NowPlayingViewModel>();
        }
    }

    public NowPlayingPage(NowPlayingViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
