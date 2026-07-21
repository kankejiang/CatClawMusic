using Microsoft.Maui.Controls;

namespace CatClawMusic.Maui.Controls;

/// <summary>底部弹出面板控件（Bottom Sheet），从屏幕底部滑入，带遮罩与模糊背景</summary>
public partial class AppBottomSheet : ContentView
{
    public static readonly BindableProperty CloseOnMaskTappedProperty =
        BindableProperty.Create(nameof(CloseOnMaskTapped), typeof(bool), typeof(AppBottomSheet), true);

    public bool CloseOnMaskTapped { get => (bool)GetValue(CloseOnMaskTappedProperty); set => SetValue(CloseOnMaskTappedProperty, value); }

    public event EventHandler? Closed;

    private bool _isOpen;

    public AppBottomSheet()
    {
        InitializeComponent();
    }

    public void AddContent(View view)
    {
        SheetContent.Children.Add(view);
    }

    public void ClearContent()
    {
        SheetContent.Children.Clear();
    }

    public void Open()
    {
        if (_isOpen) return;
        _isOpen = true;

        this.InputTransparent = false;
        this.IsVisible = true;
        this.Opacity = 1;

        MaskLayer.Opacity = 0;
        SheetCard.Opacity = 0;
        SheetCard.TranslationY = 600;

#if ANDROID
        ApplyBlurToSiblings();
#endif

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Task.WhenAll(
                MaskLayer.FadeTo(1, 220, Easing.CubicOut),
                SheetCard.FadeTo(1, 200, Easing.CubicOut),
                SheetCard.TranslateTo(0, 0, 300, Easing.CubicOut)
            );
        });
    }

    public async void Close()
    {
        if (!_isOpen) return;
        _isOpen = false;

        await Task.WhenAll(
            MaskLayer.FadeTo(0, 180, Easing.CubicIn),
            SheetCard.TranslateTo(0, 600, 220, Easing.CubicIn),
            SheetCard.FadeTo(0, 180, Easing.CubicIn)
        );

#if ANDROID
        RemoveBlurFromSiblings();
#endif

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

#if ANDROID
    private readonly List<global::Android.Views.View> _blurredViews = new();

    private void ApplyBlurToSiblings()
    {
        _blurredViews.Clear();
        if (this.Parent is Microsoft.Maui.Controls.Layout layout)
        {
            foreach (var child in layout.Children)
            {
                if (child == this) continue;
                if (child is Microsoft.Maui.Controls.View view &&
                    view.Handler?.PlatformView is global::Android.Views.View nativeView)
                {
                    nativeView.SetRenderEffect(
                        global::Android.Graphics.RenderEffect.CreateBlurEffect(
                            24, 24, global::Android.Graphics.Shader.TileMode.Clamp));
                    _blurredViews.Add(nativeView);
                }
            }
        }
    }

    private void RemoveBlurFromSiblings()
    {
        foreach (var view in _blurredViews)
        {
            try { view.SetRenderEffect(null); } catch { }
        }
        _blurredViews.Clear();
    }
#endif
}
