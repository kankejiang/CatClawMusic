using CatClawMusic.UI.ViewModels;

namespace CatClawMusic.UI.Pages;

public partial class NowPlayingPage : ContentPage
{
    private NowPlayingViewModel _viewModel = null!;

    public NowPlayingPage()
    {
        InitializeComponent();
        var services = Application.Current?.Handler?.MauiContext?.Services;
        if (services != null)
        {
            _viewModel = services.GetRequiredService<NowPlayingViewModel>();
            BindingContext = _viewModel;
        }
    }

    public NowPlayingPage(NowPlayingViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    private void OnCoverSwiped(object? sender, SwipedEventArgs e)
    {
        _viewModel.SwipeCommand.Execute(e.Direction);
    }
}
