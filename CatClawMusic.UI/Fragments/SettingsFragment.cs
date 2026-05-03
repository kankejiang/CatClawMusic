using Android.Content;
using Android.OS;
using Android.Provider;
using Android.Views;
using Android.Widget;
using AndroidX.Core.App;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Data;
using CatClawMusic.UI.Platforms.Android;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

public class SettingsFragment : Fragment
{
    private TextView? _tvLocalStatus;
    private TextView? _tvRemoteStatus;
    private TextView? _tvBatteryStatus;
    private TextView? _tvNotificationStatus;

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
    {
        var view = inflater.Inflate(Resource.Layout.fragment_settings, container, false)!;
        var nav = MainApplication.Services.GetRequiredService<INavigationService>();

        // 📁 本地音乐
        var btnFolders = view.FindViewById<View>(Resource.Id.btn_music_folders);
        _tvLocalStatus = view.FindViewById<TextView>(Resource.Id.tv_local_music_status);
        if (btnFolders != null)
            btnFolders.SetOnClickListener(new ClickListener(() => nav.PushFragment("MusicFolderSettings")));

        // ☁️ 远程音乐服务
        var btnRemoteMusic = view.FindViewById<View>(Resource.Id.btn_remote_music);
        _tvRemoteStatus = view.FindViewById<TextView>(Resource.Id.tv_remote_music_status);
        if (btnRemoteMusic != null)
            btnRemoteMusic.SetOnClickListener(new ClickListener(() => nav.PushFragment("RemoteMusic")));

        // 🔋 省电策略设置
        var btnBattery = view.FindViewById<View>(Resource.Id.btn_battery_optimization);
        _tvBatteryStatus = view.FindViewById<TextView>(Resource.Id.tv_battery_status);
        if (btnBattery != null)
            btnBattery.SetOnClickListener(new ClickListener(() => OpenBatteryOptimizationSettings()));

        // 🔔 通知权限设置
        var btnNotification = view.FindViewById<View>(Resource.Id.btn_notification_permission);
        _tvNotificationStatus = view.FindViewById<TextView>(Resource.Id.tv_notification_status);
        if (btnNotification != null)
            btnNotification.SetOnClickListener(new ClickListener(() => OpenNotificationSettings()));

        return view;
    }

    public override void OnViewCreated(View view, Bundle? state)
    {
        base.OnViewCreated(view, state);
        _ = LoadStatusAsync();
    }

    public override void OnResume()
    {
        base.OnResume();
        // 每次恢复页面时重新检查状态
        CheckBatteryOptimizationStatus();
        CheckNotificationStatus();
    }

    private async Task LoadStatusAsync()
    {
        var db = MainApplication.Services.GetRequiredService<MusicDatabase>();
        await db.EnsureInitializedAsync();

        // 本地音乐状态
        var folderUris = FolderPicker.GetSavedFolderUris();
        var localSongCount = await db.GetLocalSongCountAsync();
        var folderText = folderUris.Count > 0
            ? $"已添加{folderUris.Count}个文件夹 | 共{localSongCount}首歌曲"
            : "尚未添加文件夹";
        _tvLocalStatus?.Post(() => _tvLocalStatus.Text = folderText);

        // 远程音乐服务状态
        var profiles = await db.GetConnectionProfilesAsync();
        var enabled = profiles.Where(p => p.IsEnabled).ToList();
        var navi = enabled.FirstOrDefault(p => p.Protocol == ProtocolType.Navidrome);
        var webdav = enabled.FirstOrDefault(p => p.Protocol == ProtocolType.WebDAV);

        string remoteText;
        if (enabled.Count == 0)
        {
            remoteText = "尚未连接服务";
        }
        else
        {
            var parts = new List<string>();
            if (navi != null) parts.Add("Navidrome在线");
            if (webdav != null) parts.Add("WebDAV在线");
            remoteText = $"已连接{enabled.Count}个服务 | {string.Join("、", parts)}";
        }
        _tvRemoteStatus?.Post(() => _tvRemoteStatus.Text = remoteText);

        // 检查省电策略状态
        CheckBatteryOptimizationStatus();
        // 检查通知权限状态
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

    private class ClickListener : Java.Lang.Object, View.IOnClickListener
    {
        private readonly Action _action;
        public ClickListener(Action action) => _action = action;
        public void OnClick(View? v) => _action();
    }
}
