using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;

namespace CatClawMusic.UI;

/// <summary>
/// 启动画面 Activity：加载网络动漫图片作为启动背景，等待主界面完全加载后再关闭。
/// 网络图片会缓存在本地，下次启动优先使用缓存。
/// </summary>
[Activity(
    Theme = "@style/CatClaw.SplashTheme",
    MainLauncher = true,
    ScreenOrientation = Android.Content.PM.ScreenOrientation.Portrait)]
public class SplashActivity : global::AndroidX.AppCompat.App.AppCompatActivity
{
    private const string ImageUrl = "https://t.alcy.cc/mp";
    private const int MinDisplayMs = 1500;
    private const int NetworkTimeoutMs = 4000;

    private ImageView _imageView = null!;
    private ProgressBar _progressBar = null!;
    private TextView _subtitleText = null!;

    private bool _imageLoaded;
    private bool _minTimeReached;
    private bool _transitionStarted;

    /// <summary>静态实例，供 MainActivity 在初始化完成后调用关闭</summary>
    public static SplashActivity? Instance { get; private set; }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Instance = this;

        // 全屏沉浸
        if (Window != null)
        {
            Window.DecorView.SystemUiVisibility = (StatusBarVisibility)(
                SystemUiFlags.Fullscreen |
                SystemUiFlags.HideNavigation |
                SystemUiFlags.ImmersiveSticky |
                SystemUiFlags.LayoutStable |
                SystemUiFlags.LayoutFullscreen |
                SystemUiFlags.LayoutHideNavigation);
        }

        SetContentView(Resource.Layout.activity_splash);

        _imageView = FindViewById<ImageView>(Resource.Id.splash_image)!;
        _progressBar = FindViewById<ProgressBar>(Resource.Id.splash_progress)!;
        _subtitleText = FindViewById<TextView>(Resource.Id.splash_subtitle)!;

        // 最小显示时间计时器
        _ = Task.Delay(MinDisplayMs).ContinueWith(_ =>
        {
            _minTimeReached = true;
            TryTransition();
        });

        // 加载启动图
        _ = LoadSplashImageAsync();
    }

    /// <summary>由 MainActivity 在初始化完成后调用，关闭启动页</summary>
    public static void FinishSplash()
    {
        var activity = Instance;
        if (activity == null) return;
        Instance = null;
        try
        {
            activity.RunOnUiThread(() =>
            {
                activity.Finish();
                activity.OverridePendingTransition(0, 0);
            });
        }
        catch { /* Activity 可能已销毁 */ }
    }

    protected override void OnDestroy()
    {
        if (Instance == this) Instance = null;
        base.OnDestroy();
    }

    /// <summary>加载启动图：优先本地缓存，其次网络下载</summary>
    private async Task LoadSplashImageAsync()
    {
        string cachePath = GetCachePath();

        try
        {
            // 1. 尝试加载缓存
            if (File.Exists(cachePath))
            {
                var cachedBitmap = await Task.Run(() => DecodeBitmap(cachePath));
                if (cachedBitmap != null)
                {
                    RunOnUiThread(() =>
                    {
                        _imageView.SetImageBitmap(cachedBitmap);
                        _imageView.Visibility = ViewStates.Visible;
                    });
                    _imageLoaded = true;
                    UpdateSubtitle("喵~ 正在加载...");

                    // 后台尝试更新缓存
                    _ = RefreshCacheAsync(cachePath);
                    TryTransition();
                    return;
                }
            }

            // 2. 使用网络超时取消令牌
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(NetworkTimeoutMs));

            // 3. 后台下载
            var data = await DownloadImageAsync(cts.Token);

            if (data != null)
            {
                var bitmap = await Task.Run(() => BitmapFactory.DecodeByteArray(data, 0, data.Length));
                if (bitmap != null)
                {
                    RunOnUiThread(() =>
                    {
                        _imageView.SetImageBitmap(bitmap);
                        _imageView.Visibility = ViewStates.Visible;
                    });
                    _imageLoaded = true;
                    UpdateSubtitle("喵~ 正在加载...");

                    // 存缓存
                    _ = Task.Run(() =>
                    {
                        try { File.WriteAllBytes(cachePath, data); }
                        catch { /* 缓存写入失败不影响主流程 */ }
                    });
                }
            }
        }
        catch (System.OperationCanceledException)
        {
            // 网络超时，使用纯色背景
        }
        catch
        {
            // 忽略其他错误
        }

        // 无论是否加载成功，都标记完成
        _imageLoaded = true;
        RunOnUiThread(() =>
        {
            _progressBar.Visibility = ViewStates.Gone;
            UpdateSubtitle("喵~ 正在加载...");
        });
        TryTransition();
    }

    /// <summary>后台异步刷新缓存（不阻塞主流程）</summary>
    private async Task RefreshCacheAsync(string cachePath)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(NetworkTimeoutMs));
            var data = await DownloadImageAsync(cts.Token);
            if (data != null)
            {
                await File.WriteAllBytesAsync(cachePath, data);
            }
        }
        catch { /* 静默失败 */ }
    }

    private static async Task<byte[]?> DownloadImageAsync(CancellationToken ct)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(NetworkTimeoutMs) };
            var response = await client.GetAsync(ImageUrl, ct);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadAsByteArrayAsync(ct);
        }
        catch { return null; }
    }

    private static Bitmap? DecodeBitmap(string path)
    {
        try
        {
            var opts = new BitmapFactory.Options { InJustDecodeBounds = true };
            BitmapFactory.DecodeFile(path, opts);

            int targetW = 1080;
            int targetH = 1920;
            int scaleFactor = 1;
            while (opts.OutWidth / scaleFactor > targetW || opts.OutHeight / scaleFactor > targetH)
                scaleFactor *= 2;

            opts.InJustDecodeBounds = false;
            opts.InSampleSize = scaleFactor;

            return BitmapFactory.DecodeFile(path, opts);
        }
        catch { return null; }
    }

    private static string GetCachePath()
    {
        var cacheDir = global::Android.App.Application.Context.CacheDir!.AbsolutePath;
        return System.IO.Path.Combine(cacheDir, "splash_cache.jpg");
    }

    private void UpdateSubtitle(string text)
    {
        RunOnUiThread(() => { _subtitleText.Text = text; });
    }

    /// <summary>条件满足后启动主界面（但不关闭自身，等 MainActivity 初始化完成后关闭）</summary>
    private void TryTransition()
    {
        if (_transitionStarted) return;
        if (!_minTimeReached || !_imageLoaded) return;

        _transitionStarted = true;
        RunOnUiThread(() =>
        {
            var intent = new Intent(this, typeof(MainActivity));
            intent.AddFlags(ActivityFlags.NoAnimation);
            StartActivity(intent);
            // 不调用 Finish()，等 MainActivity 初始化完成后调用 FinishSplash()
        });
    }
}