using Android.Appwidget;
using Android.Content;
using CatClawMusic.UI.Services;

namespace CatClawMusic.UI.Widgets;

[BroadcastReceiver(
    Name = "com.catclaw.music.widgets.MusicWidget4x1",
    Exported = true,
    Label = "猫爪音乐 4x1")]
[IntentFilter(new[] {
    AppWidgetManager.ActionAppwidgetUpdate,
    AppWidgetManager.ActionAppwidgetEnabled,
    AppWidgetManager.ActionAppwidgetDisabled,
    AppWidgetManager.ActionAppwidgetOptionsChanged
})]
[MetaData("android.appwidget.provider", Resource = "@xml/widget_music_4x1")]
public class MusicWidget4x1 : MusicWidgetBase { }

[BroadcastReceiver(
    Name = "com.catclaw.music.widgets.MusicWidget4x2",
    Exported = true,
    Label = "猫爪音乐 4x2")]
[IntentFilter(new[] {
    AppWidgetManager.ActionAppwidgetUpdate,
    AppWidgetManager.ActionAppwidgetEnabled,
    AppWidgetManager.ActionAppwidgetDisabled,
    AppWidgetManager.ActionAppwidgetOptionsChanged
})]
[MetaData("android.appwidget.provider", Resource = "@xml/widget_music_4x2")]
public class MusicWidget4x2 : MusicWidgetBase { }

[BroadcastReceiver(
    Name = "com.catclaw.music.widgets.MusicWidget4x6",
    Exported = true,
    Label = "猫爪音乐 4x6")]
[IntentFilter(new[] {
    AppWidgetManager.ActionAppwidgetUpdate,
    AppWidgetManager.ActionAppwidgetEnabled,
    AppWidgetManager.ActionAppwidgetDisabled,
    AppWidgetManager.ActionAppwidgetOptionsChanged
})]
[MetaData("android.appwidget.provider", Resource = "@xml/widget_music_4x6")]
public class MusicWidget4x6 : MusicWidgetBase { }

/// <summary>桌面音乐小组件基类，处理 AppWidget 生命周期和播放控制广播</summary>
public abstract class MusicWidgetBase : AppWidgetProvider
{
    public override void OnUpdate(Context? context, AppWidgetManager? appWidgetManager, int[]? appWidgetIds)
    {
        base.OnUpdate(context, appWidgetManager, appWidgetIds);
        if (context != null)
            WidgetUpdateHelper.UpdateAllWidgets(context);
    }

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context != null && intent?.Action != null)
        {
            var action = intent.Action;
            if (action == WidgetUpdateHelper.ActionToggle ||
                action == WidgetUpdateHelper.ActionNext ||
                action == WidgetUpdateHelper.ActionPrevious ||
                action == WidgetUpdateHelper.ActionFavorite ||
                action == WidgetUpdateHelper.ActionOpen)
            {
                WidgetUpdateHelper.HandleAction(context, action);
                return;
            }
        }
        base.OnReceive(context, intent);
    }
}
