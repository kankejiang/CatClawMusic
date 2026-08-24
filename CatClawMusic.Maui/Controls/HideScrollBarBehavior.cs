namespace CatClawMusic.Maui.Controls;

/// <summary>
/// 隐藏安卓端 CollectionView 的滚动条（横滑卡片等）。
/// MAUI 的 CollectionView 不暴露 HorizontalScrollBarVisibility（仅 ScrollView 有），
/// 而原生层使用内建 MauiRecyclerView，不能整体替换 Handler，因此只能逐实例挂本 Behavior，
/// 在 Handler 就绪后取到底层原生视图把滚动条关掉。仅 Android 生效，其它平台 no-op。
/// </summary>
public class HideScrollBarBehavior : Behavior<View>
{
    protected override void OnAttachedTo(View bindable)
    {
        base.OnAttachedTo(bindable);
#if ANDROID
        bindable.HandlerChanged += OnHandlerChanged;
#endif
    }

#if ANDROID
    private void OnHandlerChanged(object? sender, EventArgs e)
    {
        if (sender is not View view || view.Handler?.PlatformView is not Android.Views.View platform)
            return;
        try
        {
            if (platform is AndroidX.RecyclerView.Widget.RecyclerView rv)
            {
                rv.VerticalScrollBarEnabled = false;
                rv.HorizontalScrollBarEnabled = false;
            }
            else
            {
                platform.GetType().GetProperty("VerticalScrollBarEnabled")?.SetValue(platform, false);
                platform.GetType().GetProperty("HorizontalScrollBarEnabled")?.SetValue(platform, false);
            }
        }
        catch { }
    }
#endif

    protected override void OnDetachingFrom(View bindable)
    {
#if ANDROID
        bindable.HandlerChanged -= OnHandlerChanged;
#endif
        base.OnDetachingFrom(bindable);
    }
}
