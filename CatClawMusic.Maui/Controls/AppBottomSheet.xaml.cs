using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;

namespace CatClawMusic.Maui.Controls;

/// <summary>AppBottomSheet 的弹出位置模式。</summary>
public enum BottomSheetMode
{
    /// <summary>从屏幕底部滑入（默认，适合快捷选择器）。</summary>
    Bottom,
    /// <summary>屏幕中央弹出（缩放淡入，适合配置型面板如均衡器）。</summary>
    Center,
    /// <summary>全屏覆盖（缩放淡入，内容可滚动）。均衡器等内容较多、居中仍会截断时改用此模式。</summary>
    FullScreen
}

/// <summary>底部弹出面板控件（Bottom Sheet），支持三种模式：Bottom=底部抽屉、Center=居中弹窗、FullScreen=全屏覆盖（均带遮罩与模糊背景）。</summary>
public partial class AppBottomSheet : ContentView
{
    public static readonly BindableProperty CloseOnMaskTappedProperty =
        BindableProperty.Create(nameof(CloseOnMaskTapped), typeof(bool), typeof(AppBottomSheet), true);

    public bool CloseOnMaskTapped { get => (bool)GetValue(CloseOnMaskTappedProperty); set => SetValue(CloseOnMaskTappedProperty, value); }

    public static readonly BindableProperty SheetModeProperty =
        BindableProperty.Create(nameof(SheetMode), typeof(BottomSheetMode), typeof(AppBottomSheet), BottomSheetMode.Bottom);

    /// <summary>弹出位置：Bottom=底部抽屉（默认），Center=屏幕居中弹窗。</summary>
    public BottomSheetMode SheetMode { get => (BottomSheetMode)GetValue(SheetModeProperty); set => SetValue(SheetModeProperty, value); }

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

        var mode = SheetMode;
        var overlay = mode == BottomSheetMode.Center || mode == BottomSheetMode.FullScreen;
        if (overlay)
        {
            GripBar.IsVisible = false;
            SheetCard.HorizontalOptions = LayoutOptions.Fill;
            SheetCard.TranslationY = 0;
            SheetCard.Scale = 0.96;

            if (mode == BottomSheetMode.FullScreen)
            {
                // 全屏覆盖：用显式 HeightRequest 强制撑满屏幕高度（不依赖 VerticalOptions=Fill，
                // 因为运行时修改 VerticalOptions 后 MAUI 布局系统可能不会正确重新测量）。
                var screenH = DeviceDisplay.MainDisplayInfo.Height / DeviceDisplay.MainDisplayInfo.Density;
                SheetCard.VerticalOptions = LayoutOptions.Start;
                SheetCard.HeightRequest = screenH;
                SheetCard.Margin = new Thickness(0);
                SheetCard.ClearValue(MaximumHeightRequestProperty);
                // ScrollView 填满卡片剩余空间并可滚动
                ContentScroll.ClearValue(MaximumHeightRequestProperty);
            }
            else
            {
                // 居中弹窗：垂直居中、横向铺满留边。需设 MaximumHeightRequest 否则
                // VerticalOptions=Center 时 MAUI 无法计算内容高度，内部 ScrollView 拿不到空间被截断。
                var screenH = DeviceDisplay.MainDisplayInfo.Height / DeviceDisplay.MainDisplayInfo.Density;
                SheetCard.VerticalOptions = LayoutOptions.Center;
                SheetCard.Margin = new Thickness(22, 12);
                SheetCard.MaximumHeightRequest = screenH * 0.85;
                SheetCard.ClearValue(HeightRequestProperty);
                ContentScroll.MaximumHeightRequest = screenH * 0.85 - 60;
            }
        }
        else
        {
            // 底部抽屉：贴底、从下方滑入
            GripBar.IsVisible = true;
            SheetCard.VerticalOptions = LayoutOptions.End;
            SheetCard.HorizontalOptions = LayoutOptions.Fill;
            SheetCard.Margin = new Thickness(8, 0, 8, 8);
            SheetCard.TranslationY = 600;
            SheetCard.Scale = 1;
            SheetCard.ClearValue(MaximumHeightRequestProperty);
            SheetCard.ClearValue(HeightRequestProperty);
            ContentScroll.MaximumHeightRequest = 560;
        }

#if ANDROID
        ApplyBlurToSiblings();
#endif

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (overlay)
            {
                await Task.WhenAll(
                    MaskLayer.FadeTo(1, 220, Easing.CubicOut),
                    SheetCard.FadeTo(1, 200, Easing.CubicOut),
                    SheetCard.ScaleTo(1, 260, Easing.CubicOut)
                );
            }
            else
            {
                await Task.WhenAll(
                    MaskLayer.FadeTo(1, 220, Easing.CubicOut),
                    SheetCard.FadeTo(1, 200, Easing.CubicOut),
                    SheetCard.TranslateTo(0, 0, 300, Easing.CubicOut)
                );
            }
        });
    }

    public async void Close()
    {
        if (!_isOpen) return;
        _isOpen = false;

        var overlay = SheetMode == BottomSheetMode.Center || SheetMode == BottomSheetMode.FullScreen;
        Task anim;
        if (overlay)
        {
            anim = Task.WhenAll(
                MaskLayer.FadeTo(0, 180, Easing.CubicIn),
                SheetCard.ScaleTo(0.96, 200, Easing.CubicIn),
                SheetCard.FadeTo(0, 180, Easing.CubicIn)
            );
        }
        else
        {
            anim = Task.WhenAll(
                MaskLayer.FadeTo(0, 180, Easing.CubicIn),
                SheetCard.TranslateTo(0, 600, 220, Easing.CubicIn),
                SheetCard.FadeTo(0, 180, Easing.CubicIn)
            );
        }
        await anim;

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
