namespace CatClawMusic.Maui.Pages;

/// <summary>
/// 启动占位页（Android 冷启动）：纯代码构建（无 XAML 膨胀、无 ActivityIndicator、无文本），
/// 背景/图标与系统启动画面（Maui.SplashTheme: #11141D + 居中 app_icon）完全一致，
/// 系统启动画面关闭后视觉无缝衔接；核心服务就绪后由 App 无条件换入 MainPage。
/// </summary>
public sealed class StartupPlaceholderPage : ContentPage
{
    public StartupPlaceholderPage()
    {
        // 固定深色，与系统启动画面一致；不用 DynamicResource（避免主题资源未就绪时闪变色）
        BackgroundColor = Color.FromArgb("#11141D");
        // 双保险：Shell 根已隐藏 NavBar/TabBar，这里显式声明，防御未来 Shell 样式变更
        Shell.SetNavBarIsVisible(this, false);
        Shell.SetTabBarIsVisible(this, false);

        Content = new Grid
        {
            Children =
            {
                new Image
                {
                    Source = "app_icon.png",
                    WidthRequest = 128,    // 对齐 MauiSplashScreen BaseSize=128,128；真机比对后可微调
                    HeightRequest = 128,
                    VerticalOptions = LayoutOptions.Center,
                    HorizontalOptions = LayoutOptions.Center,
                },
            },
        };
    }
}
