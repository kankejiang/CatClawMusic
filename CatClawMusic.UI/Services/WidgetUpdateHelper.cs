using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.UI.ViewModels;
using CatClawMusic.UI.Widgets;
using ALog = Android.Util.Log;

namespace CatClawMusic.UI.Services;

/// <summary>桌面小组件状态更新辅助类，负责构造 RemoteViews 并转发播放控制广播</summary>
public static class WidgetUpdateHelper
{
    public const string ActionToggle = "com.catclaw.music.widget.TOGGLE";
    public const string ActionNext = "com.catclaw.music.widget.NEXT";
    public const string ActionPrevious = "com.catclaw.music.widget.PREVIOUS";
    public const string ActionFavorite = "com.catclaw.music.widget.FAVORITE";
    public const string ActionOpen = "com.catclaw.music.widget.OPEN";

    private class WidgetStateSnapshot
    {
        public string Title { get; set; } = "猫爪音乐";
        public string Artist { get; set; } = "未在播放";
        public string CoverPath { get; set; } = "";
        public bool IsPlaying { get; set; }
        public bool IsLiked { get; set; }
        public long PositionMs { get; set; }
        public long DurationMs { get; set; }
    }

    private static readonly WidgetStateSnapshot _snapshot = new();

    /// <summary>从播放服务状态刷新快照并更新所有已放置的小组件</summary>
    public static void UpdateFromService(Context context, NowPlayingViewModel? vm, IAudioPlayerService? player)
    {
        var song = vm?.CurrentSong;
        _snapshot.Title = song?.Title ?? "猫爪音乐";
        _snapshot.Artist = string.IsNullOrEmpty(song?.Artist) ? "未在播放" : song.Artist;
        _snapshot.CoverPath = vm?.CoverSource ?? "";
        _snapshot.IsPlaying = player?.IsPlaying ?? false;
        _snapshot.IsLiked = vm?.IsLiked ?? false;
        _snapshot.PositionMs = player?.RealtimePositionMs ?? 0;
        _snapshot.DurationMs = (long)(player?.Duration.TotalMilliseconds ?? 0);
        if (_snapshot.DurationMs <= 0 && song?.Duration > 0)
            _snapshot.DurationMs = song.Duration;

        UpdateAllWidgets(context);
    }

    /// <summary>使用最近一次快照更新所有小组件</summary>
    public static void UpdateAllWidgets(Context context)
    {
        UpdateAllWidgets(context, _snapshot.Title, _snapshot.Artist, _snapshot.CoverPath,
            _snapshot.IsPlaying, _snapshot.IsLiked, _snapshot.PositionMs, _snapshot.DurationMs);
    }

    /// <summary>使用指定状态更新所有小组件</summary>
    public static void UpdateAllWidgets(
        Context context,
        string title,
        string artist,
        string coverPath,
        bool isPlaying,
        bool isLiked,
        long positionMs,
        long durationMs)
    {
        var appWidgetManager = AppWidgetManager.GetInstance(context);
        if (appWidgetManager == null) return;

        UpdateWidget4x1(context, appWidgetManager, title, artist, coverPath, isPlaying, isLiked, positionMs, durationMs);
        UpdateWidget4x2(context, appWidgetManager, title, artist, coverPath, isPlaying, isLiked, positionMs, durationMs);
        UpdateWidget4x6(context, appWidgetManager, title, artist, coverPath, isPlaying, isLiked, positionMs, durationMs);
    }

    private static void UpdateWidget4x1(
        Context context,
        AppWidgetManager appWidgetManager,
        string title,
        string artist,
        string coverPath,
        bool isPlaying,
        bool isLiked,
        long positionMs,
        long durationMs)
    {
        var component = new ComponentName(context, Java.Lang.Class.FromType(typeof(MusicWidget4x1)));
        var ids = appWidgetManager.GetAppWidgetIds(component);
        if (ids == null || ids.Length == 0) return;

        var views = new RemoteViews(context.PackageName, Resource.Layout.widget_music_4x1);
        views.SetTextViewText(Resource.Id.widget_title, title);
        views.SetTextViewText(Resource.Id.widget_artist, artist);
        SetCover(context, views, Resource.Id.widget_cover, coverPath, 160);

        var playIcon = isPlaying ? Resource.Drawable.ic_pause : Resource.Drawable.ic_play;
        views.SetImageViewResource(Resource.Id.widget_btn_play_pause, playIcon);

        views.SetOnClickPendingIntent(Resource.Id.widget_btn_play_pause, BuildBroadcastIntent(context, typeof(MusicWidget4x1), ActionToggle));
        views.SetOnClickPendingIntent(Resource.Id.widget_btn_next, BuildBroadcastIntent(context, typeof(MusicWidget4x1), ActionNext));
        views.SetOnClickPendingIntent(Resource.Id.widget_root, BuildBroadcastIntent(context, typeof(MusicWidget4x1), ActionOpen));

        appWidgetManager.UpdateAppWidget(ids, views);
    }

    private static void UpdateWidget4x2(
        Context context,
        AppWidgetManager appWidgetManager,
        string title,
        string artist,
        string coverPath,
        bool isPlaying,
        bool isLiked,
        long positionMs,
        long durationMs)
    {
        var component = new ComponentName(context, Java.Lang.Class.FromType(typeof(MusicWidget4x2)));
        var ids = appWidgetManager.GetAppWidgetIds(component);
        if (ids == null || ids.Length == 0) return;

        var views = new RemoteViews(context.PackageName, Resource.Layout.widget_music_4x2);
        views.SetTextViewText(Resource.Id.widget_title, title);
        views.SetTextViewText(Resource.Id.widget_artist, artist);
        views.SetTextViewText(Resource.Id.widget_time, FormatTime(positionMs, durationMs));
        SetCover(context, views, Resource.Id.widget_cover, coverPath, 320);

        var progress = durationMs > 0 ? (int)(positionMs * 100 / durationMs) : 0;
        views.SetProgressBar(Resource.Id.widget_progress, 100, progress, false);

        var playIcon = isPlaying ? Resource.Drawable.ic_pause : Resource.Drawable.ic_play;
        views.SetImageViewResource(Resource.Id.widget_btn_play_pause, playIcon);
        var likeIcon = isLiked ? Resource.Drawable.ic_widget_favorite : Resource.Drawable.ic_widget_favorite_border;
        views.SetImageViewResource(Resource.Id.widget_btn_like, likeIcon);

        views.SetOnClickPendingIntent(Resource.Id.widget_btn_prev, BuildBroadcastIntent(context, typeof(MusicWidget4x2), ActionPrevious));
        views.SetOnClickPendingIntent(Resource.Id.widget_btn_play_pause, BuildBroadcastIntent(context, typeof(MusicWidget4x2), ActionToggle));
        views.SetOnClickPendingIntent(Resource.Id.widget_btn_next, BuildBroadcastIntent(context, typeof(MusicWidget4x2), ActionNext));
        views.SetOnClickPendingIntent(Resource.Id.widget_btn_like, BuildBroadcastIntent(context, typeof(MusicWidget4x2), ActionFavorite));
        views.SetOnClickPendingIntent(Resource.Id.widget_root, BuildBroadcastIntent(context, typeof(MusicWidget4x2), ActionOpen));

        appWidgetManager.UpdateAppWidget(ids, views);
    }

    private static void UpdateWidget4x6(
        Context context,
        AppWidgetManager appWidgetManager,
        string title,
        string artist,
        string coverPath,
        bool isPlaying,
        bool isLiked,
        long positionMs,
        long durationMs)
    {
        var component = new ComponentName(context, Java.Lang.Class.FromType(typeof(MusicWidget4x6)));
        var ids = appWidgetManager.GetAppWidgetIds(component);
        if (ids == null || ids.Length == 0) return;

        var views = new RemoteViews(context.PackageName, Resource.Layout.widget_music_4x6);
        views.SetTextViewText(Resource.Id.widget_title, title);
        views.SetTextViewText(Resource.Id.widget_artist, artist);
        views.SetTextViewText(Resource.Id.widget_time, FormatTime(positionMs, durationMs));
        SetCover(context, views, Resource.Id.widget_cover, coverPath, 640);

        var progress = durationMs > 0 ? (int)(positionMs * 100 / durationMs) : 0;
        views.SetProgressBar(Resource.Id.widget_progress, 100, progress, false);

        var playIcon = isPlaying ? Resource.Drawable.ic_pause : Resource.Drawable.ic_play;
        views.SetImageViewResource(Resource.Id.widget_btn_play_pause, playIcon);
        var likeIcon = isLiked ? Resource.Drawable.ic_widget_favorite : Resource.Drawable.ic_widget_favorite_border;
        views.SetImageViewResource(Resource.Id.widget_btn_like, likeIcon);

        views.SetOnClickPendingIntent(Resource.Id.widget_btn_prev, BuildBroadcastIntent(context, typeof(MusicWidget4x6), ActionPrevious));
        views.SetOnClickPendingIntent(Resource.Id.widget_btn_play_pause, BuildBroadcastIntent(context, typeof(MusicWidget4x6), ActionToggle));
        views.SetOnClickPendingIntent(Resource.Id.widget_btn_next, BuildBroadcastIntent(context, typeof(MusicWidget4x6), ActionNext));
        views.SetOnClickPendingIntent(Resource.Id.widget_btn_like, BuildBroadcastIntent(context, typeof(MusicWidget4x6), ActionFavorite));
        views.SetOnClickPendingIntent(Resource.Id.widget_root, BuildBroadcastIntent(context, typeof(MusicWidget4x6), ActionOpen));

        appWidgetManager.UpdateAppWidget(ids, views);
    }

    private static void SetCover(Context context, RemoteViews views, int viewId, string coverPath, int size)
    {
        try
        {
            if (!string.IsNullOrEmpty(coverPath) && File.Exists(coverPath))
            {
                var bitmap = LoadCoverBitmap(coverPath, size);
                if (bitmap != null)
                {
                    views.SetImageViewBitmap(viewId, bitmap);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            ALog.Warn("CatClaw", $"Widget cover load failed: {ex.Message}");
        }
        views.SetImageViewResource(viewId, Resource.Drawable.cover_default);
    }

    private static Bitmap? LoadCoverBitmap(string coverPath, int size)
    {
        try
        {
            var options = new BitmapFactory.Options
            {
                InJustDecodeBounds = true
            };
            BitmapFactory.DecodeFile(coverPath, options);

            if (options.OutWidth <= 0 || options.OutHeight <= 0)
                return null;

            var scale = 1;
            while (options.OutWidth / scale > size || options.OutHeight / scale > size)
                scale *= 2;

            options.InJustDecodeBounds = false;
            options.InSampleSize = scale;
            return BitmapFactory.DecodeFile(coverPath, options);
        }
        catch
        {
            return null;
        }
    }

    private static PendingIntent BuildBroadcastIntent(Context context, Type providerType, string action)
    {
        var intent = new Intent(context, providerType);
        intent.SetAction(action);
        return PendingIntent.GetBroadcast(context, action.GetHashCode() & 0xFFFF, intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
    }

    private static string FormatTime(long positionMs, long durationMs)
    {
        var position = TimeSpan.FromMilliseconds(positionMs);
        var duration = TimeSpan.FromMilliseconds(durationMs);
        return $"{(int)position.TotalMinutes:D2}:{position.Seconds:D2} / {(int)duration.TotalMinutes:D2}:{duration.Seconds:D2}";
    }

    /// <summary>处理小组件发出的广播动作，转发到前台播放服务或打开应用</summary>
    public static void HandleAction(Context context, string action)
    {
        switch (action)
        {
            case ActionToggle:
                StartServiceAction(context, "catclaw_toggle_play");
                break;
            case ActionNext:
                StartServiceAction(context, "catclaw_next");
                break;
            case ActionPrevious:
                StartServiceAction(context, "catclaw_previous");
                break;
            case ActionFavorite:
                StartServiceAction(context, "catclaw_favorite");
                break;
            case ActionOpen:
                OpenApp(context);
                break;
        }
    }

    private static void StartServiceAction(Context context, string serviceAction)
    {
        try
        {
            var intent = new Intent(context, typeof(ForegroundPlayerService));
            intent.SetAction(serviceAction);
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                context.StartForegroundService(intent);
            else
                context.StartService(intent);
        }
        catch (Java.Lang.IllegalStateException ex)
        {
            ALog.Warn("CatClaw", $"Widget start service failed: {ex.Message}");
        }
    }

    private static void OpenApp(Context context)
    {
        try
        {
            var intent = new Intent(context, typeof(MainActivity));
            intent.SetAction(Intent.ActionMain);
            intent.AddCategory(Intent.CategoryLauncher);
            intent.SetFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
            context.StartActivity(intent);
        }
        catch (Exception ex)
        {
            ALog.Warn("CatClaw", $"Widget open app failed: {ex.Message}");
        }
    }
}
