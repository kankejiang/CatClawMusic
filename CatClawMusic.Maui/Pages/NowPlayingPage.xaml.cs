using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

public partial class NowPlayingPage : ContentPage
{
    private readonly NowPlayingViewModel _viewModel;

    public NowPlayingPage(NowPlayingViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadCurrentSongAsync();
        // Ensure Slider Maximum is synced after load
        if (_viewModel.Duration > 0)
            ProgressSlider.Maximum = _viewModel.Duration;
    }

    private void OnSliderDragStarted(object? sender, EventArgs e)
    {
        // Disconnect binding so timer updates don't fight with user drag
        ProgressSlider.SetBinding(Slider.ValueProperty,
            new Binding("Progress", BindingMode.OneWay));
        _viewModel.OnSeekStarted();
    }

    private async void OnSliderDragCompleted(object? sender, EventArgs e)
    {
        // Seek to dragged position
        await _viewModel.OnSeekCompleted(ProgressSlider.Value);
        // Reconnect TwoWay binding so timer updates the slider again
        ProgressSlider.SetBinding(Slider.ValueProperty,
            new Binding("Progress", BindingMode.TwoWay));
    }
}
