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
                // Grid 强制高度（同 Bottom 模式原理），ScrollView 填满卡片剩余空间并可滚动
                SheetGrid.HeightRequest = screenH;
                ContentScroll.ClearValue(HeightRequestProperty);
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
                SheetGrid.ClearValue(HeightRequestProperty);
                ContentScroll.MaximumHeightRequest = screenH * 0.85 - 60;
            }
        }
        else
        {
            // 底部抽屉：贴底、从下方滑入，固定 80% 屏高
            GripBar.IsVisible = true;
            SheetCard.VerticalOptions = LayoutOptions.End;
            SheetCard.HorizontalOptions = LayoutOptions.Fill;
            SheetCard.Margin = new Thickness(8, 0, 8, 8);
            SheetCard.Scale = 1;
            SheetCard.ClearValue(MaximumHeightRequestProperty);
            var screenH = ResolveScreenHeight();
            var sheetH = screenH * 0.8;
            // 同 FullScreen 模式原理：直接给 SheetCard/Grid 固定高度，且在置可见前设置——
            // 首次测量即带正确高度，不依赖运行时改 HeightRequest 触发重新测量（嵌入式宿主中不可靠）
            SheetCard.HeightRequest = sheetH;
            SheetGrid.HeightRequest = sheetH;
            ContentScroll.ClearValue(HeightRequestProperty);
            ContentScroll.ClearValue(MaximumHeightRequestProperty);
        }

        // 关键：尺寸全部就绪后再置可见——首次测量即带正确高度，
        // 不依赖「运行时改 HeightRequest 触发重新测量」（嵌入式宿主中不可靠，抽屉曾因此塌回内容高度）
        this.IsVisible = true;
        this.Opacity = 1;

#if ANDROID
        ApplyBlurToSiblings();
#endif

        DumpState("open");
        _ = Task.Delay(1500).ContinueWith(_ => MainThread.BeginInvokeOnMainThread(() =>
        {
            DumpState("settled");
        }));

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
                // 嵌入式宿主中 TranslationY 的 handler 映射失效（MAUI 层归零但原生视图停留在初始位移，
                // 卡片只露出底部一截），必须直写原生视图 translationY
                await Task.WhenAll(
                    MaskLayer.FadeTo(1, 220, Easing.CubicOut),
                    SheetCard.FadeTo(1, 180, Easing.CubicOut),
                    AnimateCardTranslationYAsync(600, 0, 300)
                );
                SetCardTranslationY(0);
            }
        });
    }

    /// <summary>设置抽屉卡片位移（dp）。MAUI 属性与原生视图双写：嵌入式宿主中属性映射可能失效。</summary>
    private void SetCardTranslationY(double dp)
    {
        SheetCard.TranslationY = dp;
#if ANDROID
        try
        {
            if (SheetCard.Handler?.PlatformView is global::Android.Views.View nv)
                nv.TranslationY = (float)(dp * nv.Resources!.DisplayMetrics!.Density);
        }
        catch (Exception ex) { global::Android.Util.Log.Error("FMDBG", "set translation failed: " + ex.Message); }
#endif
    }

    /// <summary>逐帧手动插值卡片 TranslationY（三次缓出），直写原生视图，不依赖 ViewExtensions 动画 ticker。</summary>
    private async Task AnimateCardTranslationYAsync(double from, double to, uint durationMs)
    {
        const int frameMs = 16;
        for (var t = 0; t < durationMs; t += frameMs)
        {
            await Task.Delay(frameMs);
            var p = Math.Min(1.0, (t + frameMs) / (double)durationMs);
            var eased = 1 - Math.Pow(1 - p, 3);
            SetCardTranslationY(from + (to - from) * eased);
        }
        SetCardTranslationY(to);
    }

    public async Task CloseAsync()
    {
        if (!_isOpen) return;
        _isOpen = false;

        try
        {
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
                    AnimateCardTranslationYAsync(SheetCard.TranslationY, 600, 200),
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
        catch (Exception ex)
        {
            // 关闭动画或清理过程中的异常不应导致崩溃，仅记录日志
            Log.Debug("AppBottomSheet", $"[AppBottomSheet] CloseAsync 异常: {ex.Message}");
        }
    }

    private void OnMaskTapped(object sender, EventArgs e)
    {
        if (CloseOnMaskTapped)
            _ = CloseAsync();
    }

    /// <summary>解析可用屏幕高度。Android 优先取原生窗口 CurrentWindowMetrics（真值，
    /// 不受 MAUI 嵌入式宿主测量影响）；否则沿元素树取宿主页真实高度；最后退回显示器尺寸。</summary>
    private double ResolveScreenHeight()
    {
#if ANDROID
        try
        {
            var act = Platform.CurrentActivity;
            var bounds = act?.Window?.WindowManager?.CurrentWindowMetrics?.Bounds;
            if (bounds is { } b && b.Height() > 0)
            {
                var d = act!.Resources!.DisplayMetrics!.Density;
                if (d > 0) return b.Height() / d;
            }
        }
        catch { }
#endif
        Element? node = Parent;
        while (node != null)
        {
            if (node is Page page && page.Height > 0)
                return page.Height;
            node = node.Parent;
        }
        try
        {
            var d = DeviceDisplay.Current.MainDisplayInfo;
            var h = d.Height / d.Density;
            if (h > 0) return h;
        }
        catch { }
        return 800;
    }

    /// <summary>logcat 诊断：输出屏高来源与各层实际/请求高度，用于排查嵌入式宿主中的布局钳制。</summary>
    private void DumpState(string phase)
    {
#if ANDROID
        try
        {
            var page = FindPage();
            string Nat(string name, Element el) =>
                el?.Handler?.PlatformView is global::Android.Views.View nv
                    ? $"{name}[{nv.Left},{nv.Top},{nv.Right},{nv.Bottom},ty={nv.TranslationY:F0}]" : $"{name}[null]";
            global::Android.Util.Log.Info("FMDBG",
                $"[{phase}] card={SheetCard.Height:F0}/{SheetCard.HeightRequest:F0} " +
                $"grid={SheetGrid.Height:F0}/{SheetGrid.HeightRequest:F0} " +
                $"ty={SheetCard.TranslationY:F0} op={SheetCard.Opacity:F2} " +
                $"overlay={this.Height:F0}/{this.HeightRequest:F0} " +
                $"pageH={page?.Height ?? -1:F0} screenH={ResolveScreenHeight():F0} " +
                $"cardBounds={SheetCard.Bounds} parent={Parent?.GetType().Name} " +
                $"{Nat("ov", this)} {Nat("root", RootGrid)} {Nat("card", SheetCard)} {Nat("grid", SheetGrid)}");
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("FMDBG", ex.ToString());
        }
#endif
    }

    private Page? FindPage()
    {
        Element? node = Parent;
        while (node != null)
        {
            if (node is Page p) return p;
            node = node.Parent;
        }
        return null;
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
