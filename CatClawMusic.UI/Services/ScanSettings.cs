using Android.Content;
using CatClawMusic.Core.Models;

namespace CatClawMusic.UI.Services;

public static class ScanSettings
{
    private const string PrefsName = "scan_settings";
    private const string KeyUseMediaStore = "use_media_store";
    private const string KeyFilterShortAudio = "filter_short_audio";
    private const string KeyMinDurationSec = "min_duration_sec";

    private static ISharedPreferences GetPrefs()
        => global::Android.App.Application.Context.GetSharedPreferences(PrefsName, FileCreationMode.Private)!;

    public static bool UseMediaStore
    {
        get => GetPrefs().GetBoolean(KeyUseMediaStore, true);
        set => GetPrefs().Edit().PutBoolean(KeyUseMediaStore, value).Apply();
    }

    public static bool FilterShortAudio
    {
        get => GetPrefs().GetBoolean(KeyFilterShortAudio, true);
        set => GetPrefs().Edit().PutBoolean(KeyFilterShortAudio, value).Apply();
    }

    public static int MinDurationSec
    {
        get => GetPrefs().GetInt(KeyMinDurationSec, 60);
        set => GetPrefs().Edit().PutInt(KeyMinDurationSec, value).Apply();
    }

    public static bool ShouldIncludeSong(Song song)
    {
        if (!FilterShortAudio) return true;
        return song.Duration >= MinDurationSec;
    }
}
