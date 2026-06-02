using Android.Content;
using Android.OS;
using Android.Provider;
using Android.Views;
using Android.Widget;
using AndroidX.Core.App;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Data;
using CatClawMusic.UI.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

/// <summary>
/// 通用设置Fragment，包含省电策略、通知权限、桌面歌词、权限管理等设置项
/// </summary>
public class GeneralSettingsFragment : Fragment
{
    private TextView? _tvBatteryStatus;
    private TextView? _tvNotificationStatus;
    private TextView? _tvPermissionStatus;
    private Switch? _switchBgAnimation;

    public const string PrefsName = "catclaw_prefs";
    public const string KeyBgAnimationEnabled = "bg_animation_enabled";

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

        var btnNotification = view.FindViewById<View>(Resource.Id.btn_notification_permission);
        _tvNotificationStatus = view.FindViewById<TextView>(Resource.Id.tv_notification_status);
        if (btnNotification != null)
            btnNotification.Click += (s, e) => OpenNotificationSettings();

        var btnDesktopLyric = view.FindViewById<View>(Resource.Id.btn_desktop_lyric);
        if (btnDesktopLyric != null)
            btnDesktopLyric.Click += (s, e) => nav.PushFragment("DesktopLyric");

        var btnPermission = view.FindViewById<View>(Resource.Id.btn_permission_management);
        _tvPermissionStatus = view.FindViewById<TextView>(Resource.Id.tv_permission_status);
        if (btnPermission != null)
            btnPermission.Click += (s, e) => ShowPermissionManagementDialog();

        return view;
    }

    /// <summary>
    /// Fragment恢复时重新检查各项权限状态
    /// </summary>
    public override void OnResume()
    {
        base.OnResume();
        CheckBatteryOptimizationStatus();
        CheckNotificationStatus();
        CheckPermissionStatus();
    }

    private void ShowPermissionManagementDialog()
    {
        var ctx = Context;
        if (ctx == null) return;

        var permService = MainApplication.Services.GetRequiredService<IPermissionService>();

        var dialog = new GlassDialog(ctx)
            .SetTitle("🔐 权限管理");

        var storageGranted = permService.CheckStoragePermissionAsync().Result;
        var overlayGranted = permService.CheckOverlayPermissionAsync().Result;
        var notificationGranted = AreNotificationsEnabled();
        var recordAudioGranted = ctx.CheckSelfPermission(Android.Manifest.Permission.RecordAudio)
            == Android.Content.PM.Permission.Granted;

        dialog.AddMessage("以下是猫爪音乐需要的权限及其用途：");

        dialog.AddItem($"🎤 录音权限（频谱可视化） — {(recordAudioGranted ? "✅ 已开启" : "❌ 未开启")}", () =>
        {
            if (recordAudioGranted)
            {
                Toast.MakeText(ctx, "录音权限已开启", ToastLength.Short)?.Show();
            }
            else
            {
                try
                {
                    var intent = new Intent(Settings.ActionApplicationDetailsSettings);
                    intent.SetData(Android.Net.Uri.Parse($"package:{ctx.PackageName}"));
                    StartActivity(intent);
                    Toast.MakeText(ctx, "请在设置中开启录音权限", ToastLength.Long)?.Show();
                }
                catch
                {
                    Toast.MakeText(ctx, "请前往系统设置 > 猫爪音乐 > 开启录音权限", ToastLength.Long)?.Show();
                }
            }
        });

        dialog.AddItem($"📁 媒体/存储权限（扫描音乐） — {(storageGranted ? "✅ 已开启" : "❌ 未开启")}", () =>
        {
            if (storageGranted)
            {
                Toast.MakeText(ctx, "存储权限已开启", ToastLength.Short)?.Show();
            }
            else
            {
                try
                {
                    var intent = new Intent(Settings.ActionApplicationDetailsSettings);
                    intent.SetData(Android.Net.Uri.Parse($"package:{ctx.PackageName}"));
                    StartActivity(intent);
                    Toast.MakeText(ctx, "请在设置中开启媒体权限", ToastLength.Long)?.Show();
                }
                catch
                {
                    Toast.MakeText(ctx, "请前往系统设置 > 猫爪音乐 > 开启媒体权限", ToastLength.Long)?.Show();
                }
            }
        });

        dialog.AddItem($"🔔 通知权限（播放控制） — {(notificationGranted ? "✅ 已开启" : "❌ 未开启")}", () =>
        {
            OpenNotificationSettings();
        });

        dialog.AddItem($"🖥️ 悬浮窗权限（桌面歌词） — {(overlayGranted ? "✅ 已开启" : "❌ 未开启")}", () =>
        {
            _ = permService.RequestOverlayPermissionAsync();
        });

        dialog.AddNegativeButton("关闭");
        dialog.Show();
    }

    private void CheckPermissionStatus()
    {
        if (Context == null) return;

        var permService = MainApplication.Services.GetRequiredService<IPermissionService>();
        var storageGranted = permService.CheckStoragePermissionAsync().Result;
        var notificationGranted = AreNotificationsEnabled();
        var recordAudioGranted = Context.CheckSelfPermission(Android.Manifest.Permission.RecordAudio)
            == Android.Content.PM.Permission.Granted;
        var overlayGranted = permService.CheckOverlayPermissionAsync().Result;

        int total = 4;
        int granted = (storageGranted ? 1 : 0) + (notificationGranted ? 1 : 0)
            + (recordAudioGranted ? 1 : 0) + (overlayGranted ? 1 : 0);

        string statusText;
        Android.Graphics.Color statusColor;
        if (granted == total)
        {
            statusText = "✅ 所有权限已开启";
            statusColor = Android.Graphics.Color.ParseColor("#4CAF50");
        }
        else
        {
            statusText = $"⚠️ {granted}/{total} 项权限已开启";
            statusColor = Android.Graphics.Color.ParseColor("#FF9800");
        }

        _tvPermissionStatus?.Post(() =>
        {
            _tvPermissionStatus.Text = statusText;
            _tvPermissionStatus.SetTextColor(statusColor);
        });
    }

    /// <summary>
    /// 检查并显示通知权限状态
    /// </summary>
    private void CheckNotificationStatus()
    {
        if (Context == null) return;

        bool areNotificationsEnabled = AreNotificationsEnabled();
        string statusText;
        Android.Graphics.Color statusColor;

        if (areNotificationsEnabled)
        {
            statusText = "✅ 通知权限已开启";
            statusColor = Android.Graphics.Color.ParseColor("#4CAF50");
        }
        else
        {
            statusText = "⚠️ 建议开启通知权限";
            statusColor = Android.Graphics.Color.ParseColor("#FF9800");
        }

        _tvNotificationStatus?.Post(() =>
        {
            _tvNotificationStatus.Text = statusText;
            _tvNotificationStatus.SetTextColor(statusColor);
        });
    }

    /// <summary>
    /// 检查是否已开启通知权限
    /// </summary>
    private bool AreNotificationsEnabled()
    {
        if (Context == null) return false;

        var notificationManager = (Android.App.NotificationManager)Context.GetSystemService(Context.NotificationService)!;
        if (notificationManager == null) return false;

        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
        {
            return notificationManager.AreNotificationsEnabled();
        }

        return true;
    }

    /// <summary>
    /// 打开系统通知设置页面
    /// </summary>
    private void OpenNotificationSettings()
    {
        if (Context == null) return;

        bool areEnabled = AreNotificationsEnabled();

        if (areEnabled)
        {
            Toast.MakeText(Context, "当前通知权限已开启", ToastLength.Short)?.Show();
            return;
        }

        try
        {
            var intent = new Intent();
            intent.SetAction(Settings.ActionAppNotificationSettings);
            intent.PutExtra(Settings.ExtraAppPackage, Context.PackageName);
            StartActivity(intent);
        }
        catch
        {
            try
            {
                var intent = new Intent();
                intent.SetAction(Settings.ActionApplicationDetailsSettings);
                intent.SetData(Android.Net.Uri.Parse($"package:{Context.PackageName}"));
                StartActivity(intent);

                Toast.MakeText(Context, "请在设置中找到通知权限并开启", ToastLength.Long)?.Show();
            }
            catch
            {
                Toast.MakeText(Context, "请前往系统设置 > 应用 > 猫爪音乐 > 通知，开启通知权限", ToastLength.Long)?.Show();
            }
        }
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
}
