namespace CatClawMusic.Maui.Controls;

public partial class HeroCard : ContentView
{
    public static readonly BindableProperty CardContentProperty = BindableProperty.Create(
        nameof(CardContent), typeof(View), typeof(HeroCard),
        propertyChanged: OnCardContentChanged);

    public static readonly BindableProperty ShowBackButtonProperty = BindableProperty.Create(
        nameof(ShowBackButton), typeof(bool), typeof(HeroCard), true);

    public View CardContent
    {
        get => (View)GetValue(CardContentProperty);
        set => SetValue(CardContentProperty, value);
    }

    public bool ShowBackButton
    {
        get => (bool)GetValue(ShowBackButtonProperty);
        set => SetValue(ShowBackButtonProperty, value);
    }

    public HeroCard()
    {
        InitializeComponent();
    }

    private static void OnCardContentChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is HeroCard card)
        {
            card.InnerContent.Content = newValue as View;
        }
    }
}
