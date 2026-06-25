using Android.Content;
using Android.OS;
using Android.Provider;
using Android.Views;
using Android.Widget;
using CatClawMusic.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace CatClawMusic.UI.Fragments;

/// <summary>
/// 权限管理页面，统一管理所有应用权限
/// </summary>
public class PermissionManagementFragment : SettingsSubPageFragment
{
    private TextView? _tvSummary;
    private TextView? _tvNotificationStatus;
    private TextView? _tvOverlayStatus;
    private TextView? _tvRecordAudioStatus;
    private TextView? _tvMediaImagesStatus;
    private TextView? _tvMediaAudioStatus;
    private TextView? _tvManageStorageStatus;

    private IPermissionService? _permService;

    protected override string GetTitle() => "权限管理";

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
        => inflater.Inflate(Resource.Layout.fragment_permission_management, container, false)!;

    protected override void OnSubViewCreated(View view, Bundle? state)
    {
        _permService = MainApplication.Services.GetRequiredService<IPermissionService>();

        _tvSummary = view.FindViewById<TextView>(Resource.Id.tv_permission_summary);
        _tvNotificationStatus = view.FindViewById<TextView>(Resource.Id.tv_notification_status);
        _tvOverlayStatus = view.FindViewById<TextView>(Resource.Id.tv_overlay_status);
        _tvRecordAudioStatus = view.FindViewById<TextView>(Resource.Id.tv_record_audio_status);
        _tvMediaImagesStatus = view.FindViewById<TextView>(Resource.Id.tv_media_images_status);
        _tvMediaAudioStatus = view.FindViewById<TextView>(Resource.Id.tv_media_audio_status);
        _tvManageStorageStatus = view.FindViewById<TextView>(Resource.Id.tv_manage_storage_status);

        // 通知权限
        view.FindViewById<View>(Resource.Id.card_notification)?.SetOnClickListener(new ClickListener(() => OpenNotificationSettings()));

        // 悬浮窗权限
        view.FindViewById<View>(Resource.Id.card_overlay)?.SetOnClickListener(new ClickListener(() => _ = _permService?.RequestOverlayPermissionAsync()));

        // 麦克风
        view.FindViewById<View>(Resource.Id.card_record_audio)?.SetOnClickListener(new ClickListener(() => RequestRecordAudioPermission()));

        // 照片和视频
        view.FindViewById<View>(Resource.Id.card_media_images)?.SetOnClickListener(new ClickListener(() => OpenAppDetailSettings()));

        // 音乐和音频
        view.FindViewById<View>(Resource.Id.card_media_audio)?.SetOnClickListener(new ClickListener(() => RequestMediaAudioPermission()));

        // 管理外部存储
        view.FindViewById<View>(Resource.Id.card_manage_storage)?.SetOnClickListener(new ClickListener(() => _ = _permService?.RequestManageStoragePermissionAsync()));
    }

    public override void OnResume()
    {
        base.OnResume();
        _ = RefreshAllStatusAsync();
    }

    /// <summary>
    /// 刷新所有权限状态
    /// </summary>
    private async Task RefreshAllStatusAsync()
    {
        if (Context == null || _permService == null) return;

        var notificationOk = AreNotificationsEnabled();
        var overlayOk = await _permService.CheckOverlayPermissionAsync();
        var recordAudioOk = Context.CheckSelfPermission(Android.Manifest.Permission.RecordAudio)
            == Android.Content.PM.Permission.Granted;
        var mediaImagesOk = CheckMediaImagesPermission();
        var mediaAudioOk = await _permService.CheckStoragePermissionAsync();
        var manageStorageOk = await _permService.CheckManageStoragePermissionAsync();

        SetStatusText(_tvNotificationStatus, notificationOk, "播放控制通知、锁屏控件");
        SetStatusText(_tvOverlayStatus, overlayOk, "桌面歌词悬浮显示");
        SetStatusText(_tvRecordAudioStatus, recordAudioOk, "音频频谱可视化");
        SetStatusText(_tvMediaImagesStatus, mediaImagesOk, "读取专辑封面、艺术家图片");
        SetStatusText(_tvMediaAudioStatus, mediaAudioOk, "扫描和播放本地音乐文件");
        SetStatusText(_tvManageStorageStatus, manageStorageOk, "备份恢复、全盘扫描音乐");

        // 总体状态
        int total = 6;
        int granted = (notificationOk ? 1 : 0) + (overlayOk ? 1 : 0) + (recordAudioOk ? 1 : 0)
            + (mediaImagesOk ? 1 : 0) + (mediaAudioOk ? 1 : 0) + (manageStorageOk ? 1 : 0);

        if (_tvSummary != null)
        {
            if (granted == total)
            {
                _tvSummary.Text = "✅ 所有权限已开启";
                _tvSummary.SetTextColor(Android.Graphics.Color.ParseColor("#4CAF50"));
            }
            else
            {
                _tvSummary.Text = $"⚠️ {granted}/{total} 项权限已开启";
                _tvSummary.SetTextColor(Android.Graphics.Color.ParseColor("#FF9800"));
            }
        }
    }

    /// <summary>
    /// 设置权限状态文本
    /// </summary>
    private void SetStatusText(TextView? tv, bool granted, string description)
    {
        if (tv == null) return;
        tv.Text = granted ? $"✅ 已开启 · {description}" : $"❌ 未开启 · {description}";
        tv.SetTextColor(granted
            ? Android.Graphics.Color.ParseColor("#4CAF50")
            : Android.Graphics.Color.ParseColor("#E04040"));
    }

    /// <summary>
    /// 检查通知权限
    /// </summary>
    private bool AreNotificationsEnabled()
    {
        if (Context == null) return false;
        var nm = (Android.App.NotificationManager)Context.GetSystemService(Context.NotificationService)!;
        if (nm == null) return false;
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
            return nm.AreNotificationsEnabled();
        return true;
    }

    /// <summary>
    /// 检查照片和视频权限（Android 13+ READ_MEDIA_IMAGES，低版本 READ_EXTERNAL_STORAGE）
    /// </summary>
    private bool CheckMediaImagesPermission()
    {
        if (Context == null) return false;
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
            return Context.CheckSelfPermission(Android.Manifest.Permission.ReadMediaImages)
                == Android.Content.PM.Permission.Granted;
        return Context.CheckSelfPermission(Android.Manifest.Permission.ReadExternalStorage)
            == Android.Content.PM.Permission.Granted;
    }

    /// <summary>
    /// 打开通知设置页面
    /// </summary>
    private void OpenNotificationSettings()
    {
        if (Context == null) return;
        try
        {
            var intent = new Intent(Settings.ActionAppNotificationSettings);
            intent.PutExtra(Settings.ExtraAppPackage, Context.PackageName);
            StartActivity(intent);
        }
        catch
        {
            OpenAppDetailSettings();
        }
    }

    /// <summary>
    /// 请求麦克风权限
    /// </summary>
    private void RequestRecordAudioPermission()
    {
        var activity = Activity;
        if (activity == null) return;
        if (activity.CheckSelfPermission(Android.Manifest.Permission.RecordAudio)
            == Android.Content.PM.Permission.Granted)
        {
            OpenAppDetailSettings();
            return;
        }
        activity.RequestPermissions(new[] { Android.Manifest.Permission.RecordAudio }, 1002);
    }

    /// <summary>
    /// 请求音乐和音频权限
    /// </summary>
    private void RequestMediaAudioPermission()
    {
        var activity = Activity;
        if (activity == null) return;

        var permission = Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu
            ? Android.Manifest.Permission.ReadMediaAudio
            : Android.Manifest.Permission.ReadExternalStorage;

        if (activity.CheckSelfPermission(permission) == Android.Content.PM.Permission.Granted)
        {
            OpenAppDetailSettings();
            return;
        }
        activity.RequestPermissions(new[] { permission }, 1003);
    }

    /// <summary>
    /// 打开应用详情设置页
    /// </summary>
    private void OpenAppDetailSettings()
    {
        if (Context == null) return;
        try
        {
            var intent = new Intent(Settings.ActionApplicationDetailsSettings);
            intent.SetData(Android.Net.Uri.Parse($"package:{Context.PackageName}"));
            intent.AddFlags(ActivityFlags.NewTask);
            StartActivity(intent);
        }
        catch
        {
            Toast.MakeText(Context, "请前往系统设置 > 猫爪音乐 > 权限", ToastLength.Long)?.Show();
        }
    }

    private class ClickListener : Java.Lang.Object, View.IOnClickListener
    {
        private readonly Action _action;
        public ClickListener(Action action) => _action = action;
        public void OnClick(View? v) => _action();
    }
}
