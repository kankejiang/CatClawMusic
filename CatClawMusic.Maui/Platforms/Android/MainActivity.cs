using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using AndroidX.Core.View;
using CatClawMusic.Maui.Services;

namespace CatClawMusic.Maui;

/// <summary>Android 主 Activity，承载应用入口、Edge-to-Edge 显示、Android Context 注入以及 FolderPicker 结果处理</summary>
[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize
        | ConfigChanges.Orientation
        | ConfigChanges.UiMode
        | ConfigChanges.ScreenLayout
        | ConfigChanges.SmallestScreenSize
        | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    /// <summary>Activity 创建时回调：执行基类创建、设置 Edge-to-Edge 并将自身注入到 AudioPlayerService</summary>
    /// <param name="savedInstanceState">保存的实例状态</param>
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetupEdgeToEdge();

        // 将 Android Context 传给 AudioPlayerService 用于启动前台服务
        var audioPlayer = MauiProgram.Services.GetService<AudioPlayerService>();
        audioPlayer?.SetAndroidContext(this);
    }

    /// <summary>配置 Edge-to-Edge 显示：内容延伸到系统栏下方、状态栏与导航栏透明、并应用 insets 监听器</summary>
    private void SetupEdgeToEdge()
    {
        if (Window == null) return;

        // 允许内容延伸到系统栏下方
        WindowCompat.SetDecorFitsSystemWindows(Window, false);

        // 状态栏和导航栏透明
        Window.SetStatusBarColor(Android.Graphics.Color.Transparent);
        Window.SetNavigationBarColor(Android.Graphics.Color.Transparent);

        // DecorView 背景设为应用背景色，让系统栏区域颜色与页面一致
        UpdateDecorViewBackground();

        // 处理系统栏 insets
        var rootView = Window.DecorView.FindViewById(Android.Resource.Id.Content);
        if (rootView != null)
        {
            ViewCompat.SetOnApplyWindowInsetsListener(rootView, new EdgeToEdgeInsets());
        }
    }

    /// <summary>从应用资源读取 WindowBackgroundColor 并应用到 DecorView，同时根据亮度自动调整状态栏图标颜色</summary>
    public void UpdateDecorViewBackground()
    {
        try
        {
            var resources = CatClawMusic.Maui.App.Current?.Resources;
            if (resources?.TryGetValue("WindowBackgroundColor", out var colorObj) == true
                && colorObj is Microsoft.Maui.Graphics.Color mauiColor)
            {
                var androidColor = Android.Graphics.Color.Argb(
                    (int)(mauiColor.Alpha * 255),
                    (int)(mauiColor.Red * 255),
                    (int)(mauiColor.Green * 255),
                    (int)(mauiColor.Blue * 255));
                Window?.DecorView.SetBackgroundColor(androidColor);

                if (Build.VERSION.SdkInt >= BuildVersionCodes.M && Window?.DecorView != null)
                {
                    var brightness = 0.299f * mauiColor.Red + 0.587f * mauiColor.Green + 0.114f * mauiColor.Blue;
                    var isLight = brightness > 0.5f;
                    const int lightStatusBarFlag = 0x00002000; // SYSTEM_UI_FLAG_LIGHT_STATUS_BAR
                    var flags = (int)Window.DecorView.SystemUiVisibility;
                    if (isLight)
                        flags |= lightStatusBarFlag;
                    else
                        flags &= ~lightStatusBarFlag;
                    Window.DecorView.SystemUiVisibility = (StatusBarVisibility)flags;
                }
                return;
            }
        }
        catch { }

        Window?.DecorView.SetBackgroundColor(Android.Graphics.Color.ParseColor("#080B1A"));
    }

    /// <summary>Activity 结果回调：优先交给 FolderPicker 处理文件夹选择结果，未处理时回退到基类实现</summary>
    /// <param name="requestCode">请求码</param>
    /// <param name="resultCode">结果码</param>
    /// <param name="data">返回的 Intent 数据</param>
    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if (!Platforms.Android.FolderPicker.HandleResult(requestCode, resultCode, data))
            base.OnActivityResult(requestCode, resultCode, data);
    }
}

/// <summary>
/// 处理窗口 insets：记录系统栏高度到静态属性，供页面手动应用 SafeArea padding。
/// 不再给根视图设置 padding，让全屏页面（播放页/歌词页）的雾面背景能延伸到状态栏和导航栏区域。
/// </summary>
internal class EdgeToEdgeInsets : Java.Lang.Object, IOnApplyWindowInsetsListener
{
    /// <summary>系统栏 insets 应用回调：记录系统栏高度并通知页面更新 padding</summary>
    /// <param name="v">应用 insets 的视图</param>
    /// <param name="insets">窗口 insets</param>
    /// <returns>消费后的 WindowInsetsCompat</returns>
    public WindowInsetsCompat OnApplyWindowInsets(Android.Views.View? v, WindowInsetsCompat? insets)
    {
        if (v == null || insets == null) return insets!;
        var systemBars = insets.GetInsets(WindowInsetsCompat.Type.SystemBars());

        // 不设置根视图 padding，让内容延伸到系统栏区域
        // 各页面通过 SafeAreaHelper 自行处理 padding
        try
        {
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
            var density = activity?.Resources?.DisplayMetrics?.Density ?? 1f;
            var topDp = systemBars.Top / density;
            var bottomDp = systemBars.Bottom / density;

            Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
                SafeAreaHelper.UpdateInsets(topDp, bottomDp));
        }
        catch { }

        return WindowInsetsCompat.Consumed;
    }
}
