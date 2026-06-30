using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using AndroidX.Core.View;
using CatClawMusic.Maui.Services;

namespace CatClawMusic.Maui;

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
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetupEdgeToEdge();

        // 将 Android Context 传给 AudioPlayerService 用于启动前台服务
        var audioPlayer = MauiProgram.Services.GetService<AudioPlayerService>();
        audioPlayer?.SetAndroidContext(this);
    }

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

    private void UpdateDecorViewBackground()
    {
        // 从 MAUI 主题资源获取 WindowBackgroundColor
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
                return;
            }
        }
        catch { }

        // 回退默认色
        Window?.DecorView.SetBackgroundColor(Android.Graphics.Color.ParseColor("#080B1A"));
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if (!Platforms.Android.FolderPicker.HandleResult(requestCode, resultCode, data))
            base.OnActivityResult(requestCode, resultCode, data);
    }
}

/// <summary>
/// 处理窗口 insets：底部 TabBar 区域需要 navigation bar 的 padding，
/// 顶部状态栏由 MAUI Shell 自行处理。
/// </summary>
internal class EdgeToEdgeInsets : Java.Lang.Object, IOnApplyWindowInsetsListener
{
    public WindowInsetsCompat OnApplyWindowInsets(Android.Views.View? v, WindowInsetsCompat? insets)
    {
        if (v == null || insets == null) return insets!;
        var systemBars = insets.GetInsets(WindowInsetsCompat.Type.SystemBars());
        // 顶部留出状态栏高度，底部留出导航栏高度，让内容在安全区域内
        v.SetPadding(
            systemBars.Left,
            systemBars.Top,
            systemBars.Right,
            systemBars.Bottom
        );
        return WindowInsetsCompat.Consumed;
    }
}
