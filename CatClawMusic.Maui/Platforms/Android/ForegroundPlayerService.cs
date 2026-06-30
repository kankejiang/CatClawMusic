using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Media;
using Android.OS;
using AndroidX.Core.App;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Maui.Platforms.Android;

/// <summary>
/// 前台播放服务 — 在通知栏显示播放控制，防止后台被杀
/// </summary>
[Service(ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeMediaPlayback)]
public class ForegroundPlayerService : Service
{
    private const int NotificationId = 1001;
    private const string ChannelId = "catclaw_playback";
    private const string ChannelName = "播放控制";

    public const string ActionPlayPause = "com.catclaw.action.PLAY_PAUSE";
    public const string ActionNext = "com.catclaw.action.NEXT";
    public const string ActionPrevious = "com.catclaw.action.PREVIOUS";
    public const string ActionStop = "com.catclaw.action.STOP";

    private static ForegroundPlayerService? _instance;
    public static ForegroundPlayerService? Instance => _instance;

    private string _title = "未在播放";
    private string _artist = "";
    private bool _isPlaying = false;

    // 用于从外部（AudioPlayerService）触发 UI 更新
    public static event Action<string, string, bool>? OnUpdateRequested;

    public override void OnCreate()
    {
        base.OnCreate();
        _instance = this;
        CreateNotificationChannel();
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action != null)
        {
            switch (intent.Action)
            {
                case ActionPlayPause:
                    TogglePlayPause();
                    break;
                case ActionNext:
                    NextTrack();
                    break;
                case ActionPrevious:
                    PreviousTrack();
                    break;
                case ActionStop:
                    StopPlayback();
                    break;
            }
        }

        return StartCommandResult.Sticky;
    }

    public override IBinder? OnBind(Intent? intent) => null;

    /// <summary>更新通知信息（由 AudioPlayerService 调用）</summary>
    public void UpdateNotification(string title, string artist, bool isPlaying)
    {
        _title = title;
        _artist = artist;
        _isPlaying = isPlaying;
        var notification = BuildNotification();
        StartForeground(NotificationId, notification);
    }

    /// <summary>显示通知并启动前台服务</summary>
    public static void Start(global::Android.Content.Context context, string title, string artist)
    {
        var intent = new Intent(context, typeof(ForegroundPlayerService));
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            context.StartForegroundService(intent);
        }
        else
        {
            context.StartService(intent);
        }

        // 延迟更新通知（等 Service 创建完成）
        _ = Task.Delay(200).ContinueWith(_ =>
        {
            Instance?.UpdateNotification(title, artist, false);
        }, TaskScheduler.Default);
    }

    /// <summary>停止前台服务</summary>
    public static void Stop(global::Android.Content.Context context)
    {
        var intent = new Intent(context, typeof(ForegroundPlayerService));
        context.StopService(intent);
        _instance = null;
    }

    /// <summary>更新播放状态（由 AudioPlayerService 调用）</summary>
    public static void UpdatePlayState(string title, string artist, bool isPlaying)
    {
        Instance?.UpdateNotification(title, artist, isPlaying);
    }

    private void TogglePlayPause()
    {
        OnUpdateRequested?.Invoke(_title, _artist, !_isPlaying);
    }

    private void NextTrack()
    {
        OnUpdateRequested?.Invoke(_title, _artist, _isPlaying); // Next action
    }

    private void PreviousTrack()
    {
        OnUpdateRequested?.Invoke(_title, _artist, _isPlaying); // Previous action
    }

    private void StopPlayback()
    {
        StopForeground(StopForegroundFlags.Remove);
        StopSelf();
        _instance = null;
    }

    private Notification BuildNotification()
    {
        var playPauseIntent = new Intent(this, typeof(ForegroundPlayerService));
        playPauseIntent.SetAction(ActionPlayPause);
        var playPausePending = PendingIntent.GetService(this, 0, playPauseIntent,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        var nextIntent = new Intent(this, typeof(ForegroundPlayerService));
        nextIntent.SetAction(ActionNext);
        var nextPending = PendingIntent.GetService(this, 1, nextIntent,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        var stopIntent = new Intent(this, typeof(ForegroundPlayerService));
        stopIntent.SetAction(ActionStop);
        var stopPending = PendingIntent.GetService(this, 2, stopIntent,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        // 点击通知回到 App
        var packageManager = PackageManager!;
        var launchIntent = packageManager.GetLaunchIntentForPackage(PackageName!);
        var contentPending = PendingIntent.GetActivity(this, 3, launchIntent,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        var playIcon = _isPlaying
            ? global::Android.Resource.Drawable.IcMediaPause
            : global::Android.Resource.Drawable.IcMediaPlay;

        var notification = new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle(_title)
            .SetContentText(_artist)
            .SetSmallIcon(global::Android.Resource.Drawable.IcMediaPlay)
            .SetContentIntent(contentPending)
            .SetOngoing(true)
            .SetPriority(NotificationCompat.PriorityHigh)
            .AddAction(global::Android.Resource.Drawable.IcMediaPrevious, "上一首", null!) // placeholder
            .AddAction(playIcon, "播放/暂停", playPausePending)
            .AddAction(global::Android.Resource.Drawable.IcMediaNext, "下一首", nextPending)
            .AddAction(new NotificationCompat.Action.Builder(
                global::Android.Resource.Drawable.IcDelete, "停止", stopPending).Build())
            .SetVisibility(NotificationCompat.VisibilityPublic)
            .Build();

        return notification;
    }

    private void CreateNotificationChannel()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            var channel = new NotificationChannel(ChannelId, ChannelName, NotificationImportance.Low)
            {
                Description = "猫爪音乐播放控制",
                LockscreenVisibility = NotificationVisibility.Public
            };
            var manager = GetSystemService(NotificationService) as NotificationManager;
            manager?.CreateNotificationChannel(channel);
        }
    }

    public override void OnDestroy()
    {
        _instance = null;
        base.OnDestroy();
    }
}
