using System.IO;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Provider;
using Android.Views;
using Android.Widget;
using AndroidX.Activity.Result;
using CatClawMusic.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Path = System.IO.Path;

namespace CatClawMusic.UI.Fragments;

/// <summary>
/// 启动页设置页面，支持自定义API图片源和本地图片两种模式。
/// </summary>
public class SplashSettingsFragment : SettingsSubPageFragment
{
    private const string PrefsName = "splash_prefs";
    private const string KeySourceMode = "source_mode"; // 0=default, 1=custom_api, 2=custom_image
    private const string KeyCustomApi = "custom_api_url";
    private const string CacheFileName = "splash_cache.jpg";
    private const string CustomImageFileName = "splash_custom.jpg";

    private ImageView? _ivPreview;
    private RadioGroup? _rgSource;
    private View? _cardCustomApi;
    private View? _cardCustomImage;
    private EditText? _etCustomApi;
    private TextView? _tvImageInfo;

    private ActivityResultLauncher? _pickImageLauncher;

    protected override string GetTitle() => "启动页设置";

    public override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _pickImageLauncher = RegisterForActivityResult(
            new AndroidX.Activity.Result.Contract.ActivityResultContracts.OpenDocument(),
            new PickImageCallback(this));
    }

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
        => inflater.Inflate(Resource.Layout.fragment_splash_settings, container, false)!;

    protected override void OnSubViewCreated(View view, Bundle? state)
    {
        _ivPreview = view.FindViewById<ImageView>(Resource.Id.iv_splash_preview);
        _rgSource = view.FindViewById<RadioGroup>(Resource.Id.rg_image_source);
        _cardCustomApi = view.FindViewById<View>(Resource.Id.card_custom_api);
        _cardCustomImage = view.FindViewById<View>(Resource.Id.card_custom_image);
        _etCustomApi = view.FindViewById<EditText>(Resource.Id.et_custom_api);
        _tvImageInfo = view.FindViewById<TextView>(Resource.Id.tv_image_info);

        var prefs = Context!.GetSharedPreferences(PrefsName, FileCreationMode.Private);
        var mode = prefs.GetInt(KeySourceMode, 0);
        var customApi = prefs.GetString(KeyCustomApi, "") ?? "";

        // 恢复选中状态（0=默认, 1=自定义API, 2=自定义图片）
        var rbId = mode switch
        {
            2 => Resource.Id.rb_custom_image,
            1 => Resource.Id.rb_custom_api,
            _ => Resource.Id.rb_default_image
        };
        _rgSource?.Check(rbId);
        if (_etCustomApi != null && !string.IsNullOrEmpty(customApi))
            _etCustomApi.Text = customApi;

        UpdateVisibility(mode);
        LoadPreview();

        // 模式切换
        if (_rgSource != null)
        {
            _rgSource.CheckedChange += (s, e) =>
            {
                var selectedMode = e.CheckedId switch
                {
                    Resource.Id.rb_custom_image => 2,
                    Resource.Id.rb_custom_api => 1,
                    _ => 0 // 默认
                };
                prefs.Edit().PutInt(KeySourceMode, selectedMode).Apply();
                UpdateVisibility(selectedMode);
                LoadPreview();
            };
        }

        // 测试并保存自定义API
        view.FindViewById<Google.Android.Material.Button.MaterialButton>(Resource.Id.btn_test_api)?
            .SetOnClickListener(new ClickListener(() => TestAndSaveCustomApi()));

        // 选择自定义图片
        view.FindViewById<Google.Android.Material.Button.MaterialButton>(Resource.Id.btn_pick_image)?
            .SetOnClickListener(new ClickListener(() => PickImage()));

        // 清除缓存
        view.FindViewById<Google.Android.Material.Button.MaterialButton>(Resource.Id.btn_clear_cache)?
            .SetOnClickListener(new ClickListener(() => ClearCache()));
    }

    private void UpdateVisibility(int mode)
    {
        if (_cardCustomApi != null)
            _cardCustomApi.Visibility = mode == 1 ? ViewStates.Visible : ViewStates.Gone;
        if (_cardCustomImage != null)
            _cardCustomImage.Visibility = mode == 2 ? ViewStates.Visible : ViewStates.Gone;
    }

    /// <summary>加载预览图</summary>
    private void LoadPreview()
    {
        if (_ivPreview == null || Context == null) return;

        var prefs = Context.GetSharedPreferences(PrefsName, FileCreationMode.Private);
        var mode = prefs.GetInt(KeySourceMode, 0);

        // 默认模式：显示默认猫图
        if (mode == 0)
        {
            bool isDark = (Resources?.Configuration?.UiMode ?? Android.Content.Res.UiMode.NightUndefined & Android.Content.Res.UiMode.NightMask) == Android.Content.Res.UiMode.NightYes;
            _ivPreview.SetImageResource(isDark ? Resource.Drawable.splash_cat_dark : Resource.Drawable.splash_cat_light);
            return;
        }

        string? imagePath = mode switch
        {
            2 => GetCustomImagePath(),
            _ => GetCachePath()
        };

        if (File.Exists(imagePath))
        {
            var bitmap = DecodeBitmap(imagePath);
            if (bitmap != null)
            {
                _ivPreview.SetImageBitmap(bitmap);
                return;
            }
        }

        _ivPreview.SetImageResource(Resource.Drawable.splash_screen);
    }

    /// <summary>测试自定义API并保存</summary>
    private async void TestAndSaveCustomApi()
    {
        if (_etCustomApi == null || Context == null) return;

        var url = _etCustomApi.Text?.Trim();
        if (string.IsNullOrEmpty(url))
        {
            Toast.MakeText(Context, "请输入 API 地址", ToastLength.Short)?.Show();
            return;
        }

        if (!url.StartsWith("http"))
        {
            Toast.MakeText(Context, "请输入有效的 HTTP/HTTPS 地址", ToastLength.Short)?.Show();
            return;
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            var response = await client.GetAsync(url);
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";

            if (response.IsSuccessStatusCode && (contentType.StartsWith("image/") || contentType.StartsWith("text/html")))
            {
                var data = await response.Content.ReadAsByteArrayAsync();
                if (data.Length > 1000)
                {
                    // 保存API地址
                    var prefs = Context.GetSharedPreferences(PrefsName, FileCreationMode.Private);
                    prefs.Edit().PutString(KeyCustomApi, url).Apply();

                    // 更新缓存
                    try { await File.WriteAllBytesAsync(GetCachePath(), data); } catch { }

                    Activity?.RunOnUiThread(() =>
                    {
                        Toast.MakeText(Context, "API 测试成功，已保存", ToastLength.Short)?.Show();
                        LoadPreview();
                    });
                    return;
                }
            }

            Activity?.RunOnUiThread(() =>
                Toast.MakeText(Context, "API 未返回有效图片", ToastLength.Short)?.Show());
        }
        catch (System.Exception ex)
        {
            Activity?.RunOnUiThread(() =>
                Toast.MakeText(Context, $"测试失败: {ex.Message}", ToastLength.Long)?.Show());
        }
    }

    /// <summary>选择本地图片</summary>
    private void PickImage()
    {
        try
        {
            _pickImageLauncher?.Launch(new[] { "image/*" });
        }
        catch
        {
            Toast.MakeText(Context, "无法打开图片选择器", ToastLength.Short)?.Show();
        }
    }

    /// <summary>处理图片选择结果</summary>
    private void OnImagePicked(Android.Net.Uri? uri)
    {
        if (uri == null || Context == null) return;

        try
        {
            var inputStream = Context.ContentResolver?.OpenInputStream(uri);
            if (inputStream == null) return;

            var data = new byte[inputStream.Length];
            inputStream.Read(data, 0, data.Length);
            inputStream.Close();

            // 保存到自定义图片路径
            File.WriteAllBytes(GetCustomImagePath(), data);

            // 同时更新缓存
            try { File.WriteAllBytes(GetCachePath(), data); } catch { }

            if (_tvImageInfo != null)
                _tvImageInfo.Text = $"已选择自定义图片 ({data.Length / 1024}KB)";

            LoadPreview();
            Toast.MakeText(Context, "启动图片已更新", ToastLength.Short)?.Show();
        }
        catch (System.Exception ex)
        {
            Toast.MakeText(Context, $"图片保存失败: {ex.Message}", ToastLength.Long)?.Show();
        }
    }

    /// <summary>清除缓存图片</summary>
    private void ClearCache()
    {
        if (Context == null) return;
        try
        {
            var cachePath = GetCachePath();
            if (File.Exists(cachePath)) File.Delete(cachePath);
            Toast.MakeText(Context, "缓存已清除", ToastLength.Short)?.Show();
            LoadPreview();
        }
        catch (System.Exception ex)
        {
            Toast.MakeText(Context, $"清除失败: {ex.Message}", ToastLength.Long)?.Show();
        }
    }

    /// <summary>获取缓存文件路径</summary>
    private static string GetCachePath()
    {
        var cacheDir = global::Android.App.Application.Context.CacheDir!.AbsolutePath;
        return Path.Combine(cacheDir, CacheFileName);
    }

    /// <summary>获取自定义图片文件路径</summary>
    private static string GetCustomImagePath()
    {
        var filesDir = global::Android.App.Application.Context.FilesDir!.AbsolutePath;
        return Path.Combine(filesDir, CustomImageFileName);
    }

    /// <summary>从路径解码Bitmap，自动计算采样率避免OOM</summary>
    /// <param name="path">图片文件路径</param>
    private static Bitmap? DecodeBitmap(string path)
    {
        try
        {
            var opts = new BitmapFactory.Options { InJustDecodeBounds = true };
            BitmapFactory.DecodeFile(path, opts);

            int targetW = 540;
            int targetH = 960;
            int scaleFactor = 1;
            while (opts.OutWidth / scaleFactor > targetW || opts.OutHeight / scaleFactor > targetH)
                scaleFactor *= 2;

            opts.InJustDecodeBounds = false;
            opts.InSampleSize = scaleFactor;

            return BitmapFactory.DecodeFile(path, opts);
        }
        catch { return null; }
    }

    /// <summary>获取当前配置的图片来源模式</summary>
    public static int GetSourceMode(Android.Content.Context ctx)
    {
        var prefs = ctx.GetSharedPreferences(PrefsName, FileCreationMode.Private);
        return prefs.GetInt(KeySourceMode, 0);
    }

    /// <summary>获取自定义API地址</summary>
    public static string GetCustomApiUrl(Android.Content.Context ctx)
    {
        var prefs = ctx.GetSharedPreferences(PrefsName, FileCreationMode.Private);
        return prefs.GetString(KeyCustomApi, "") ?? "";
    }

    /// <summary>获取自定义图片路径</summary>
    public static string GetCustomImageFilePath()
    {
        var filesDir = global::Android.App.Application.Context.FilesDir!.AbsolutePath;
        return Path.Combine(filesDir, CustomImageFileName);
    }

    /// <summary>图片选择回调</summary>
    private class PickImageCallback : Java.Lang.Object, IActivityResultCallback
    {
        private readonly SplashSettingsFragment _fragment;
        public PickImageCallback(SplashSettingsFragment fragment) => _fragment = fragment;
        public void OnActivityResult(Java.Lang.Object? result)
        {
            var uri = result as Android.Net.Uri;
            _fragment.OnImagePicked(uri);
        }
    }

    private class ClickListener : Java.Lang.Object, View.IOnClickListener
    {
        private readonly Action _action;
        public ClickListener(Action action) => _action = action;
        public void OnClick(View? v) => _action();
    }
}
