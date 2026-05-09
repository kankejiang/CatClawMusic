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

[Service(
    Name = "com.catclaw.music.ForegroundPlayerService",
    Exported = true,
    ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeMediaPlayback)]
public class ForegroundPlayerService : Service
{
    private const string ChannelId = "catclaw_playback_v3";
    private const string NotifGroup = "catclaw_playback_group";
    private const int NotifIdMain = 1001;
    private const int NotifIdTools = 1002;
    private const string PrefKeyDesktopLyric = "desktop_lyric";
    private const string PrefKeyDesktopLyricEnabled = "desktop_lyric_enabled";

    private IAudioPlayerService? _audioPlayer;
    private NowPlayingViewModel? _nowPlayingVm;
    private WifiManager.WifiLock? _wifiLock;
    private MediaSession? _mediaSession;
    private Handler? _progressHandler;
    private bool _started;

    public override IBinder? OnBind(Intent? intent) => null;

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
    }

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
            NotifyTools();
            _started = true;
            StartProgressUpdates();
        }

        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        if (_audioPlayer is not null)
            _audioPlayer.StateChanged -= OnPlaybackStateChanged;

        if (_nowPlayingVm is not null)
            _nowPlayingVm.PropertyChanged -= OnViewModelPropertyChanged;

        _started = false;
        StopForeground(StopForegroundFlags.Remove);
        CancelToolsNotification();
        ReleaseWifiLock();
        StopProgressUpdates();
        ReleaseMediaSession();

        base.OnDestroy();
    }

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

    public static void Stop(Context context)
    {
        var intent = new Intent(context, typeof(ForegroundPlayerService));
        context.StopService(intent);
    }

    #region Progress Timer

    private void StartProgressUpdates()
    {
        if (_progressHandler != null) return;
        _progressHandler = new Handler(Looper.MainLooper!);
        _progressHandler.Post(ProgressTick);
    }

    private void StopProgressUpdates()
    {
        if (_progressHandler != null)
        {
            _progressHandler.RemoveCallbacksAndMessages(null);
            _progressHandler = null;
        }
    }

    private void ProgressTick()
    {
        if (_progressHandler == null) return;
        UpdateMediaSessionPlaybackState();
        _progressHandler.PostDelayed(ProgressTick, 1000);
    }

    #endregion

    #region MediaSession

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

    private class MediaSessionCallback : MediaSession.Callback
    {
        private readonly ForegroundPlayerService _service;
        public MediaSessionCallback(ForegroundPlayerService service) => _service = service;

        public override void OnPlay() => _service._audioPlayer?.ResumeAsync();
        public override void OnPause() => _service._audioPlayer?.PauseAsync();
        public override void OnSkipToNext() => _service._nowPlayingVm?.NextCommand.Execute(null);
        public override void OnSkipToPrevious() => _service._nowPlayingVm?.PreviousCommand.Execute(null);
        public override void OnSeekTo(long pos) => _service._audioPlayer?.SeekAsync(TimeSpan.FromMilliseconds(pos));

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

    private void UpdateMediaSessionPlaybackState()
    {
        if (_mediaSession == null) return;
        try
        {
            var isPlaying = _audioPlayer?.IsPlaying ?? false;
            var state = isPlaying ? PlaybackStateCode.Playing : PlaybackStateCode.Paused;
            var positionMs = (long)(_audioPlayer?.CurrentPosition.TotalMilliseconds ?? 0);

            var playbackState = new Android.Media.Session.PlaybackState.Builder()
                .SetState(state, positionMs, 1.0f)
                .SetActions(
                    Android.Media.Session.PlaybackState.ActionPlay |
                    Android.Media.Session.PlaybackState.ActionPause |
                    Android.Media.Session.PlaybackState.ActionSkipToNext |
                    Android.Media.Session.PlaybackState.ActionSkipToPrevious |
                    Android.Media.Session.PlaybackState.ActionSeekTo)
                .Build();

            _mediaSession.SetPlaybackState(playbackState);
        }
        catch (System.Exception ex)
        {
            ALog.Warn("CatClaw", $"UpdateMediaSessionPlaybackState failed: {ex.Message}");
        }
    }

    private void UpdateMediaSessionMetadata()
    {
        if (_mediaSession == null) return;
        try
        {
            var song = _nowPlayingVm?.CurrentSong;
            var title = song?.Title ?? "猫爪音乐";
            var artist = song?.Artist ?? "未在播放";
            var durationMs = (long)(_audioPlayer?.Duration.TotalMilliseconds ?? 0);

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

    private void CreateNotificationChannels()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O) return;
        var manager = (NotificationManager)GetSystemService(NotificationService)!;

        var channel = new NotificationChannel(ChannelId, "猫爪音乐", NotificationImportance.High)
        {
            Description = "播放控制与快捷操作",
            LockscreenVisibility = NotificationVisibility.Public
        };
        channel.SetSound(null, null);
        channel.EnableVibration(false);
        channel.SetShowBadge(false);
        channel.SetBypassDnd(true);
        manager.CreateNotificationChannel(channel);
    }

    #endregion

    #region Notifications

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

        var builder = new Notification.Builder(this, ChannelId)
            .SetContentTitle(title)
            .SetContentText(artist)
            .SetSmallIcon(Resource.Drawable.ic_play)
            .SetLargeIcon(LoadCoverBitmap())
            .SetVisibility(NotificationVisibility.Public)
            .SetOngoing(true)
            .SetShowWhen(false)
            .SetCategory(Notification.CategoryTransport)
            .SetColor(unchecked((int)0xFF9B7ED8))
            .SetContentIntent(BuildContentPendingIntent())
            .SetStyle(mediaStyle);

        builder.AddAction(BuildAction(Resource.Drawable.ic_notif_previous, "上一曲", "previous"));
        builder.AddAction(BuildAction(playIcon, playLabel, isPlaying ? "pause" : "play"));
        builder.AddAction(BuildAction(Resource.Drawable.ic_notif_next, "下一曲", "next"));

        return builder.Build();
    }

    private void NotifyTools()
    {
        try
        {
            var manager = (NotificationManager)GetSystemService(NotificationService)!;
            manager.Notify(NotifIdTools, BuildToolsNotification());
        }
        catch (System.Exception ex)
        {
            ALog.Warn("CatClaw", $"NotifyTools failed: {ex.Message}");
        }
    }

    private void CancelToolsNotification()
    {
        try
        {
            var manager = (NotificationManager)GetSystemService(NotificationService)!;
            manager.Cancel(NotifIdTools);
        }
        catch { }
    }

    private Notification BuildToolsNotification()
    {
        var isLiked = _nowPlayingVm?.IsLiked ?? false;
        var isLyricOn = IsDesktopLyricEnabled();
        var isLocked = DesktopLyricService.Instance.IsLocked;
        var isDualMode = DesktopLyricService.Instance.GetDisplayMode() == 1;

        var favIcon = isLiked ? Resource.Drawable.ic_notif_favorite : Resource.Drawable.ic_notif_favorite_border;
        var lyricIcon = isLyricOn ? Resource.Drawable.ic_notif_lyric_on : Resource.Drawable.ic_notif_lyric_off;

        var remoteViews = new RemoteViews(PackageName!, Resource.Layout.notification_tools);
        remoteViews.SetImageViewResource(Resource.Id.tool_favorite, favIcon);
        remoteViews.SetImageViewResource(Resource.Id.tool_lyric, lyricIcon);
        remoteViews.SetImageViewResource(Resource.Id.tool_lock, isLocked
            ? Resource.Drawable.ic_lock_locked : Resource.Drawable.ic_lock);
        remoteViews.SetImageViewResource(Resource.Id.tool_mode, isDualMode
            ? Resource.Drawable.ic_mode_dual : Resource.Drawable.ic_mode_single);

        remoteViews.SetOnClickPendingIntent(Resource.Id.tool_favorite, BuildActionIntent("favorite"));
        remoteViews.SetOnClickPendingIntent(Resource.Id.tool_lyric, BuildActionIntent("lyric_toggle"));
        remoteViews.SetOnClickPendingIntent(Resource.Id.tool_lock, BuildActionIntent("lyric_lock"));
        remoteViews.SetOnClickPendingIntent(Resource.Id.tool_mode,
            BuildActionIntent(isDualMode ? "lyric_single" : "lyric_dual"));

        return new Notification.Builder(this, ChannelId)
            .SetSmallIcon(Resource.Drawable.ic_play)
            .SetVisibility(NotificationVisibility.Public)
            .SetOngoing(true)
            .SetShowWhen(false)
            .SetContentTitle("快捷操作")
            .SetCustomContentView(remoteViews)
            .Build();
    }

    #endregion

    #region Helpers

    private Notification.Action BuildAction(int iconRes, string title, string action)
    {
        var icon = Icon.CreateWithResource(this, iconRes);
        var pendingIntent = BuildActionIntent(action);
        return new Notification.Action.Builder(icon, title, pendingIntent).Build();
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

    private bool IsDesktopLyricEnabled()
    {
        var prefs = GetSharedPreferences(PrefKeyDesktopLyric, FileCreationMode.Private);
        return prefs?.GetBoolean(PrefKeyDesktopLyricEnabled, false) ?? false;
    }

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

    private PendingIntent BuildActionIntent(string action)
    {
        var intent = new Intent(this, typeof(ForegroundPlayerService));
        intent.SetAction("catclaw_" + action);
        return PendingIntent.GetService(this, action.GetHashCode() & 0xFFFF, intent,
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

    #endregion

    #region Actions + Update

    private void UpdateNotification()
    {
        try
        {
            var manager = (NotificationManager)GetSystemService(NotificationService)!;
            manager.Notify(NotifIdMain, BuildMainNotification());
            manager.Notify(NotifIdTools, BuildToolsNotification());
        }
        catch (System.Exception ex)
        {
            ALog.Warn("CatClaw", $"UpdateNotification failed: {ex.Message}");
        }
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
            case "favorite":
                _nowPlayingVm?.ToggleLikeCommand.Execute(null);
                new Handler(Looper.MainLooper!).PostDelayed(() =>
                {
                    UpdateMediaSessionMetadata();
                    UpdateNotification();
                }, 200);
                return;
            case "lyric_toggle":
                ToggleDesktopLyric();
                new Handler(Looper.MainLooper!).PostDelayed(() => UpdateNotification(), 200);
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
        UpdateMediaSessionPlaybackState();
        UpdateNotification();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NowPlayingViewModel.IsLiked))
        {
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
}
