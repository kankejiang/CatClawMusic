using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Maui.Controls;

public partial class AppPopup : ContentView
{
    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(nameof(Title), typeof(string), typeof(AppPopup), string.Empty,
            propertyChanged: OnTitleChanged);

    public static readonly BindableProperty ShowCloseButtonProperty =
        BindableProperty.Create(nameof(ShowCloseButton), typeof(bool), typeof(AppPopup), true);

    public static readonly BindableProperty CloseOnMaskTappedProperty =
        BindableProperty.Create(nameof(CloseOnMaskTapped), typeof(bool), typeof(AppPopup), true);

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public bool ShowCloseButton { get => (bool)GetValue(ShowCloseButtonProperty); set => SetValue(ShowCloseButtonProperty, value); }
    public bool CloseOnMaskTapped { get => (bool)GetValue(CloseOnMaskTappedProperty); set => SetValue(CloseOnMaskTappedProperty, value); }

    public event EventHandler? Closed;

    private View? _titleBar;
    private bool _isOpen = false;

    public AppPopup()
    {
        InitializeComponent();
    }

    private static void OnTitleChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is AppPopup popup)
            popup.RebuildTitleBar();
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        RebuildTitleBar();
    }

    private void RebuildTitleBar()
    {
        if (PopupContent == null) return;

        if (_titleBar != null && PopupContent.Children.Contains(_titleBar))
            PopupContent.Children.Remove(_titleBar);

        if (string.IsNullOrEmpty(Title) && !ShowCloseButton)
            return;

        var titleRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new() { Width = new GridLength(1, GridUnitType.Star) },
                new() { Width = GridLength.Auto }
            },
            Margin = new Thickness(0, 0, 0, 16)
        };

        if (!string.IsNullOrEmpty(Title))
        {
            var titleLabel = new Label
            {
                Text = Title,
                FontSize = 18,
                FontAttributes = FontAttributes.Bold,
                FontFamily = "OpenSansSemibold",
                TextColor = (Color)Application.Current!.Resources["TextPrimaryColor"],
                VerticalOptions = LayoutOptions.Center
            };
            titleRow.Add(titleLabel, 0);
        }

        if (ShowCloseButton)
        {
            var closeBtn = new Border
            {
                BackgroundColor = (Color)Application.Current!.Resources["ChipInactiveColor"],
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(20) },
                StrokeThickness = 0,
                WidthRequest = 32,
                HeightRequest = 32,
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Center,
                Padding = new Thickness(0),
                Content = new Label
                {
                    Text = "\u2715",
                    FontSize = 16,
                    TextColor = (Color)Application.Current.Resources["TextSecondaryColor"],
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment = TextAlignment.Center
                }
            };
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => Close();
            closeBtn.GestureRecognizers.Add(tap);
            titleRow.Add(closeBtn, 1);
        }

        _titleBar = titleRow;
        PopupContent.Children.Insert(0, _titleBar);
    }

    public void AddContent(View view)
    {
        PopupContent.Children.Add(view);
    }

    public void ClearContent()
    {
        var toRemove = PopupContent.Children.Skip(1).ToList();
        foreach (var child in toRemove)
            PopupContent.Children.Remove(child);
    }

    public void Open()
    {
        if (_isOpen) return;
        _isOpen = true;

        this.InputTransparent = false;
        this.IsVisible = true;
        this.Opacity = 1;

        MaskLayer.Opacity = 0;
        PopupCard.Opacity = 0;
        PopupCard.Scale = 0.9;
        PopupCard.TranslationY = 20;

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Task.WhenAll(
                MaskLayer.FadeTo(1, 220, Easing.CubicOut),
                PopupCard.FadeTo(1, 220, Easing.CubicOut),
                PopupCard.TranslateTo(0, 0, 280, Easing.CubicOut),
                PopupCard.ScaleTo(1, 220, Easing.CubicOut)
            );
        });
    }

    public async void Close()
    {
        if (!_isOpen) return;
        _isOpen = false;

        await Task.WhenAll(
            MaskLayer.FadeTo(0, 180, Easing.CubicIn),
            PopupCard.TranslateTo(0, 20, 180, Easing.CubicIn),
            PopupCard.FadeTo(0, 180, Easing.CubicIn),
            PopupCard.ScaleTo(0.9, 180, Easing.CubicIn)
        );

        this.Opacity = 0;
        this.IsVisible = false;
        this.InputTransparent = true;

        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void OnMaskTapped(object sender, EventArgs e)
    {
        if (CloseOnMaskTapped)
            Close();
    }
}
