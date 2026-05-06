using Android.Content;
using Android.OS;
using Android.Provider;
using Android.Views;
using Android.Widget;
using AndroidX.Core.App;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Data;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

public class GeneralSettingsFragment : Fragment
{
    private TextView? _tvBatteryStatus;
    private TextView? _tvNotificationStatus;
    private Spinner? _spinnerTheme;
    private Spinner? _spinnerDarkMode;
    private IThemeService? _themeService;
    private bool _isThemeSpinnerInitialized = false;
    private bool _isDarkModeSpinnerInitialized = false;

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
    {
        var view = inflater.Inflate(Resource.Layout.fragment_general_settings, container, false)!;
        var nav = MainApplication.Services.GetRequiredService<INavigationService>();
        _themeService = MainApplication.Services.GetRequiredService<IThemeService>();

        // 返回按钮
        var btnBack = view.FindViewById<ImageButton>(Resource.Id.btn_back);
        if (btnBack != null)
            btnBack.Click += (s, e) => nav.GoBack();

        // 🎨 主题选择
        _spinnerTheme = view.FindViewById<Spinner>(Resource.Id.spinner_theme);
        if (_spinnerTheme != null)
        {
            SetupThemeSpinner();
        }

        // 🌓 深色模式选择
        _spinnerDarkMode = view.FindViewById<Spinner>(Resource.Id.spinner_dark_mode);
        if (_spinnerDarkMode != null)
        {
            SetupDarkModeSpinner();
        }

        // 🔋 省电策略设置
        var btnBattery = view.FindViewById<View>(Resource.Id.btn_battery_optimization);
        _tvBatteryStatus = view.FindViewById<TextView>(Resource.Id.tv_battery_status);
        if (btnBattery != null)
            btnBattery.Click += (s, e) => OpenBatteryOptimizationSettings();

        // 🔔 通知权限设置
        var btnNotification = view.FindViewById<View>(Resource.Id.btn_notification_permission);
        _tvNotificationStatus = view.FindViewById<TextView>(Resource.Id.tv_notification_status);
        if (btnNotification != null)
            btnNotification.Click += (s, e) => OpenNotificationSettings();

        // 🎵 桌面与歌词
        var btnDesktopLyric = view.FindViewById<View>(Resource.Id.btn_desktop_lyric);
        if (btnDesktopLyric != null)
            btnDesktopLyric.Click += (s, e) => nav.PushFragment("DesktopLyric");

        return view;
    }

    private void SetupThemeSpinner()
    {
        if (_themeService == null || _spinnerTheme == null) return;

        // 创建主题名称列表
        var themeNames = new List<string>
        {
            "💜 紫色主题",
            "💗 粉色主题",
            "💙 蓝色主题",
            "💚 绿色主题",
            "🧡 橙色主题"
        };

        // 创建适配器
        var adapter = new ArrayAdapter<string>(
            Context!,
            Android.Resource.Layout.SimpleSpinnerItem,
            themeNames);
        adapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
        
        _spinnerTheme.Adapter = adapter;

        // 设置当前选中的主题
        var currentTheme = _themeService.CurrentTheme;
        _spinnerTheme.SetSelection((int)currentTheme, false);

        // 监听选择变化
        _spinnerTheme.ItemSelected += (sender, e) =>
        {
            if (!_isThemeSpinnerInitialized)
            {
                _isThemeSpinnerInitialized = true;
                return;
            }

            var selectedTheme = (AppTheme)e.Position;
            if (selectedTheme != _themeService.CurrentTheme)
            {
                _themeService.SetTheme(selectedTheme);
                // 重启活动以应用新主题
                RestartActivityForTheme();
            }
        };
    }

    private void SetupDarkModeSpinner()
    {
        if (_themeService == null || _spinnerDarkMode == null) return;

        // 创建深色模式选项列表
        var darkModeNames = new List<string>
        {
            "☀️ 浅色模式",
            "🌙 深色模式",
            "🔄 跟随系统"
        };

        // 创建适配器
        var adapter = new ArrayAdapter<string>(
            Context!,
            Android.Resource.Layout.SimpleSpinnerItem,
            darkModeNames);
        adapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
        
        _spinnerDarkMode.Adapter = adapter;

        // 设置当前选中的深色模式
        var currentDarkMode = _themeService.DarkModeSetting;
        _spinnerDarkMode.SetSelection((int)currentDarkMode, false);

        // 监听选择变化
        _spinnerDarkMode.ItemSelected += (sender, e) =>
        {
            if (!_isDarkModeSpinnerInitialized)
            {
                _isDarkModeSpinnerInitialized = true;
                return;
            }

            var selectedDarkMode = (DarkModeSetting)e.Position;
            if (selectedDarkMode != _themeService.DarkModeSetting)
            {
                _themeService.SetDarkModeSetting(selectedDarkMode);
                // 重启活动以应用新主题
                RestartActivityForTheme();
            }
        };
    }

    private void RestartActivityForTheme()
    {
        if (Activity == null) return;

        // 重启活动
        var intent = Activity.Intent;
        Activity.Finish();
        StartActivity(intent);
    }

    public override void OnResume()
    {
        base.OnResume();
        // 每次恢复页面时重新检查状态
        CheckBatteryOptimizationStatus();
        CheckNotificationStatus();
    }

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

    private bool AreNotificationsEnabled()
    {
        if (Context == null) return false;

        var notificationManager = (Android.App.NotificationManager)Context.GetSystemService(Context.NotificationService)!;
        if (notificationManager == null) return false;

        // 对于 Android 13+，我们也可以检查 POST_NOTIFICATIONS 权限
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
        {
            return notificationManager.AreNotificationsEnabled();
        }

        // 对于旧版本，默认认为通知是开启的
        return true;
    }

    private void OpenNotificationSettings()
    {
        if (Context == null) return;

        bool areEnabled = AreNotificationsEnabled();

        if (areEnabled)
        {
            // 已经开启，显示提示
            Toast.MakeText(Context, "当前通知权限已开启", ToastLength.Short)?.Show();
            return;
        }

        try
        {
            // 尝试直接跳转到应用的通知设置页面
            var intent = new Intent();
            intent.SetAction(Settings.ActionAppNotificationSettings);
            intent.PutExtra(Settings.ExtraAppPackage, Context.PackageName);
            StartActivity(intent);
        }
        catch
        {
            try
            {
                // 如果上面的方法失败，跳转到应用详情页面
                var intent = new Intent();
                intent.SetAction(Settings.ActionApplicationDetailsSettings);
                intent.SetData(Android.Net.Uri.Parse($"package:{Context.PackageName}"));
                StartActivity(intent);

                Toast.MakeText(Context, "请在设置中找到通知权限并开启", ToastLength.Long)?.Show();
            }
            catch
            {
                // 如果都失败，提示用户手动操作
                Toast.MakeText(Context, "请前往系统设置 > 应用 > 猫爪音乐 > 通知，开启通知权限", ToastLength.Long)?.Show();
            }
        }
    }

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

    private bool IsIgnoringBatteryOptimizations()
    {
        if (Context == null) return false;

        var packageName = Context.PackageName;
        var powerManager = (Android.OS.PowerManager)Context.GetSystemService(Context.PowerService)!;

        if (powerManager == null) return false;

        // 检查是否已经忽略电池优化（即设置为无限制）
        return powerManager.IsIgnoringBatteryOptimizations(packageName);
    }

    private void OpenBatteryOptimizationSettings()
    {
        if (Context == null) return;

        var packageName = Context.PackageName;
        bool isIgnoring = IsIgnoringBatteryOptimizations();

        if (isIgnoring)
        {
            // 已经是无限制，显示提示
            Toast.MakeText(Context, "当前已设置为无限制", ToastLength.Short)?.Show();
            return;
        }

        try
        {
            // 尝试直接跳转到应用的电池优化设置页面
            var intent = new Intent();
            intent.SetAction(Settings.ActionRequestIgnoreBatteryOptimizations);
            intent.SetData(Android.Net.Uri.Parse($"package:{packageName}"));
            StartActivity(intent);
        }
        catch
        {
            try
            {
                // 如果上面的方法失败，跳转到电池优化列表页面
                var intent = new Intent();
                intent.SetAction(Settings.ActionIgnoreBatteryOptimizationSettings);
                StartActivity(intent);

                Toast.MakeText(Context, "请在列表中找到猫爪音乐，设置为无限制", ToastLength.Long)?.Show();
            }
            catch
            {
                // 如果都失败，提示用户手动操作
                Toast.MakeText(Context, "请前往系统设置 > 电池 > 找到猫爪音乐 > 设置为无限制", ToastLength.Long)?.Show();
            }
        }
    }
}
