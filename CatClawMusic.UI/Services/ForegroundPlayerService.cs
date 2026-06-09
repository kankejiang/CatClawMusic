using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Media;
using Android.Media.Session;
using Android.Net.Wifi;
using Android.OS;
using Android.Views;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using ALog = Android.Util.Log;

namespace CatClawMusic.UI.Services;

/// <summary>前台播放服务，提供通知栏播放控制、MediaSession 集成和桌面歌词快捷操作</summary>
[Service(
    Name = "com.catclaw.music.ForegroundPlayerService",
    Exported = true,
    ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeMediaPlayback)]
public class ForegroundPlayerService : Service
{
    private const string ChannelId = "catclaw_playback_v6";
    private const string NotifGroup = "catclaw_playback_group";
    private const int NotifIdMain = 1001;
    private const string PrefKeyDesktopLyric = "desktop_lyric";
    private const string PrefKeyDesktopLyricEnabled = "desktop_lyric_enabled";

    private IAudioPlayerService? _audioPlayer;
    private NowPlayingViewModel? _nowPlayingVm;
    private WifiManager.WifiLock? _wifiLock;
    private MediaSession? _mediaSession;
    private Handler? _progressHandler;
    private bool _started;
    private ScreenOnReceiver? _screenOnReceiver;
    

    /// <summary>返回 null，此服务不绑定</summary>
    public override IBinder? OnBind(Intent? intent) => null;

    /// <summary>服务创建时初始化播放器、通知频道、WiFi 锁和 MediaSession，并订阅事件</summary>
    public override void OnCreate()
    {
        base.OnCreate();

        _audioPlayer = MainApplication.Services.GetRequiredService<IAudioPlayerService>();
        _nowPlayingVm = MainApplication.Services.GetRequiredService<NowPlayingViewModel>();

        CreateNotificationChannels();
        AcquireWifiLock();
        InitMediaSession();

        if (_audioPlayer is not null)
            _audioPlayer.StateChanged += OnPlaybackStateChanged;

        if (_nowPlayingVm is not null)
            _nowPlayingVm.PropertyChanged += OnViewModelPropertyChanged;

        _screenOnReceiver = new ScreenOnReceiver(this);
        var filter = new IntentFilter();
        filter.AddAction(Intent.ActionScreenOn);
        filter.AddAction(Intent.ActionUserPresent);
        RegisterReceiver(_screenOnReceiver, filter);
    }

    /// <summary>处理启动命令：首次启动时进入前台模式并显示通知，或根据 Intent Action 执行播放控制</summary>
    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var action = intent?.Action;
        if (!string.IsNullOrEmpty(action) && action != Intent.ActionMain)
        {
            if (action.StartsWith("catclaw_"))
                HandleAction(action.Substring("catclaw_".Length));
            else
                HandleAction(action);
            return StartCommandResult.Sticky;
        }

        if (!_started)
        {
            StartForeground(NotifIdMain, BuildMainNotification());
            _started = true;
            StartProgressUpdates();
        }

        return StartCommandResult.Sticky;
    }

    /// <summary>服务销毁时取消事件订阅、停止前台通知、释放 WiFi 锁和 MediaSession</summary>
    public override void OnDestroy()
    {
        if (_screenOnReceiver != null)
        {
            try { UnregisterReceiver(_screenOnReceiver); } catch { }
            _screenOnReceiver = null;
        }

        if (_audioPlayer is not null)
            _audioPlayer.StateChanged -= OnPlaybackStateChanged;

        if (_nowPlayingVm is not null)
            _nowPlayingVm.PropertyChanged -= OnViewModelPropertyChanged;

        _started = false;
        StopForeground(StopForegroundFlags.Remove);
        ReleaseWifiLock();
        StopProgressUpdates();
        ReleaseMediaSession();

        base.OnDestroy();
    }

    /// <summary>启动前台播放服务，Android O+ 使用 StartForegroundService</summary>
    public static void Start(Context context)
    {
        try
        {
            var intent = new Intent(context, typeof(ForegroundPlayerService));
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                context.StartForegroundService(intent);
            else
                context.StartService(intent);
        }
        catch (Java.Lang.IllegalStateException ex)
        {
            ALog.Warn("CatClaw", $"ForegroundPlayerService start rejected: {ex.Message}");
        }
        catch (System.Exception ex)
        {
            ALog.Warn("CatClaw", $"ForegroundPlayerService start failed: {ex.Message}");
        }
    }

    /// <summary>停止前台播放服务</summary>
    public static void Stop(Context context)
    {
        var intent = new Intent(context, typeof(ForegroundPlayerService));
        context.StopService(intent);
    }

    #region Progress Timer

    /// <summary>启动进度更新定时器，每秒更新 MediaSession 播放状态</summary>
    private void StartProgressUpdates()
    {
        if (_progressHandler != null) return;
        _progressHandler = new Handler(Looper.MainLooper!);
        _progressHandler.Post(ProgressTick);
    }

    /// <summary>停止进度更新定时器</summary>
    private void StopProgressUpdates()
    {
        if (_progressHandler != null)
        {
            _progressHandler.RemoveCallbacksAndMessages(null);
            _progressHandler = null;
        }
    }

    /// <summary>进度定时器回调，更新 MediaSession 播放状态（系统自动同步通知进度条）</summary>
    private void ProgressTick()
    {
        if (_progressHandler == null) return;
        UpdateMediaSessionPlaybackState();
        _progressHandler.PostDelayed(ProgressTick, 1000);
    }

    #endregion

    #region MediaSession

    /// <summary>初始化 MediaSession，支持系统播控、锁屏信息和蓝牙/耳机线控</summary>
    private void InitMediaSession()
    {
        try
        {
            _mediaSession = new MediaSession(this, "CatClawMusic");
            _mediaSession.SetFlags(
                MediaSession.FlagHandlesTransportControls |
                MediaSession.FlagHandlesMediaButtons);
            _mediaSession.SetCallback(new MediaSessionCallback(this));
            _mediaSession.Active = true;
            UpdateMediaSessionPlaybackState();
            UpdateMediaSessionMetadata();
        }
        catch (System.Exception ex)
        {
            ALog.Warn("CatClaw", $"MediaSession init failed: {ex.Message}");
        }
    }

    /// <summary>MediaSession 回调，处理系统播控和耳机/蓝牙按钮事件</summary>
    private class MediaSessionCallback : MediaSession.Callback
    {
        private readonly ForegroundPlayerService _service;
        public MediaSessionCallback(ForegroundPlayerService service) => _service = service;

        public override void OnPlay() => _service._audioPlayer?.ResumeAsync();
        public override void OnPause() => _service._audioPlayer?.PauseAsync();
        public override void OnSkipToNext() => _service._nowPlayingVm?.NextCommand.Execute(null);
        public override void OnSkipToPrevious() => _service._nowPlayingVm?.PreviousCommand.Execute(null);
        public override void OnSeekTo(long pos) => _service._audioPlayer?.SeekAsync(TimeSpan.FromMilliseconds(pos));

        /// <summary>处理自定义按钮点击（收藏、歌词等），HyperOS 通过 PlaybackState.CustomAction 触发</summary>
        public override void OnCustomAction(string action, Bundle? extras)
        {
            _service.HandleAction(action);
        }

        /// <summary>处理媒体按钮事件，支持耳机线控（播放/暂停/上一曲/下一曲/快进/快退）</summary>
        public override bool OnMediaButtonEvent(Intent? mediaButtonIntent)
        {
            if (mediaButtonIntent?.Action != Intent.ActionMediaButton)
                return base.OnMediaButtonEvent(mediaButtonIntent);

            var keyEvent = mediaButtonIntent.GetParcelableExtra(Intent.ExtraKeyEvent) as KeyEvent;
            if (keyEvent?.Action != KeyEventActions.Down)
                return true;

            var handled = true;
            switch (keyEvent.KeyCode)
            {
                case Keycode.Headsethook:
                case Keycode.MediaPlayPause:
                    if (_service._audioPlayer?.IsPlaying ?? false)
                        _service._audioPlayer?.PauseAsync();
                    else
                        _service._audioPlayer?.ResumeAsync();
                    break;
                case Keycode.MediaPlay:
                    _service._audioPlayer?.ResumeAsync();
                    break;
                case Keycode.MediaPause:
                    _service._audioPlayer?.PauseAsync();
                    break;
                case Keycode.MediaNext:
                    _service._nowPlayingVm?.NextCommand.Execute(null);
                    break;
                case Keycode.MediaPrevious:
                    _service._nowPlayingVm?.PreviousCommand.Execute(null);
                    break;
                case Keycode.MediaFastForward:
                    if (_service._audioPlayer != null)
                    {
                        var fwdMs = _service._audioPlayer.CurrentPosition.TotalMilliseconds + 5000;
                        _ = _service._audioPlayer.SeekAsync(TimeSpan.FromMilliseconds(fwdMs));
                    }
                    break;
                case Keycode.MediaRewind:
                    if (_service._audioPlayer != null)
                    {
                        var rwdMs = Math.Max(0, _service._audioPlayer.CurrentPosition.TotalMilliseconds - 5000);
                        _ = _service._audioPlayer.SeekAsync(TimeSpan.FromMilliseconds(rwdMs));
                    }
                    break;
                case Keycode.MediaStop:
                    _service._audioPlayer?.PauseAsync();
                    break;
                default:
                    handled = base.OnMediaButtonEvent(mediaButtonIntent);
                    break;
            }

            return handled;
        }
    }

    /// <summary>释放 MediaSession 资源</summary>
    private void ReleaseMediaSession()
    {
        try
        {
            if (_mediaSession != null)
            {
                _mediaSession.Active = false;
                _mediaSession.Release();
                _mediaSession = null;
            }
        }
        catch { }
    }

    /// <summary>更新 MediaSession 播放状态（播放/暂停/进度位置），使用实时位置确保锁屏/通知栏进度同步</summary>
    /// <remarks>HyperOS 从 PlaybackState 的 Actions + CustomActions 读取通知按钮，而非从 Notification.Builder.AddAction()</remarks>
    private void UpdateMediaSessionPlaybackState()
    {
        if (_mediaSession == null) return;
        try
        {
            var isPlaying = _audioPlayer?.IsPlaying ?? false;
            var state = isPlaying ? PlaybackStateCode.Playing : PlaybackStateCode.Paused;
            var positionMs = _audioPlayer?.RealtimePositionMs ?? 0;

            var isLiked = _nowPlayingVm?.IsLiked ?? false;
            var isLyricOn = IsDesktopLyricEnabled();

            var builder = new Android.Media.Session.PlaybackState.Builder()
                .SetState(state, positionMs, 1.0f)
                .SetActions(
                    Android.Media.Session.PlaybackState.ActionPlay |
                    Android.Media.Session.PlaybackState.ActionPause |
                    Android.Media.Session.PlaybackState.ActionSkipToNext |
                    Android.Media.Session.PlaybackState.ActionSkipToPrevious |
                    Android.Media.Session.PlaybackState.ActionSeekTo);

            // HyperOS 从 PlaybackState 读取 CustomAction 来显示自定义按钮
            var favIcon = isLiked ? Resource.Drawable.ic_notif_favorite : Resource.Drawable.ic_notif_favorite_border;
            builder.AddCustomAction(new Android.Media.Session.PlaybackState.CustomAction.Builder(
                "favorite", isLiked ? "取消收藏" : "收藏", favIcon).Build());

            var lyricIcon = isLyricOn ? Resource.Drawable.ic_notif_lyric_on : Resource.Drawable.ic_notif_lyric_off;
            builder.AddCustomAction(new Android.Media.Session.PlaybackState.CustomAction.Builder(
                "lyric_toggle", isLyricOn ? "关闭歌词" : "桌面歌词", lyricIcon).Build());

            _mediaSession.SetPlaybackState(builder.Build());
        }
        catch (System.Exception ex)
        {
            ALog.Warn("CatClaw", $"UpdateMediaSessionPlaybackState failed: {ex.Message}");
        }
    }

    /// <summary>更新 MediaSession 元数据（歌名、艺术家、专辑、封面）</summary>
    private void UpdateMediaSessionMetadata()
    {
        if (_mediaSession == null) return;
        try
        {
            var song = _nowPlayingVm?.CurrentSong;
            var title = song?.Title ?? "猫爪音乐";
            var artist = song?.Artist ?? "未在播放";
            var durationMs = (long)(_audioPlayer?.Duration.TotalMilliseconds ?? 0);
            if (durationMs <= 0 && song?.Duration > 0)
                durationMs = song.Duration;

            var builder = new MediaMetadata.Builder()
                .PutString(MediaMetadata.MetadataKeyTitle, title)
                .PutString(MediaMetadata.MetadataKeyArtist, artist)
                .PutString(MediaMetadata.MetadataKeyAlbum, song?.Album ?? "")
                .PutLong(MediaMetadata.MetadataKeyDuration, durationMs);

            var cover = LoadCoverBitmap();
            if (cover != null)
                builder.PutBitmap(MediaMetadata.MetadataKeyAlbumArt, cover);

            _mediaSession.SetMetadata(builder.Build());
        }
        catch (System.Exception ex)
        {
            ALog.Warn("CatClaw", $"UpdateMediaSessionMetadata failed: {ex.Message}");
        }
    }

    #endregion

    #region Notification Channels

    /// <summary>创建通知频道（Android O+）</summary>
    private void CreateNotificationChannels()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O) return;
        var manager = (NotificationManager)GetSystemService(NotificationService)!;
        // 先删除旧频道确保干净状态
        manager.DeleteNotificationChannel(ChannelId);
        // 删除旧版本频道
        manager.DeleteNotificationChannel("catclaw_playback_v5");
        manager.DeleteNotificationChannel("catclaw_playback_v4");

        var channel = new NotificationChannel(ChannelId, "猫爪音乐", NotificationImportance.Default)
        {
            Description = "播放控制",
            LockscreenVisibility = NotificationVisibility.Public
        };
        manager.CreateNotificationChannel(channel);
    }

    #endregion

    #region Notifications

    /// <summary>构建主通知（媒体风格，包含上一曲/播放暂停/下一曲按钮）</summary>
    private Notification BuildMainNotification()
    {
        var song = _nowPlayingVm?.CurrentSong;
        var title = song?.Title ?? "猫爪音乐";
        var artist = string.IsNullOrEmpty(song?.Artist) ? "未在播放" : song!.Artist;
        var isPlaying = _audioPlayer?.IsPlaying ?? false;

        var playIcon = isPlaying ? Resource.Drawable.ic_notif_pause : Resource.Drawable.ic_notif_play;
        var playLabel = isPlaying ? "暂停" : "播放";

        var mediaStyle = new Notification.MediaStyle();
        if (_mediaSession != null)
            mediaStyle.SetMediaSession(_mediaSession.SessionToken);
        mediaStyle.SetShowActionsInCompactView(1, 2, 3);

        var isLiked = _nowPlayingVm?.IsLiked ?? false;
        var isLyricOn = IsDesktopLyricEnabled();
        var favIcon = isLiked ? Resource.Drawable.ic_notif_favorite : Resource.Drawable.ic_notif_favorite_border;

        var builder = new Notification.Builder(this, ChannelId)
            .SetContentTitle(title)
            .SetContentText(artist)
            .SetSmallIcon(Resource.Drawable.ic_play)
            .SetLargeIcon(LoadCoverBitmap())
            .SetOngoing(true)
            .SetContentIntent(BuildContentPendingIntent())
            .SetStyle(mediaStyle);

        // 5 个按钮：收藏 | 上一曲 | 播放/暂停 | 下一曲 | 桌面歌词
        builder.AddAction(BuildAction(favIcon, isLiked ? "取消收藏" : "收藏", "favorite"));
        builder.AddAction(BuildAction(Resource.Drawable.ic_notif_previous, "上一曲", "previous"));
        builder.AddAction(BuildAction(playIcon, playLabel, isPlaying ? "pause" : "play"));
        builder.AddAction(BuildAction(Resource.Drawable.ic_notif_next, "下一曲", "next"));
        builder.AddAction(BuildAction(isLyricOn ? Resource.Drawable.ic_notif_lyric_on : Resource.Drawable.ic_notif_lyric_off,
            isLyricOn ? "关闭歌词" : "桌面歌词", "lyric_toggle"));

        return builder.Build();
    }

    #endregion

    #region Helpers

    /// <summary>构建通知 Action（图标+标题+PendingIntent）</summary>
    private Notification.Action BuildAction(int iconRes, string title, string action)
    {
        var icon = Icon.CreateWithResource(this, iconRes);
        var pendingIntent = BuildActionIntent(action);
        return new Notification.Action.Builder(icon, title, pendingIntent).Build();
    }

    /// <summary>从当前歌曲封面路径加载 Bitmap</summary>
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

    private bool IsDesktopLyricEnabled()
    {
        var prefs = GetSharedPreferences(PrefKeyDesktopLyric, FileCreationMode.Private);
        return prefs?.GetBoolean(PrefKeyDesktopLyricEnabled, false) ?? false;
    }

    /// <summary>切换桌面歌词开关状态并相应显示/隐藏桌面歌词</summary>
    private void ToggleDesktopLyric()
    {
        var currentEnabled = IsDesktopLyricEnabled();
        var newEnabled = !currentEnabled;
        var prefs = GetSharedPreferences(PrefKeyDesktopLyric, FileCreationMode.Private);
        prefs?.Edit()?.PutBoolean(PrefKeyDesktopLyricEnabled, newEnabled)?.Apply();

        var service = DesktopLyricService.Instance;
        if (newEnabled)
        {
            var ctx = global::Android.App.Application.Context;
            new Handler(Looper.MainLooper!).Post(() => { if (ctx != null) service.Show(ctx); });
        }
        else
        {
            new Handler(Looper.MainLooper!).Post(() => service.Hide());
        }
    }

    /// <summary>构建指向本服务的 PendingIntent（通过 Action 区分操作）</summary>
    private PendingIntent BuildActionIntent(string action)
    {
        var intent = new Intent(this, typeof(ForegroundPlayerService));
        intent.SetAction("catclaw_" + action);
        return PendingIntent.GetService(this, action.GetHashCode() & 0xFFFF, intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
    }

    /// <summary>构建点击通知打开 MainActivity 的 PendingIntent</summary>
    private PendingIntent BuildContentPendingIntent()
    {
        var intent = new Intent(this, typeof(MainActivity));
        intent.SetAction(Intent.ActionMain);
        intent.AddCategory(Intent.CategoryLauncher);
        intent.SetFlags(ActivityFlags.SingleTop);
        return PendingIntent.GetActivity(this, 0, intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
    }

    #endregion

    #region Actions + Update

    /// <summary>刷新主通知</summary>
    private void UpdateNotification()
    {
        try
        {
            var manager = (NotificationManager)GetSystemService(NotificationService)!;
            manager.Notify(NotifIdMain, BuildMainNotification());
        }
        catch (System.Exception ex)
        {
            ALog.Warn("CatClaw", $"UpdateNotification failed: {ex.Message}");
        }
    }

    /// <summary>根据 Action 字符串执行对应的播放控制或快捷操作</summary>
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
            case "favorite":
                _nowPlayingVm?.ToggleLikeCommand.Execute(null);
                new Handler(Looper.MainLooper!).PostDelayed(() =>
                {
                    UpdateMediaSessionPlaybackState();
                    UpdateMediaSessionMetadata();
                    UpdateNotification();
                }, 200);
                return;
            case "lyric_toggle":
                ToggleDesktopLyric();
                new Handler(Looper.MainLooper!).PostDelayed(() =>
                {
                    UpdateMediaSessionPlaybackState();
                    UpdateNotification();
                }, 200);
                return;
            case "lyric_lock":
                DesktopLyricService.Instance.ToggleLock();
                new Handler(Looper.MainLooper!).PostDelayed(() => UpdateNotification(), 200);
                return;
            case "lyric_single":
                DesktopLyricService.Instance.SetDisplayMode(0);
                new Handler(Looper.MainLooper!).PostDelayed(() => UpdateNotification(), 200);
                return;
            case "lyric_dual":
                DesktopLyricService.Instance.SetDisplayMode(1);
                new Handler(Looper.MainLooper!).PostDelayed(() => UpdateNotification(), 200);
                return;
        }
    }

    #endregion

    #region WiFi Lock

    /// <summary>获取 WiFi 锁，防止网络播放时 WiFi 休眠</summary>
    private void AcquireWifiLock()
    {
        try
        {
            var wifiManager = (WifiManager)GetSystemService(WifiService)!;
            _wifiLock = wifiManager.CreateWifiLock("CatClawMusic:WiFiLock");
            _wifiLock.SetReferenceCounted(false);
            _wifiLock.Acquire();
        }
        catch { }
    }

    /// <summary>释放 WiFi 锁</summary>
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

    /// <summary>播放状态变化时更新 MediaSession 和通知</summary>
    private void OnPlaybackStateChanged(object? sender, PlaybackStateChangedEventArgs e)
    {
        UpdateMediaSessionPlaybackState();
        // 播放状态变化时同步更新元数据（修复切歌时通知栏时长显示 00:00）
        UpdateMediaSessionMetadata();
        // 延迟更新通知确保播放状态已同步到系统
        new Handler(Looper.MainLooper!).PostDelayed(() => UpdateNotification(), 150);
    }

    /// <summary>ViewModel 属性变化时按需更新 MediaSession 元数据和通知</summary>
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NowPlayingViewModel.IsLiked))
        {
            UpdateMediaSessionPlaybackState();
            UpdateNotification();
        }
        else if (e.PropertyName == nameof(NowPlayingViewModel.CurrentSong) ||
                 e.PropertyName == nameof(NowPlayingViewModel.CoverSource))
        {
            UpdateMediaSessionMetadata();
            UpdateNotification();
        }
    }

    #endregion

    /// <summary>屏幕亮起/用户解锁时立即同步 MediaSession 进度，修复息屏后通知栏/锁屏进度条不同步</summary>
    private class ScreenOnReceiver : BroadcastReceiver
    {
        private readonly WeakReference<ForegroundPlayerService> _serviceRef;
        public ScreenOnReceiver(ForegroundPlayerService service) => _serviceRef = new(service);
        public override void OnReceive(global::Android.Content.Context? context, Intent? intent)
        {
            if (_serviceRef.TryGetTarget(out var service))
            {
                service.UpdateMediaSessionPlaybackState();
                service.UpdateNotification();
            }
        }
    }
}
