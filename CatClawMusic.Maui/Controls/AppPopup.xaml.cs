using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Devices;

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
            tap.Tapped += (_, _) => { _ = CloseAsync(); };
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

        // 在 ViewPager2 等「无界高度」宿主中，弹窗所在的 Grid 会被撑到整个内容高度，
        // 导致 PopupCard（VerticalOptions=Center）落在内容底部而非可见屏幕中心。
        // 将弹窗覆盖层固定为「一屏高」并顶对齐，使卡片在可见区域内居中。
        PinToScreenHeight();

        this.InputTransparent = false;
        this.IsVisible = true;
        this.Opacity = 1;

        MaskLayer.Opacity = 0;
        PopupCard.Opacity = 0;
        PopupCard.Scale = 0.9;
        PopupCard.TranslationY = 20;

#if ANDROID
        ApplyBlurToSiblings();
#endif

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

    /// <summary>
    /// 当弹窗被放在「无界高度」宿主（如 NativeTabPager / ViewPager2 中的页面）内时，
    /// 其父 Grid 会被 ScrollView 内容撑到极高，PopupCard 的 VerticalOptions=Center 会落到内容底部。
    /// 这里把弹窗覆盖层自身高度钳制为「一屏高(dp)」并顶对齐父容器，
    /// 从而让 PopupCard 在可见屏幕区域内居中，而不受父级内容总高度影响。
    /// </summary>
    private void PinToScreenHeight()
    {
        try
        {
            var display = DeviceDisplay.Current.MainDisplayInfo;
            var screenHeightDp = display.Height / display.Density;
            if (screenHeightDp > 0)
            {
                this.VerticalOptions = LayoutOptions.Start;
                this.HeightRequest = screenHeightDp;
            }
        }
        catch
        {
            // 取不到屏幕尺寸时回退到默认 Fill 居中行为，不影响功能
        }
    }

    public async Task CloseAsync()
    {
        if (!_isOpen) return;
        _isOpen = false;

        try
        {
            await Task.WhenAll(
                MaskLayer.FadeTo(0, 180, Easing.CubicIn),
                PopupCard.TranslateTo(0, 20, 180, Easing.CubicIn),
                PopupCard.FadeTo(0, 180, Easing.CubicIn),
                PopupCard.ScaleTo(0.9, 180, Easing.CubicIn)
            );

#if ANDROID
            RemoveBlurFromSiblings();
#endif

            this.Opacity = 0;
            this.IsVisible = false;
            this.InputTransparent = true;

            Closed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            // 关闭动画或清理过程中的异常不应导致崩溃，仅记录日志
            Log.Debug("AppPopup", $"[AppPopup] CloseAsync 异常: {ex.Message}");
        }
    }

    private void OnMaskTapped(object sender, EventArgs e)
    {
        if (CloseOnMaskTapped)
            _ = CloseAsync();
    }

#if ANDROID
    private readonly List<global::Android.Views.View> _blurredViews = new();

    /// <summary>对弹窗背后的兄弟视图应用高斯模糊 RenderEffect</summary>
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

    /// <summary>移除兄弟视图上的模糊效果</summary>
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
