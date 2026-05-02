using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Net.Wifi;
using Android.OS;
using AndroidX.Core.App;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Services;

/// <summary>
/// Android 前台播放服务。
/// 前台 Service + 通知栏控制 + WiFi锁，解决后台被杀和锁屏断网。
/// </summary>
[Service(
    Name = "com.catclaw.music.ForegroundPlayerService",
    Exported = true,
    ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeMediaPlayback)]
public class ForegroundPlayerService : Service
{
    private const string ChannelId = "catclaw_playback";
    private const int NotificationId = 1001;

    private IAudioPlayerService? _audioPlayer;
    private NowPlayingViewModel? _nowPlayingVm;
    private WifiManager.WifiLock? _wifiLock;
    private bool _started;

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnCreate()
    {
        base.OnCreate();

        _audioPlayer = MainApplication.Services.GetRequiredService<IAudioPlayerService>();
        _nowPlayingVm = MainApplication.Services.GetRequiredService<NowPlayingViewModel>();

        CreateNotificationChannel();
        AcquireWifiLock();

        if (_audioPlayer is not null)
        {
            _audioPlayer.StateChanged += OnPlaybackStateChanged;
        }
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var action = intent?.Action;
        if (!string.IsNullOrEmpty(action) && action != Intent.ActionMain)
        {
            HandleAction(action);
            return StartCommandResult.Sticky;
        }

        if (!_started)
        {
            var notification = BuildNotification();
            StartForeground(NotificationId, notification);
            _started = true;
        }

        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        if (_audioPlayer is not null)
            _audioPlayer.StateChanged -= OnPlaybackStateChanged;

        _started = false;
        StopForeground(StopForegroundFlags.Remove);
        ReleaseWifiLock();

        base.OnDestroy();
    }

    public static void Start(Context context)
    {
        var intent = new Intent(context, typeof(ForegroundPlayerService));
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            context.StartForegroundService(intent);
        else
            context.StartService(intent);
    }

    public static void Stop(Context context)
    {
        var intent = new Intent(context, typeof(ForegroundPlayerService));
        context.StopService(intent);
    }

    #region Notification

    private void CreateNotificationChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O) return;

        var channel = new NotificationChannel(ChannelId, "播放控制", NotificationImportance.Default)
        {
            Description = "猫爪音乐播放控制",
            LockscreenVisibility = NotificationVisibility.Public
        };
        channel.SetSound(null, null);
        channel.EnableVibration(false);
        channel.SetShowBadge(false);

        var manager = (NotificationManager)GetSystemService(NotificationService)!;
        manager.CreateNotificationChannel(channel);
    }

    private Notification BuildNotification()
    {
        var song = _nowPlayingVm?.CurrentSong;
        var title = song?.Title ?? "猫爪音乐";
        var artist = song?.Artist ?? "未在播放";
        var isPlaying = _audioPlayer?.IsPlaying ?? false;

        var playPauseIcon = isPlaying ? Resource.Drawable.ic_pause : Resource.Drawable.ic_play;
        var playPauseLabel = isPlaying ? "暂停" : "播放";

        var mediaStyle = new AndroidX.Media.App.NotificationCompat.MediaStyle();
        mediaStyle.SetShowActionsInCompactView(0, 1, 2);
        mediaStyle.SetMediaSession(null);

        var builder = new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle(title)
            .SetContentText(artist)
            .SetSmallIcon(Resource.Drawable.ic_play)
            .SetLargeIcon(LoadCoverBitmap())
            .SetVisibility(NotificationCompat.VisibilityPublic)
            .SetOngoing(true)
            .SetShowWhen(false)
            .SetPriority(NotificationCompat.PriorityDefault)
            .SetContentIntent(BuildContentPendingIntent())
            .SetColor(unchecked((int)0xFF9B7ED8))
            .SetStyle(mediaStyle);

        // 添加三个控制按钮
        builder.AddAction(
            Resource.Drawable.ic_skip_previous, "上一首",
            BuildActionIntent("previous"));

        builder.AddAction(
            playPauseIcon, playPauseLabel,
            BuildActionIntent(isPlaying ? "pause" : "play"));

        builder.AddAction(
            Resource.Drawable.ic_skip_next, "下一首",
            BuildActionIntent("next"));

        return builder.Build();
    }

    private Bitmap? LoadCoverBitmap()
    {
        try
        {
            var path = _nowPlayingVm?.CoverSource;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;
            return BitmapFactory.DecodeFile(path);
        }
        catch { return null; }
    }

    private PendingIntent BuildActionIntent(string action)
    {
        var intent = new Intent(this, typeof(ForegroundPlayerService));
        intent.SetAction(action);
        return PendingIntent.GetService(this, action.GetHashCode(), intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
    }

    private PendingIntent BuildContentPendingIntent()
    {
        var intent = new Intent(this, typeof(MainActivity));
        intent.SetAction(Intent.ActionMain);
        intent.AddCategory(Intent.CategoryLauncher);
        intent.SetFlags(ActivityFlags.SingleTop);
        return PendingIntent.GetActivity(this, 0, intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
    }

    private void UpdateNotification()
    {
        var notification = BuildNotification();
        var manager = (NotificationManager)GetSystemService(NotificationService)!;
        manager.Notify(NotificationId, notification);
    }

    private void HandleAction(string action)
    {
        switch (action)
        {
            case "play":
                _audioPlayer?.ResumeAsync();
                break;
            case "pause":
                _audioPlayer?.PauseAsync();
                break;
            case "next":
                _nowPlayingVm?.NextCommand.Execute(null);
                break;
            case "previous":
                _nowPlayingVm?.PreviousCommand.Execute(null);
                break;
        }
        // 通知栏更新由 StateChanged 事件触发，避免异步竞态导致双击
    }

    #endregion

    #region WiFi Lock

    private void AcquireWifiLock()
    {
        try
        {
            var wifiManager = (WifiManager)GetSystemService(WifiService)!;
            _wifiLock = wifiManager.CreateWifiLock("CatClawMusic:WiFiLock");
            _wifiLock.SetReferenceCounted(false);
            _wifiLock.Acquire();
        }
        catch { /* 无 WiFi 模块或权限不足 */ }
    }

    private void ReleaseWifiLock()
    {
        try
        {
            if (_wifiLock is not null && _wifiLock.IsHeld)
                _wifiLock.Release();
        }
        catch { }
    }

    #endregion

    #region Events

    private void OnPlaybackStateChanged(object? sender, PlaybackStateChangedEventArgs e)
    {
        UpdateNotification();
    }

    #endregion
}
