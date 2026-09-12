namespace CatClawMusic.Maui.Pages;

/// <summary>
/// 启动加载页（Android 冷启动）：在关键服务（数据库、插件、FFmpeg）初始化完成前展示，
/// 等全部就绪后由 App 切换到 MainPage/DesktopMainPage。
/// 提前展示本页可避免 ViewPager2 主界面构建与后台服务初始化并发竞争主线程/IO，
/// 消除「App 已能操作但仍卡顿」的冷启动窗口。
/// </summary>
public partial class SplashLoadingPage : ContentPage
{
    public SplashLoadingPage()
    {
        InitializeComponent();
    }
}
