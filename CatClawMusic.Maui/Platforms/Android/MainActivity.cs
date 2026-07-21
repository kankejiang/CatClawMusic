using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Android.Views;
using AndroidX.Core.View;
using CatClawMusic.Maui.Platforms.Android;
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
    /// <summary>交互状态服务：全局触摸事件上报，用于在手指操作期间暂停雾面动画、英雄卡轮播等持续工作</summary>
    private IInteractionStateService? _interaction;

    /// <summary>Activity 创建时回调：执行基类创建、设置 Edge-to-Edge 并将自身注入到 AudioPlayerService</summary>
    /// <param name="savedInstanceState">保存的实例状态</param>
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetupEdgeToEdge();
        SetupHighRefreshRate();
        // 首帧布局（含 Splash 关闭后的真实布局）后再次强制 Edge-to-Edge：
        // MAUI 的 AndroidWindow 可能在 OnCreate 之后才真正建立并把 DecorFitsSystemWindows 重置为 true，
        // 导致「启动有空白、导航返回后变全屏」。GlobalLayout 监听在每次真实布局后都再强制一次，覆盖该时机。
        AttachEdgeToEdgeReassert();

        // 将 Android Context 传给 AudioPlayerService 用于启动前台服务
        var audioPlayer = MauiProgram.Services.GetService<AudioPlayerService>();
        audioPlayer?.SetAndroidContext(this);

        _interaction = MauiProgram.Services.GetService<IInteractionStateService>();

        // 全局未处理异常处理：把堆栈与崩溃前日志轨迹落盘，无设备也能定位
        AndroidEnvironment.UnhandledExceptionRaiser += (_, args) =>
        {
            try
            {
                LogService.Instance?.Error("Crash", $"Unhandled (Android): {args.Exception}");
                FileLoggerProvider.DumpToCrashFile();
                CrashReporter.RecordJava("AndroidEnvironment.UnhandledExceptionRaiser", args.Exception);
                LogService.Instance?.Flush();
                BitmapMemoryCache.Clear();
            }
            catch { }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            try
            {
                var ex = args.ExceptionObject as Exception;
                LogService.Instance?.Error("Crash", $"Unhandled (AppDomain, terminating={args.IsTerminating}): {ex}");
                FileLoggerProvider.DumpToCrashFile();
                CrashReporter.RecordManaged("AppDomain.UnhandledException", ex, args.IsTerminating);
                LogService.Instance?.Flush();
                BitmapMemoryCache.Clear();
            }
            catch { }
        };
    }

    /// <summary>Activity 创建完成（窗口已附加、MAUI 已完成首轮窗口搭建）后回调。
    /// MAUI 的 AndroidWindow 可能在 OnCreate 之后才真正建立并把 DecorFitsSystemWindows 重置为 true，
    /// 因此这里再次强制 Edge-to-Edge，确保首帧即全屏、启动无底部空白。</summary>
    protected override void OnPostCreate(Bundle? savedInstanceState)
    {
        base.OnPostCreate(savedInstanceState);
        SetupEdgeToEdge();
    }

    /// <summary>每次 Activity 回到前台（含首次启动）回调：再次确保 Edge-to-Edge 生效，
    /// 抵消任何在 OnCreate/OnPostCreate 之后才发生的窗口装饰重置。</summary>
    protected override void OnResume()
    {
        base.OnResume();
        SetupEdgeToEdge();
    }

    /// <summary>全局触摸分发钩子：在任何视图处理之前上报手指按下/抬起。
    /// 交互状态服务据此在手指停留在屏幕期间暂停雾面流体动画、英雄卡轮播等
    /// 持续性工作（覆盖拖进度条、竖滑播放页、长按等所有手势，不限于列表滚动）。</summary>
    public override bool DispatchTouchEvent(MotionEvent? e)
    {
        if (e != null)
        {
            switch (e.ActionMasked)
            {
                case MotionEventActions.Down:
                case MotionEventActions.PointerDown:
                    _interaction?.NotifyTouchStarted();
                    break;
                case MotionEventActions.Up:
                case MotionEventActions.PointerUp:
                case MotionEventActions.Cancel:
                    _interaction?.NotifyTouchEnded();
                    break;
            }
        }
        return base.DispatchTouchEvent(e);
    }

    /// <summary>Activity 进入后台回调：强制清零触摸计数。
    /// 手势进行中切走应用时 UP 事件可能丢失，不清零会让 IsUserInteracting
    /// 永久为 true，导致歌词高亮/背景动画再也无法恢复。</summary>
    protected override void OnPause()
    {
        base.OnPause();
        try { _interaction?.ResetTouchState(); } catch { }
    }

    /// <summary>Android 低内存警告回调：主动释放 Bitmap 缓存并触发 GC，避免被 LMK 杀进程。</summary>
    public override void OnLowMemory()
    {
        base.OnLowMemory();
        try { BitmapMemoryCache.Clear(); } catch { }
        try { System.GC.Collect(); } catch { }
    }

    /// <summary>Android 内存裁剪回调：仅在严重级别（RunningCritical 及以上）清空 Bitmap 缓存并触发 GC。
    /// 注意：不要用 RunningLow 作为清空门槛——RunningLow(10) 在前台轻微内存压力下就会触发，
    /// 会把 64MB 封面缓存整个清空，回前台时所有封面被迫重解码，引发解码风暴与主线程长冻结。
    /// LRU 缓存本身已自限 64MB 并自动驱逐最旧条目，无需在轻度裁剪时全清。</summary>
    /// <param name="level">内存裁剪级别</param>
    public override void OnTrimMemory(TrimMemory level)
    {
        base.OnTrimMemory(level);
        if (level >= TrimMemory.RunningCritical)
        {
            try { BitmapMemoryCache.Clear(); } catch { }
            try { System.GC.Collect(); } catch { }
        }
    }

    /// <summary>挂载一次性（重复数次）全局布局监听：每次真实布局后都重新强制 Edge-to-Edge，
    /// 覆盖 Splash 关闭、主题切换等可能把窗口重置为「内容止于导航栏」的时机，确保启动即全屏。</summary>
    private void AttachEdgeToEdgeReassert()
    {
        try
        {
            var rootView = Window?.DecorView?.FindViewById(Android.Resource.Id.Content);
            if (rootView?.ViewTreeObserver != null)
            {
                var listener = new EdgeToEdgeGlobalLayoutListener(this, rootView, 2);
                rootView.ViewTreeObserver.AddOnGlobalLayoutListener(listener);
            }
        }
        catch { }
    }

    /// <summary>请求窗口使用高刷新率（120Hz），解决 MAUI 默认 60Hz 帧率低的问题。
    /// Android 会自动回退到设备支持的最高值。</summary>
    private void SetupHighRefreshRate()
    {
        if (Window == null) return;

        var attrs = Window.Attributes;
        try
        {
            // .NET Android 未绑定 preferredDisplayRefreshRate 字段，通过 JNI 设置
            var clazz = JNIEnv.FindClass("android/view/WindowManager$LayoutParams");
            var fieldId = JNIEnv.GetFieldID(clazz, "preferredDisplayRefreshRate", "I");
            JNIEnv.SetField(attrs.Handle, fieldId, (nint)120);
            Window.Attributes = attrs;
        }
        catch { }
    }

    /// <summary>配置 Edge-to-Edge 显示：内容延伸到系统栏下方、状态栏与导航栏透明、并应用 insets 监听器。
    /// 可在 OnCreate/OnPostCreate/OnResume 及每次布局后重复调用（幂等）。</summary>
    internal void SetupEdgeToEdge()
    {
        if (Window == null) return;

        // 允许内容延伸到系统栏下方
        WindowCompat.SetDecorFitsSystemWindows(Window, false);

        // 启用窗口绘制系统栏背景
        Window.AddFlags(WindowManagerFlags.DrawsSystemBarBackgrounds);

        // 状态栏和导航栏完全透明
        Window.SetStatusBarColor(Android.Graphics.Color.Transparent);
        Window.SetNavigationBarColor(Android.Graphics.Color.Transparent);

        // Android 11+: 设置导航栏对比度为 0（不自动添加半透明遮罩）
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            Window.Attributes.LayoutInDisplayCutoutMode = LayoutInDisplayCutoutMode.Always;
            Window.NavigationBarContrastEnforced = false;
            Window.StatusBarContrastEnforced = false;
        }

        // DecorView 背景设为应用背景色，让系统栏区域颜色与页面一致
        UpdateDecorViewBackground();

        // 处理系统栏 insets
        var rootView = Window.DecorView.FindViewById(Android.Resource.Id.Content);
        if (rootView != null)
        {
            // 只在首次设置 listener，避免每次 SetupEdgeToEdge 替换掉正在排队的 insets 回调
            if (_insetsListener == null)
            {
                _insetsListener = new EdgeToEdgeInsets();
                ViewCompat.SetOnApplyWindowInsetsListener(rootView, _insetsListener);
            }
            // 请求重新应用 insets
            ViewCompat.RequestApplyInsets(rootView);

            // 同步读取当前窗口 insets（兜底：用于 EdgeToEdgeInsets 异步回调尚未到达时的首帧渲染）
            var density = Resources?.DisplayMetrics?.Density ?? 1f;
            double syncTop = 0, syncBottom = 0;

            // 优先尝试同步读取当前窗口 inset（Android 11+ / API 30+），
            // 比 navigation_bar_height 资源更准确（后者在手势导航下可能为 0）。
            var currentInsets = rootView.RootWindowInsets;
            if (currentInsets != null)
            {
                var systemBars = currentInsets.GetInsets(WindowInsets.Type.SystemBars());
                syncTop = systemBars.Top / density;
                syncBottom = systemBars.Bottom / density;
            }

            // 回退：从系统维度资源读取（传统 3 键导航）
            if (syncTop <= 0)
            {
                var statusHId = Resources?.GetIdentifier("status_bar_height", "dimen", "android") ?? 0;
                syncTop = (statusHId > 0 ? Resources?.GetDimensionPixelSize(statusHId) ?? 0 : 0) / density;
            }
            if (syncBottom <= 0)
            {
                var navHId = Resources?.GetIdentifier("navigation_bar_height", "dimen", "android") ?? 0;
                syncBottom = (navHId > 0 ? Resources?.GetDimensionPixelSize(navHId) ?? 0 : 0) / density;
            }

            // 全面屏手势兜底：navigation_bar_height 在手势导航下返回 0，
            // 但 RootWindowInsets 在 OnCreate 阶段可能尚未就绪。
            // 使用合理默认值防止 TabBar 完全紧贴屏幕底边。
            if (syncBottom <= 0)
                syncBottom = 16;

            SafeAreaHelper.UpdateInsets(syncTop, syncBottom);
        }
    }

    /// <summary>EdgeToEdgeInsets 监听器实例（只创建一次，避免重复替换打断回调链）</summary>
    private EdgeToEdgeInsets? _insetsListener;

    /// <summary>从应用资源读取 WindowBackgroundColor 并应用到 DecorView，同时根据亮度自动调整状态栏/导航栏图标颜色</summary>
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

                if (Window?.DecorView != null)
                {
                    var brightness = 0.299f * mauiColor.Red + 0.587f * mauiColor.Green + 0.114f * mauiColor.Blue;
                    var isLight = brightness > 0.5f;

                    // 使用 AndroidX WindowInsetsControllerCompat 来控制状态栏/导航栏图标颜色
                    if (Window != null)
                    {
                        var insetsController = WindowCompat.GetInsetsController(Window, Window.DecorView);
                        if (insetsController != null)
                        {
                            // Light status bar = 深色状态栏图标
                            insetsController.AppearanceLightStatusBars = isLight;
                            // Light navigation bar = 深色导航栏图标
                            insetsController.AppearanceLightNavigationBars = isLight;
                        }
                    }
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

        // 关键：强制清零根内容视图的原生 padding。
        // 即使返回 Consumed，MAUI 仍可能在其 ApplySafeArea 中将导航栏高度作为
        // ContentViewGroup 的底部 padding，导致所有页面（含全屏页）底部留空。
        // 项目内部页面通过 SafeAreaHelper 自行管理 padding，根视图不应再叠加。
        v.SetPadding(0, 0, 0, 0);

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

/// <summary>
/// 全局布局监听：每次真实布局完成后重新强制 Edge-to-Edge（重复数次），
/// 覆盖 Splash 关闭后、主题切换等窗口被重置为「内容止于系统栏」的时机，
/// 确保首帧起页面就延伸到屏幕底部（含导航栏区域），消除启动底部空白。
/// </summary>
internal class EdgeToEdgeGlobalLayoutListener : Java.Lang.Object, Android.Views.ViewTreeObserver.IOnGlobalLayoutListener
{
    private readonly System.WeakReference<MainActivity> _activity;
    private readonly Android.Views.View _view;
    private int _remaining;

    public EdgeToEdgeGlobalLayoutListener(MainActivity activity, Android.Views.View view, int repeats)
    {
        _activity = new System.WeakReference<MainActivity>(activity);
        _view = view;
        _remaining = repeats;
    }

    public void OnGlobalLayout()
    {
        if (_activity.TryGetTarget(out var activity))
            activity.SetupEdgeToEdge();
        _remaining--;
        if (_remaining <= 0)
        {
            var observer = _view?.ViewTreeObserver;
            if (observer != null && observer.IsAlive)
            {
                try { observer.RemoveOnGlobalLayoutListener(this); } catch { }
            }
        }
    }
}
