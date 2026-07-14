using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Media;
using Android.Media.Session;
using Android.OS;

namespace CatClawMusic.Maui.Platforms.Android;

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
    public const string ActionLyrics = "com.catclaw.action.LYRICS";
    public const string ActionFavorite = "com.catclaw.action.FAVORITE";

    public const string CustomActionLyrics = "catclaw.custom.LYRICS";
    public const string CustomActionFavorite = "catclaw.custom.FAVORITE";

    private static ForegroundPlayerService? _instance;
    public static ForegroundPlayerService? Instance => _instance;

    private string _title = "未在播放";
    private string _artist = "";
    private bool _isPlaying = false;
    private bool _isFavorite = false;
    private bool _isLyricsEnabled = false;
    private Bitmap? _albumArt;
    private Bitmap? _albumArtSource;
    private long _positionMs;
    private long _durationMs;

    private static string? _pendingTitle;
    private static string? _pendingArtist;

    private MediaSession? _mediaSession;

    public static event Action<bool>? OnPlayPauseRequested;
    public static event Action? OnNextRequested;
    public static event Action? OnPreviousRequested;
    public static event Action<bool>? OnLyricsRequested;
    public static event Action<bool>? OnFavoriteToggled;

    /// <summary>同步桌面歌词按钮状态（当实际开启/关闭与通知栏按钮状态不一致时调用）</summary>
    public static void SyncLyricsEnabled(bool enabled)
    {
        if (_instance == null) return;
        if (_instance._isLyricsEnabled == enabled) return;
        _instance._isLyricsEnabled = enabled;
        _instance.UpdateNotification(_instance._title, _instance._artist, _instance._isPlaying, _instance._isFavorite, _instance._albumArt);
    }

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

    private void InitMediaSession()
    {
        _mediaSession = new MediaSession(this, "CatClawMusic");
        _mediaSession.Active = true;
        _mediaSession.SetCallback(new MediaSessionCallback(this));
        _mediaSession.SetFlags(MediaSessionFlags.HandlesMediaButtons | MediaSessionFlags.HandlesTransportControls);
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        try
        {
            StartForeground(NotificationId, BuildNotification());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FGService] StartForeground failed: {ex.GetType().Name}: {ex.Message}");
            try
            {
                var fallback = new Notification.Builder(this, ChannelId)
                    .SetContentTitle(_title)
                    .SetContentText(_artist)
                    .SetSmallIcon(Resource.Drawable.ic_notif_play)
                    .SetOngoing(true)
                    .SetVisibility(NotificationVisibility.Public)
                    .Build();
                StartForeground(NotificationId, fallback);
            }
            catch (Exception ex2)
            {
                System.Diagnostics.Debug.WriteLine($"[FGService] Fallback StartForeground also failed: {ex2.GetType().Name}: {ex2.Message}");
            }
        }

        if (intent?.Action != null)
        {
            HandleAction(intent.Action);
        }

        return StartCommandResult.Sticky;
    }

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
                _isLyricsEnabled = !_isLyricsEnabled;
                OnLyricsRequested?.Invoke(_isLyricsEnabled);
                UpdateNotification(_title, _artist, _isPlaying, _isFavorite, _albumArt);
                break;
            case ActionFavorite:
                _isFavorite = !_isFavorite;
                OnFavoriteToggled?.Invoke(_isFavorite);
                UpdateNotification(_title, _artist, _isPlaying, _isFavorite, _albumArt);
                break;
        }
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public void UpdateNotification(string title, string artist, bool isPlaying, bool isFavorite = false, Bitmap? albumArt = null, long positionMs = 0, long durationMs = 0)
    {
        _title = title;
        _artist = artist;
        _isPlaying = isPlaying;
        _isFavorite = isFavorite;
        _positionMs = positionMs;
        _durationMs = durationMs;

        if (albumArt != null)
        {
            // 如果传入的就是当前已解码的副本（如收藏/歌词切换时回传 _albumArt），
            // 或者是同一个源 Bitmap（如进度更新时复用缓存），则无需重新解码，避免回收已使用的 Bitmap
            if (!ReferenceEquals(albumArt, _albumArt) && !ReferenceEquals(albumArt, _albumArtSource))
            {
                _albumArt?.Recycle();
                _albumArt = DecodeBitmapDownsampled(albumArt, 512);
                _albumArtSource = albumArt;
            }
        }
        else if (_albumArt != null)
        {
            _albumArt.Recycle();
            _albumArt = null;
            _albumArtSource = null;
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

    public void UpdatePosition(long positionMs)
    {
        _positionMs = positionMs;
        if (_mediaSession == null) return;
        try
        {
            var state = _isPlaying ? PlaybackStateCode.Playing : PlaybackStateCode.Paused;
            var actions = PlaybackState.ActionPlay
                | PlaybackState.ActionPause
                | PlaybackState.ActionSkipToNext
                | PlaybackState.ActionSkipToPrevious
                | PlaybackState.ActionPlayPause
                | PlaybackState.ActionSeekTo;

            var builder = new PlaybackState.Builder()
                .SetActions(actions)
                .SetState(state, positionMs, 1.0f);

            int favoriteIcon = _isFavorite
                ? Resource.Drawable.ic_notif_favorite
                : Resource.Drawable.ic_notif_favorite_border;
            int lyricsIcon = _isLyricsEnabled
                ? Resource.Drawable.ic_notif_lyric_on
                : Resource.Drawable.ic_notif_lyric_off;

            builder.AddCustomAction(new PlaybackState.CustomAction.Builder(
                CustomActionLyrics, _isLyricsEnabled ? "关闭桌面歌词" : "桌面歌词", lyricsIcon).Build());
            builder.AddCustomAction(new PlaybackState.CustomAction.Builder(
                CustomActionFavorite, _isFavorite ? "已收藏" : "收藏", favoriteIcon).Build());

            _mediaSession.SetPlaybackState(builder.Build());
        }
        catch { }
    }

    private static Bitmap? DecodeBitmapDownsampled(Bitmap source, int maxSize)
    {
        try
        {
            int width = source.Width;
            int height = source.Height;
            if (width <= 0 || height <= 0) return null;
            float scale = Math.Min((float)maxSize / width, (float)maxSize / height);
            if (scale >= 1.0f) return source;  // 无需缩小，直接返回原图避免 Copy 分配
            return Bitmap.CreateScaledBitmap(source, (int)(width * scale), (int)(height * scale), true);
        }
        catch
        {
            return null;
        }
    }

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
            | PlaybackState.ActionPlayPause
            | PlaybackState.ActionSeekTo;

        int favoriteIcon = _isFavorite
            ? Resource.Drawable.ic_notif_favorite
            : Resource.Drawable.ic_notif_favorite_border;
        int lyricsIcon = _isLyricsEnabled
            ? Resource.Drawable.ic_notif_lyric_on
            : Resource.Drawable.ic_notif_lyric_off;

        var playbackStateBuilder = new PlaybackState.Builder()
            .SetActions(actions)
            .SetState(state, _positionMs, 1.0f);

        playbackStateBuilder.AddCustomAction(new PlaybackState.CustomAction.Builder(
            CustomActionLyrics, _isLyricsEnabled ? "关闭桌面歌词" : "桌面歌词", lyricsIcon).Build());
        playbackStateBuilder.AddCustomAction(new PlaybackState.CustomAction.Builder(
            CustomActionFavorite, _isFavorite ? "已收藏" : "收藏", favoriteIcon).Build());

        _mediaSession.SetPlaybackState(playbackStateBuilder.Build());

        var metadataBuilder = new MediaMetadata.Builder()
            .PutString(MediaMetadata.MetadataKeyTitle, _title)
            .PutString(MediaMetadata.MetadataKeyArtist, _artist)
            .PutLong(MediaMetadata.MetadataKeyDuration, _durationMs);
        if (_albumArt != null)
        {
            metadataBuilder.PutBitmap(MediaMetadata.MetadataKeyAlbumArt, _albumArt);
            metadataBuilder.PutBitmap(MediaMetadata.MetadataKeyArt, _albumArt);
        }
        _mediaSession.SetMetadata(metadataBuilder.Build());
    }

    public static void Start(global::Android.Content.Context context, string title, string artist)
    {
        _pendingTitle = title;
        _pendingArtist = artist;

        var intent = new Intent(context, typeof(ForegroundPlayerService));
        context.StartForegroundService(intent);
    }

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

    public static void UpdatePlayState(string title, string artist, bool isPlaying, bool isFavorite = false, Bitmap? albumArt = null, long positionMs = 0, long durationMs = 0)
    {
        Instance?.UpdateNotification(title, artist, isPlaying, isFavorite, albumArt, positionMs, durationMs);
    }

    public static void UpdatePlayPosition(long positionMs)
    {
        Instance?.UpdatePosition(positionMs);
    }

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

    private PendingIntent CreateActionIntent(string action, int requestCode)
    {
        var intent = new Intent(this, typeof(ForegroundPlayerService));
        intent.SetAction(action);
        return PendingIntent.GetService(this, requestCode, intent,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
    }

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

        int playIcon = _isPlaying
            ? Resource.Drawable.ic_notif_pause
            : Resource.Drawable.ic_notif_play;
        int favoriteIcon = _isFavorite
            ? Resource.Drawable.ic_notif_favorite
            : Resource.Drawable.ic_notif_favorite_border;
        int lyricsIcon = _isLyricsEnabled
            ? Resource.Drawable.ic_notif_lyric_on
            : Resource.Drawable.ic_notif_lyric_off;

        var builder = new Notification.Builder(this, ChannelId)
            .SetContentTitle(_title)
            .SetContentText(_artist)
            .SetSmallIcon(Resource.Drawable.ic_notif_play)
            .SetContentIntent(contentPending)
            .SetOngoing(true)
            .SetVisibility(NotificationVisibility.Public)
            .SetPriority((int)NotificationPriority.High)
            .SetShowWhen(false);

        if (_albumArt != null)
        {
            builder.SetLargeIcon(_albumArt);
        }

        builder.AddAction(lyricsIcon, _isLyricsEnabled ? "关闭桌面歌词" : "桌面歌词", lyricsIntent);
        builder.AddAction(Resource.Drawable.ic_notif_previous, "上一首", prevIntent);
        builder.AddAction(playIcon, _isPlaying ? "暂停" : "播放", playPauseIntent);
        builder.AddAction(Resource.Drawable.ic_notif_next, "下一首", nextIntent);
        builder.AddAction(favoriteIcon, _isFavorite ? "已收藏" : "收藏", favoriteIntent);

        if (_mediaSession != null)
        {
            try
            {
                var mediaStyle = new Notification.MediaStyle()
                    .SetMediaSession(_mediaSession.SessionToken)
                    .SetShowActionsInCompactView(1, 2, 3);
                builder.SetStyle(mediaStyle);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FGService] MediaStyle build failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        return builder.Build();
    }

    private void CreateNotificationChannel()
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
        _albumArtSource = null;
        _instance = null;
        base.OnDestroy();
    }

    private class MediaSessionCallback : MediaSession.Callback
    {
        private readonly ForegroundPlayerService _service;

        public MediaSessionCallback(ForegroundPlayerService service)
        {
            _service = service;
        }

        public override void OnPlay()
        {
            _service._isPlaying = true;
            OnPlayPauseRequested?.Invoke(true);
            _service.UpdateNotification(_service._title, _service._artist, true, _service._isFavorite, _service._albumArt);
        }

        public override void OnPause()
        {
            _service._isPlaying = false;
            OnPlayPauseRequested?.Invoke(false);
            _service.UpdateNotification(_service._title, _service._artist, false, _service._isFavorite, _service._albumArt);
        }

        public override void OnSkipToNext() => OnNextRequested?.Invoke();
        public override void OnSkipToPrevious() => OnPreviousRequested?.Invoke();
        public override void OnStop() => _service.StopPlayback();

        public override void OnCustomAction(string? action, Bundle? extras)
        {
            if (action == null) return;
            switch (action)
            {
                case CustomActionFavorite:
                    _service._isFavorite = !_service._isFavorite;
                    OnFavoriteToggled?.Invoke(_service._isFavorite);
                    _service.UpdateNotification(_service._title, _service._artist, _service._isPlaying, _service._isFavorite, _service._albumArt);
                    break;
                case CustomActionLyrics:
                    _service._isLyricsEnabled = !_service._isLyricsEnabled;
                    OnLyricsRequested?.Invoke(_service._isLyricsEnabled);
                    _service.UpdateNotification(_service._title, _service._artist, _service._isPlaying, _service._isFavorite, _service._albumArt);
                    break;
            }
        }
    }
}
