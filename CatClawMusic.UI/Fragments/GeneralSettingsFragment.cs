using Android.Content;
using Android.OS;
using Android.Provider;
using Android.Views;
using Android.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
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

        var btnDesktopLyric = view.FindViewById<View>(Resource.Id.btn_desktop_lyric);
        if (btnDesktopLyric != null)
            btnDesktopLyric.Click += (s, e) => nav.PushFragment("DesktopLyric");

        return view;
    }

    /// <summary>
    /// Fragment恢复时重新检查电池优化状态
    /// </summary>
    public override void OnResume()
    {
        base.OnResume();
        CheckBatteryOptimizationStatus();
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
