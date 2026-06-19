using Android.Content;
using Android.OS;
using Android.Provider;
using Android.Views;
using Android.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Data;
using CatClawMusic.UI.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

/// <summary>
/// 通用设置Fragment，包含省电策略、桌面歌词等设置项
/// </summary>
public class GeneralSettingsFragment : Fragment
{
    private TextView? _tvBatteryStatus;
    private Switch? _switchBgAnimation;
    private TextView? _tvCacheSize;
    private TextView? _tvCacheValue;

    public const string PrefsName = "catclaw_prefs";
    public const string KeyBgAnimationEnabled = "bg_animation_enabled";
    public const string KeyCacheSizeGB = "cache_size_gb";

    /// <summary>
    /// 创建通用设置视图，初始化各项设置入口和返回按钮
    /// </summary>
    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
    {
        var view = inflater.Inflate(Resource.Layout.fragment_general_settings, container, false)!;
        var nav = MainApplication.Services.GetRequiredService<INavigationService>();

        var btnBack = view.FindViewById<ImageButton>(Resource.Id.btn_back);
        if (btnBack != null)
            btnBack.Click += (s, e) => nav.GoBack();

        _switchBgAnimation = view.FindViewById<Switch>(Resource.Id.switch_bg_animation);
        if (_switchBgAnimation != null)
        {
            var prefs = Context!.GetSharedPreferences(PrefsName, FileCreationMode.Private);
            _switchBgAnimation.Checked = prefs.GetBoolean(KeyBgAnimationEnabled, false);
            _switchBgAnimation.CheckedChange += (s, e) =>
            {
                var editor = Context!.GetSharedPreferences(PrefsName, FileCreationMode.Private).Edit();
                editor.PutBoolean(KeyBgAnimationEnabled, e.IsChecked);
                editor.Apply();
            };
        }

        var btnBattery = view.FindViewById<View>(Resource.Id.btn_battery_optimization);
        _tvBatteryStatus = view.FindViewById<TextView>(Resource.Id.tv_battery_status);
        if (btnBattery != null)
            btnBattery.Click += (s, e) => OpenBatteryOptimizationSettings();

        var btnDesktopLyric = view.FindViewById<View>(Resource.Id.btn_desktop_lyric);
        if (btnDesktopLyric != null)
            btnDesktopLyric.Click += (s, e) => nav.PushFragment("DesktopLyric");

        // 缓存管理
        _tvCacheSize = view.FindViewById<TextView>(Resource.Id.tv_cache_size);
        _tvCacheValue = view.FindViewById<TextView>(Resource.Id.tv_cache_value);
        var seekbarCache = view.FindViewById<SeekBar>(Resource.Id.seekbar_cache_size);
        if (seekbarCache != null)
        {
            var prefs = Context!.GetSharedPreferences(PrefsName, FileCreationMode.Private);
            int cacheGb = prefs.GetInt(KeyCacheSizeGB, 1);
            seekbarCache.Progress = cacheGb - 1; // SeekBar 0~14 → 1~15GB
            _tvCacheValue!.Text = $"上限：{cacheGb} GB";
            seekbarCache.ProgressChanged += (s, e) =>
            {
                int gb = e.Progress + 1;
                _tvCacheValue!.Text = $"上限：{gb} GB";
                if (e.FromUser)
                {
                    var editor = Context!.GetSharedPreferences(PrefsName, FileCreationMode.Private).Edit();
                    editor.PutInt(KeyCacheSizeGB, gb);
                    editor.Apply();
                }
            };
        }
        UpdateCacheDisplay();

        return view;
    }

    /// <summary>
    /// Fragment恢复时重新检查电池优化状态
    /// </summary>
    public override void OnResume()
    {
        base.OnResume();
        CheckBatteryOptimizationStatus();
        UpdateCacheDisplay();
    }

    /// <summary>
    /// 检查并显示电池优化状态
    /// </summary>
    private void CheckBatteryOptimizationStatus()
    {
        if (Context == null) return;

        bool isIgnoringBatteryOptimizations = IsIgnoringBatteryOptimizations();
        string statusText;
        Android.Graphics.Color statusColor;

        if (isIgnoringBatteryOptimizations)
        {
            statusText = "✅ 已设置为无限制";
            statusColor = Android.Graphics.Color.ParseColor("#4CAF50");
        }
        else
        {
            statusText = "⚠️ 建议设置为无限制";
            statusColor = Android.Graphics.Color.ParseColor("#FF9800");
        }

        _tvBatteryStatus?.Post(() =>
        {
            _tvBatteryStatus.Text = statusText;
            _tvBatteryStatus.SetTextColor(statusColor);
        });
    }

    /// <summary>
    /// 检查当前应用是否已忽略电池优化
    /// </summary>
    private bool IsIgnoringBatteryOptimizations()
    {
        if (Context == null) return false;

        var packageName = Context.PackageName;
        var powerManager = (Android.OS.PowerManager)Context.GetSystemService(Context.PowerService)!;

        if (powerManager == null) return false;

        return powerManager.IsIgnoringBatteryOptimizations(packageName);
    }

    /// <summary>
    /// 打开电池优化设置页面
    /// </summary>
    private void OpenBatteryOptimizationSettings()
    {
        if (Context == null) return;

        var packageName = Context.PackageName;
        bool isIgnoring = IsIgnoringBatteryOptimizations();

        if (isIgnoring)
        {
            Toast.MakeText(Context, "当前已设置为无限制", ToastLength.Short)?.Show();
            return;
        }

        try
        {
            var intent = new Intent();
            intent.SetAction(Settings.ActionRequestIgnoreBatteryOptimizations);
            intent.SetData(Android.Net.Uri.Parse($"package:{packageName}"));
            StartActivity(intent);
        }
        catch
        {
            try
            {
                var intent = new Intent();
                intent.SetAction(Settings.ActionIgnoreBatteryOptimizationSettings);
                StartActivity(intent);

                Toast.MakeText(Context, "请在列表中找到猫爪音乐，设置为无限制", ToastLength.Long)?.Show();
            }
            catch
            {
                Toast.MakeText(Context, "请前往系统设置 > 电池 > 找到猫爪音乐 > 设置为无限制", ToastLength.Long)?.Show();
            }
        }
    }

    /// <summary>
    /// 更新缓存大小显示
    /// </summary>
    private void UpdateCacheDisplay()
    {
        if (Context == null || _tvCacheSize == null) return;

        var cacheDir = Path.Combine(Context.CacheDir!.AbsolutePath, "music_cache");
        long usedBytes = GetDirectorySize(cacheDir);
        string usedText = usedBytes < 1024 * 1024
            ? $"{usedBytes / 1024.0:F1} KB"
            : usedBytes < 1024L * 1024 * 1024
                ? $"{usedBytes / (1024.0 * 1024):F1} MB"
                : $"{usedBytes / (1024.0 * 1024 * 1024):F2} GB";

        var prefs = Context.GetSharedPreferences(PrefsName, FileCreationMode.Private);
        int maxGb = prefs.GetInt(KeyCacheSizeGB, 1);

        _tvCacheSize.Text = $"当前缓存：{usedText}";

        // 超限标红
        if (usedBytes > (long)maxGb * 1024 * 1024 * 1024)
            _tvCacheSize.SetTextColor(Android.Graphics.Color.ParseColor("#F44336"));
        else
            _tvCacheSize.SetTextColor(Android.Graphics.Color.ParseColor("#4CAF50"));
    }

    /// <summary>
    /// 递归计算目录大小
    /// </summary>
    private static long GetDirectorySize(string path)
    {
        if (!Directory.Exists(path)) return 0;
        long size = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { size += new FileInfo(file).Length; } catch { }
            }
        }
        catch { }
        return size;
    }

    /// <summary>
    /// 缓存超限清理：按文件最后写入时间从旧到新删除，直到低于上限。
    /// 同时清理数据库中对应的 CachedSong 记录。
    /// </summary>
    public static async Task EvictCacheAsync(int maxGb)
    {
        try
        {
            var ctx = global::Android.App.Application.Context;
            var cacheDir = Path.Combine(ctx.CacheDir!.AbsolutePath, "music_cache");
            if (!Directory.Exists(cacheDir)) return;

            long maxBytes = (long)maxGb * 1024 * 1024 * 1024;
            long currentSize = GetDirectorySize(cacheDir);
            if (currentSize <= maxBytes) return;

            System.Diagnostics.Debug.WriteLine($"[CacheEvict] 当前 {currentSize / (1024.0 * 1024):F1} MB > 上限 {maxGb} GB，开始清理");

            var files = Directory.EnumerateFiles(cacheDir, "*", SearchOption.AllDirectories)
                .Select(f =>
                {
                    try { return new { Path = f, Info = new FileInfo(f) }; }
                    catch { return null; }
                })
                .Where(x => x != null)
                .OrderBy(x => x!.Info.LastWriteTimeUtc)
                .ToList();

            var db = MainApplication.Services.GetService<MusicDatabase>();
            if (db != null) await db.EnsureInitializedAsync();

            foreach (var file in files)
            {
                if (currentSize <= maxBytes) break;
                try
                {
                    long fileSize = file!.Info.Length;
                    file.Info.Delete();
                    currentSize -= fileSize;
                    System.Diagnostics.Debug.WriteLine($"[CacheEvict] 删除 {Path.GetFileName(file.Path)} ({fileSize / (1024.0 * 1024):F1} MB)");

                    // 尝试清理数据库记录（文件名通常是 songId 的哈希）
                    if (db != null)
                    {
                        var fileName = Path.GetFileNameWithoutExtension(file.Path);
                        if (int.TryParse(fileName, out int songId))
                            await db.DeleteCachedSongAsync(songId);
                    }
                }
                catch { }
            }

            System.Diagnostics.Debug.WriteLine($"[CacheEvict] 清理完成，剩余 {currentSize / (1024.0 * 1024):F1} MB");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CacheEvict] 清理失败: {ex.Message}");
        }
    }
}
