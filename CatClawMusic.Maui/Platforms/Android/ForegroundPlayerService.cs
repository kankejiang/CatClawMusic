using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Media;
using Android.Media.Session;
using Android.OS;
using AndroidX.Core.App;

namespace CatClawMusic.Maui.Platforms.Android;

/// <summary>Android 前台播放服务，承载播放通知、MediaSession 及通知按钮事件转发，保证应用在后台时仍可控制播放</summary>
[Service(ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeMediaPlayback)]
public class ForegroundPlayerService : Service
{
    /// <summary>前台通知 ID</summary>
    private const int NotificationId = 1001;
    /// <summary>通知渠道 ID</summary>
    private const string ChannelId = "catclaw_playback";
    /// <summary>通知渠道显示名称</summary>
    private const string ChannelName = "播放控制";

    /// <summary>通知"播放/暂停"按钮 Action</summary>
    public const string ActionPlayPause = "com.catclaw.action.PLAY_PAUSE";
    /// <summary>通知"下一首"按钮 Action</summary>
    public const string ActionNext = "com.catclaw.action.NEXT";
    /// <summary>通知"上一首"按钮 Action</summary>
    public const string ActionPrevious = "com.catclaw.action.PREVIOUS";
    /// <summary>通知"停止"按钮 Action</summary>
    public const string ActionStop = "com.catclaw.action.STOP";
    /// <summary>通知"歌词"按钮 Action</summary>
    public const string ActionLyrics = "com.catclaw.action.LYRICS";
    /// <summary>通知"收藏"按钮 Action</summary>
    public const string ActionFavorite = "com.catclaw.action.FAVORITE";

    /// <summary>当前服务实例（静态），用于外部访问</summary>
    private static ForegroundPlayerService? _instance;
    /// <summary>获取当前服务实例</summary>
    public static ForegroundPlayerService? Instance => _instance;

    /// <summary>当前歌曲标题</summary>
    private string _title = "未在播放";
    /// <summary>当前歌曲艺术家</summary>
    private string _artist = "";
    /// <summary>是否正在播放</summary>
    private bool _isPlaying = false;
    /// <summary>是否已收藏</summary>
    private bool _isFavorite = false;
    /// <summary>专辑封面 Bitmap</summary>
    private Bitmap? _albumArt;

    /// <summary>等待服务创建完成后写入的标题（在 Start 调用但服务尚未创建时使用）</summary>
    private static string? _pendingTitle;
    /// <summary>等待服务创建完成后写入的艺术家（在 Start 调用但服务尚未创建时使用）</summary>
    private static string? _pendingArtist;

    /// <summary>Android MediaSession，用于对外暴露播放状态以适配锁屏/蓝牙/耳机等设备</summary>
    private MediaSession? _mediaSession;

    /// <summary>通知"播放/暂停"按钮请求事件，参数为按钮按下后的目标播放状态</summary>
    public static event Action<bool>? OnPlayPauseRequested;
    /// <summary>通知"下一首"按钮请求事件</summary>
    public static event Action? OnNextRequested;
    /// <summary>通知"上一首"按钮请求事件</summary>
    public static event Action? OnPreviousRequested;
    /// <summary>通知"歌词"按钮请求事件</summary>
    public static event Action? OnLyricsRequested;
    /// <summary>通知"收藏"按钮请求事件，参数为按钮按下后的目标收藏状态</summary>
    public static event Action<bool>? OnFavoriteToggled;

    /// <summary>服务创建时回调：缓存实例、应用 pending 状态、创建通知渠道与 MediaSession</summary>
    public override void OnCreate()
    {
        base.OnCreate();
        _instance = this;
        if (!string.IsNullOrEmpty(_pendingTitle))
        {
            _title = _pendingTitle!;
            _artist = _pendingArtist ?? "";
        }
        CreateNotificationChannel();
        InitMediaSession();
    }

    /// <summary>初始化 MediaSession 并注册回调与标志位</summary>
    private void InitMediaSession()
    {
        _mediaSession = new MediaSession(this, "CatClawMusic");
        _mediaSession.Active = true;
        _mediaSession.SetCallback(new MediaSessionCallback(this));
        _mediaSession.SetFlags(MediaSessionFlags.HandlesMediaButtons | MediaSessionFlags.HandlesTransportControls);
    }

    /// <summary>服务启动命令：进入前台并显示通知，随后根据 Intent Action 处理按钮事件</summary>
    /// <param name="intent">启动服务的 Intent</param>
    /// <param name="flags">启动标志</param>
    /// <param name="startId">启动 ID</param>
    /// <returns>返回 Sticky 表示服务被杀后系统会尝试重启</returns>
    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        try
        {
            StartForeground(NotificationId, BuildNotification());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FGService] StartForeground failed: {ex.Message}");
        }

        if (intent?.Action != null)
        {
            HandleAction(intent.Action);
        }

        return StartCommandResult.Sticky;
    }

    /// <summary>根据通知按钮 Action 分发事件，并相应更新通知状态</summary>
    /// <param name="action">通知按钮 Action</param>
    private void HandleAction(string action)
    {
        switch (action)
        {
            case ActionPlayPause:
                _isPlaying = !_isPlaying;
                OnPlayPauseRequested?.Invoke(_isPlaying);
                UpdateNotification(_title, _artist, _isPlaying, _isFavorite, _albumArt);
                break;
            case ActionNext:
                OnNextRequested?.Invoke();
                break;
            case ActionPrevious:
                OnPreviousRequested?.Invoke();
                break;
            case ActionStop:
                StopPlayback();
                break;
            case ActionLyrics:
                OnLyricsRequested?.Invoke();
                break;
            case ActionFavorite:
                _isFavorite = !_isFavorite;
                OnFavoriteToggled?.Invoke(_isFavorite);
                UpdateNotification(_title, _artist, _isPlaying, _isFavorite, _albumArt);
                break;
        }
    }

    /// <summary>服务绑定回调，本服务不支持绑定，固定返回 null</summary>
    /// <param name="intent">绑定 Intent</param>
    /// <returns>始终返回 null</returns>
    public override IBinder? OnBind(Intent? intent) => null;

    /// <summary>更新通知显示的歌曲信息、播放状态、收藏状态与专辑封面</summary>
    /// <param name="title">歌曲标题</param>
    /// <param name="artist">歌曲艺术家</param>
    /// <param name="isPlaying">是否正在播放</param>
    /// <param name="isFavorite">是否已收藏</param>
    /// <param name="albumArt">专辑封面，可为 null</param>
    public void UpdateNotification(string title, string artist, bool isPlaying, bool isFavorite = false, Bitmap? albumArt = null)
    {
        _title = title;
        _artist = artist;
        _isPlaying = isPlaying;
        _isFavorite = isFavorite;
        if (albumArt != null)
        {
            _albumArt?.Recycle();
            _albumArt = albumArt;
        }
        
        UpdateMediaSessionPlaybackState();
        var notification = BuildNotification();
        try
        {
            var notifManager = GetSystemService(NotificationService) as NotificationManager;
            notifManager?.Notify(NotificationId, notification);
        }
        catch { }
    }

    /// <summary>更新 MediaSession 的播放状态与元数据，使锁屏/蓝牙等设备同步显示当前歌曲</summary>
    private void UpdateMediaSessionPlaybackState()
    {
        if (_mediaSession == null) return;

        var state = _isPlaying
            ? PlaybackStateCode.Playing
            : PlaybackStateCode.Paused;

        var actions = PlaybackState.ActionPlay
            | PlaybackState.ActionPause
            | PlaybackState.ActionSkipToNext
            | PlaybackState.ActionSkipToPrevious
            | PlaybackState.ActionPlayPause;

        var playbackState = new PlaybackState.Builder()
            .SetActions(actions)
            .SetState(state, PlaybackState.PlaybackPositionUnknown, 1.0f)
            .Build();

        _mediaSession.SetPlaybackState(playbackState);

        var metadataBuilder = new MediaMetadata.Builder()
            .PutString(MediaMetadata.MetadataKeyTitle, _title)
            .PutString(MediaMetadata.MetadataKeyArtist, _artist);
        if (_albumArt != null)
        {
            metadataBuilder.PutBitmap(MediaMetadata.MetadataKeyAlbumArt, _albumArt);
        }
        _mediaSession.SetMetadata(metadataBuilder.Build());
    }

    /// <summary>静态启动入口：缓存待写入的歌曲信息，并根据系统版本启动前台服务</summary>
    /// <param name="context">Android 上下文</param>
    /// <param name="title">歌曲标题</param>
    /// <param name="artist">歌曲艺术家</param>
    public static void Start(global::Android.Content.Context context, string title, string artist)
    {
        _pendingTitle = title;
        _pendingArtist = artist;

        var intent = new Intent(context, typeof(ForegroundPlayerService));
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            context.StartForegroundService(intent);
        }
        else
        {
            context.StartService(intent);
        }
    }

    /// <summary>静态停止入口：停止服务、释放 MediaSession 与封面资源，并清空静态实例引用</summary>
    /// <param name="context">Android 上下文</param>
    public static void Stop(global::Android.Content.Context context)
    {
        try
        {
            var intent = new Intent(context, typeof(ForegroundPlayerService));
            context.StopService(intent);
        }
        catch { }
        if (_instance?._mediaSession != null)
        {
            _instance._mediaSession.Active = false;
            _instance._mediaSession.Release();
            _instance._mediaSession = null;
        }
        if (_instance != null)
        {
            _instance._albumArt?.Recycle();
            _instance._albumArt = null;
        }
        _instance = null;
    }

    /// <summary>静态更新入口：通过当前实例更新通知的播放状态与歌曲信息</summary>
    /// <param name="title">歌曲标题</param>
    /// <param name="artist">歌曲艺术家</param>
    /// <param name="isPlaying">是否正在播放</param>
    /// <param name="isFavorite">是否已收藏</param>
    /// <param name="albumArt">专辑封面，可为 null</param>
    public static void UpdatePlayState(string title, string artist, bool isPlaying, bool isFavorite = false, Bitmap? albumArt = null)
    {
        Instance?.UpdateNotification(title, artist, isPlaying, isFavorite, albumArt);
    }

    /// <summary>停止播放：移除前台状态、停止自身、释放 MediaSession 与封面资源，并清空静态实例引用</summary>
    private void StopPlayback()
    {
        StopForeground(StopForegroundFlags.Remove);
        StopSelf();
        if (_mediaSession != null)
        {
            _mediaSession.Active = false;
            _mediaSession.Release();
            _mediaSession = null;
        }
        _albumArt?.Recycle();
        _albumArt = null;
        _instance = null;
    }

    /// <summary>构造通知按钮对应的 PendingIntent，用于在用户点击通知按钮时回传 Action</summary>
    /// <param name="action">按钮对应的 Action 字符串</param>
    /// <param name="requestCode">请求码，用于区分不同按钮</param>
    /// <returns>构造完成的 PendingIntent</returns>
    private PendingIntent CreateActionIntent(string action, int requestCode)
    {
        var intent = new Intent(this, typeof(ForegroundPlayerService));
        intent.SetAction(action);
        return PendingIntent.GetService(this, requestCode, intent,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
    }

    /// <summary>构建播放控制通知，包含歌曲信息、专辑封面、点击跳转以及"播放/暂停/上一首/下一首/歌词/收藏"按钮</summary>
    /// <returns>构建完成的 Notification</returns>
    private Notification BuildNotification()
    {
        var playPauseIntent = CreateActionIntent(ActionPlayPause, 0);
        var nextIntent = CreateActionIntent(ActionNext, 1);
        var prevIntent = CreateActionIntent(ActionPrevious, 2);
        var lyricsIntent = CreateActionIntent(ActionLyrics, 3);
        var favoriteIntent = CreateActionIntent(ActionFavorite, 4);

        var packageManager = PackageManager!;
        var launchIntent = packageManager.GetLaunchIntentForPackage(PackageName!);
        var contentPending = PendingIntent.GetActivity(this, 5, launchIntent,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        var playIcon = _isPlaying
            ? global::Android.Resource.Drawable.IcMediaPause
            : global::Android.Resource.Drawable.IcMediaPlay;

        var favoriteIcon = _isFavorite
            ? global::Android.Resource.Drawable.StarOn
            : global::Android.Resource.Drawable.StarOff;

        var builder = new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle(_title)
            .SetContentText(_artist)
            .SetSmallIcon(global::Android.Resource.Drawable.IcMediaPlay)
            .SetContentIntent(contentPending)
            .SetOngoing(true)
            .SetPriority(NotificationCompat.PriorityHigh)
            .SetVisibility(NotificationCompat.VisibilityPublic);

        if (_albumArt != null)
        {
            builder.SetLargeIcon(_albumArt);
        }

        builder
            .AddAction(global::Android.Resource.Drawable.IcMenuInfoDetails, "歌词", lyricsIntent)
            .AddAction(global::Android.Resource.Drawable.IcMediaPrevious, "上一首", prevIntent)
            .AddAction(playIcon, _isPlaying ? "暂停" : "播放", playPauseIntent)
            .AddAction(global::Android.Resource.Drawable.IcMediaNext, "下一首", nextIntent)
            .AddAction(favoriteIcon, _isFavorite ? "已收藏" : "收藏", favoriteIntent)
            .SetStyle(new NotificationCompat.BigTextStyle().BigText(_artist));

        return builder.Build();
    }

    /// <summary>创建通知渠道（Android 8.0+ 必需），渠道为低优先级、锁屏可见、不显示角标</summary>
    private void CreateNotificationChannel()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            var channel = new NotificationChannel(ChannelId, ChannelName, NotificationImportance.Low)
            {
                Description = "猫爪音乐播放控制",
                LockscreenVisibility = NotificationVisibility.Public
            };
            channel.SetShowBadge(false);
            var manager = GetSystemService(NotificationService) as NotificationManager;
            manager?.CreateNotificationChannel(channel);
        }
    }

    /// <summary>服务销毁时回调：释放 MediaSession 与封面资源，并清空静态实例引用</summary>
    public override void OnDestroy()
    {
        if (_mediaSession != null)
        {
            _mediaSession.Active = false;
            _mediaSession.Release();
            _mediaSession = null;
        }
        _albumArt?.Recycle();
        _albumArt = null;
        _instance = null;
        base.OnDestroy();
    }

    /// <summary>MediaSession 回调实现：将系统/外部设备的播放控制指令转发到本服务的事件</summary>
    private class MediaSessionCallback : MediaSession.Callback
    {
        /// <summary>关联的前台播放服务实例</summary>
        private readonly ForegroundPlayerService _service;

        /// <summary>构造回调并关联服务实例</summary>
        /// <param name="service">关联的前台播放服务实例</param>
        public MediaSessionCallback(ForegroundPlayerService service)
        {
            _service = service;
        }

        /// <summary>系统"播放"指令回调：标记为播放中并触发 OnPlayPauseRequested 事件</summary>
        public override void OnPlay()
        {
            _service._isPlaying = true;
            OnPlayPauseRequested?.Invoke(true);
            _service.UpdateNotification(_service._title, _service._artist, true, _service._isFavorite, _service._albumArt);
        }

        /// <summary>系统"暂停"指令回调：标记为未播放并触发 OnPlayPauseRequested 事件</summary>
        public override void OnPause()
        {
            _service._isPlaying = false;
            OnPlayPauseRequested?.Invoke(false);
            _service.UpdateNotification(_service._title, _service._artist, false, _service._isFavorite, _service._albumArt);
        }

        /// <summary>系统"下一首"指令回调：触发 OnNextRequested 事件</summary>
        public override void OnSkipToNext() => OnNextRequested?.Invoke();
        /// <summary>系统"上一首"指令回调：触发 OnPreviousRequested 事件</summary>
        public override void OnSkipToPrevious() => OnPreviousRequested?.Invoke();
        /// <summary>系统"停止"指令回调：调用服务的 StopPlayback 停止播放</summary>
        public override void OnStop() => _service.StopPlayback();
    }
}
