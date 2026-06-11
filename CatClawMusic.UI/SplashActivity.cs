using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;
using CatClawMusic.UI.Fragments;
using CatClawMusic.UI.Services;
using CatClawMusic.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI;

/// <summary>
/// 启动画面 Activity：根据用户设置加载启动背景图，等待主界面初始化完成后再跳转。
/// 支持自定义API图片源和本地图片，网络图片缓存在本地。
/// </summary>
[Activity(
    Theme = "@style/CatClaw.SplashTheme",
    MainLauncher = true,
    ScreenOrientation = Android.Content.PM.ScreenOrientation.Portrait)]
public class SplashActivity : global::AndroidX.AppCompat.App.AppCompatActivity
{
    private const int MinDisplayMs = 1500;
    private const int NetworkTimeoutMs = 4000;

    private ImageView _imageView = null!;
    private ProgressBar _progressBar = null!;
    private TextView _subtitleText = null!;
    private TextView _initStatusText = null!;

    private bool _imageLoaded;
    private bool _minTimeReached;
    private bool _initCompleted;
    private bool _transitionStarted;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

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
        _initStatusText = FindViewById<TextView>(Resource.Id.splash_init_status)!;

        // 最小显示时间计时器
        _ = Task.Delay(MinDisplayMs).ContinueWith(_ =>
        {
            _minTimeReached = true;
            TryTransition();
        });

        // 加载启动图
        _ = LoadSplashImageAsync();

        // 在启动页等待核心初始化完成，确保跳转后主界面立即可用
        _ = WaitForInitAsync();
    }

    /// <summary>等待数据库初始化和播放状态恢复完成</summary>
    private async Task WaitForInitAsync()
    {
        try
        {
            UpdateInitStatus("正在初始化数据库...");
            var db = MainApplication.Services.GetRequiredService<MusicDatabase>();
            await db.EnsureInitializedAsync();
            UpdateInitStatus("数据库就绪");

            UpdateInitStatus("正在恢复播放状态...");
            var queue = MainApplication.Services.GetRequiredService<PlayQueue>();
            var npVm = MainApplication.Services.GetRequiredService<NowPlayingViewModel>();
            PlaybackStateManager.RestorePrefsToViewModel(queue, npVm);
            UpdateInitStatus("播放状态已恢复");
        }
        catch { /* 异常已由各任务内部处理 */ }

        _initCompleted = true;
        UpdateSubtitle("喵~ 准备就绪！");
        UpdateInitStatus("加载完成");
        TryTransition();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
    }

    /// <summary>加载启动图：根据设置选择图片来源（默认猫图 > 自定义图片 > 自定义API），优先本地缓存</summary>
    private async Task LoadSplashImageAsync()
    {
        var ctx = global::Android.App.Application.Context;
        var sourceMode = SplashSettingsFragment.GetSourceMode(ctx);
        string cachePath = GetCachePath();

        // 模式0：默认猫图（浅色模式=白猫，深色模式=黑猫）
        if (sourceMode == 0)
        {
            RunOnUiThread(() => ShowDefaultCatImage());
            _imageLoaded = true;
            UpdateSubtitle("喵~ 正在加载...");
            TryTransition();
            return;
        }

        // 模式2：自定义本地图片
        if (sourceMode == 2)
        {
            var customPath = SplashSettingsFragment.GetCustomImageFilePath();
            if (File.Exists(customPath))
            {
                var bitmap = await Task.Run(() => DecodeBitmap(customPath));
                if (bitmap != null)
                {
                    RunOnUiThread(() =>
                    {
                        _imageView.SetImageBitmap(bitmap);
                        _imageView.Visibility = ViewStates.Visible;
                    });
                    _imageLoaded = true;
                    UpdateSubtitle("喵~ 正在加载...");
                    TryTransition();
                    return;
                }
            }
        }

        // 模式1：自定义API
        var imageUrl = SplashSettingsFragment.GetCustomApiUrl(ctx);
        if (string.IsNullOrEmpty(imageUrl))
        {
            // 未配置API地址，使用默认猫图（浅色模式=白猫，深色模式=黑猫）
            ShowDefaultCatImage();
            _imageLoaded = true;
            UpdateSubtitle("喵~ 正在加载...");
            TryTransition();
            return;
        }

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
                    _ = RefreshCacheAsync(imageUrl, cachePath);
                    TryTransition();
                    return;
                }
            }

            // 2. 使用网络超时取消令牌
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(NetworkTimeoutMs));

            // 3. 后台下载
            var data = await DownloadImageAsync(imageUrl, cts.Token);

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
            // 网络图片加载失败时回退到默认猫图
            if (_imageView.Visibility != ViewStates.Visible)
                ShowDefaultCatImage();
            _progressBar.Visibility = ViewStates.Gone;
            UpdateSubtitle("喵~ 正在加载...");
        });
        TryTransition();
    }

    /// <summary>后台异步刷新缓存（不阻塞主流程）</summary>
    private async Task RefreshCacheAsync(string url, string cachePath)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(NetworkTimeoutMs));
            var data = await DownloadImageAsync(url, cts.Token);
            if (data != null)
            {
                await File.WriteAllBytesAsync(cachePath, data);
            }
        }
        catch { /* 静默失败 */ }
    }

    /// <summary>从URL下载图片数据</summary>
    private async Task<byte[]?> DownloadImageAsync(string url, CancellationToken ct)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(NetworkTimeoutMs) };
            var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadAsByteArrayAsync(ct);
        }
        catch { return null; }
    }

    /// <summary>从文件路径解码Bitmap，自动计算采样率避免OOM</summary>
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

    /// <summary>根据当前深浅模式显示默认猫图：浅色=白猫，深色=黑猫</summary>
    private void ShowDefaultCatImage()
    {
        bool isDark = IsDarkMode();
        int resId = isDark
            ? Resource.Drawable.splash_cat_dark
            : Resource.Drawable.splash_cat_light;

        _imageView.SetImageResource(resId);
        _imageView.Visibility = ViewStates.Visible;

        // 深色模式用深色背景，浅色模式用浅色背景
        if (_imageView.Parent is View parent)
            parent.SetBackgroundColor(isDark ? Color.ParseColor("#0A0A0A") : Color.ParseColor("#F5F5F5"));
    }

    /// <summary>判断当前是否为深色模式</summary>
    private bool IsDarkMode()
    {
        try
        {
            var uiMode = Resources?.Configuration?.UiMode ?? Android.Content.Res.UiMode.NightUndefined;
            return (uiMode & Android.Content.Res.UiMode.NightMask) == Android.Content.Res.UiMode.NightYes;
        }
        catch { return true; } // 默认深色
    }

    /// <summary>获取启动图缓存路径</summary>
    private static string GetCachePath()
    {
        var cacheDir = global::Android.App.Application.Context.CacheDir!.AbsolutePath;
        return System.IO.Path.Combine(cacheDir, "splash_cache.jpg");
    }

    private void UpdateSubtitle(string text)
    {
        RunOnUiThread(() => { _subtitleText.Text = text; });
    }

    /// <summary>更新左下角初始化状态文字（白色30%透明）</summary>
    private void UpdateInitStatus(string text)
    {
        RunOnUiThread(() => { _initStatusText.Text = text; });
    }

    /// <summary>所有条件满足后跳转到主界面并关闭启动页</summary>
    /// <remarks>必须同时满足：最小显示时间、图片加载完成、核心初始化完成</remarks>
    private void TryTransition()
    {
        if (_transitionStarted) return;
        if (!_minTimeReached || !_imageLoaded || !_initCompleted) return;

        _transitionStarted = true;
        RunOnUiThread(() =>
        {
            var intent = new Intent(this, typeof(MainActivity));
            intent.AddFlags(ActivityFlags.NoAnimation);
            StartActivity(intent);
            Finish();
            OverridePendingTransition(0, 0);
        });
    }
}