using Android.Content;
using CatClawMusic.Core.Models;

namespace CatClawMusic.UI.Services;

/// <summary>
/// 扫描设置管理类，用于持久化存储和读取音乐扫描相关的配置项。
/// 基于 Android 的 SharedPreferences 实现键值对存储，所有设置在应用重启后依然保留。
/// </summary>
public static class ScanSettings
{
    /// <summary>
    /// SharedPreferences 文件名称，用于隔离扫描相关的配置数据。
    /// </summary>
    private const string PrefsName = "scan_settings";

    /// <summary>
    /// 配置键：是否使用 MediaStore 作为音频扫描数据源。
    /// </summary>
    private const string KeyUseMediaStore = "use_media_store";

    /// <summary>
    /// 配置键：是否过滤短音频文件（如铃声、通知音等）。
    /// </summary>
    private const string KeyFilterShortAudio = "filter_short_audio";

    /// <summary>
    /// 配置键：是否使用 SAF（Storage Access Framework）扫描文件夹。
    /// </summary>
    private const string KeyUseSafScanner = "use_saf_scanner";

    /// <summary>
    /// 配置键：短音频过滤的最小时长阈值，单位为秒。
    /// </summary>
    private const string KeyMinDurationSec = "min_duration_sec";

    /// <summary>
    /// 获取当前应用的 SharedPreferences 实例。
    /// 使用私有模式（FileCreationMode.Private），确保配置文件仅本应用可访问。
    /// </summary>
    /// <returns>ISharedPreferences 实例</returns>
    private static ISharedPreferences GetPrefs()
        => global::Android.App.Application.Context.GetSharedPreferences(PrefsName, FileCreationMode.Private)!;

    /// <summary>
    /// 获取或设置是否使用 MediaStore 作为音频扫描数据源。
    /// <list type="bullet">
    ///   <item><c>true</c>：通过 Android MediaStore API 扫描音频，效率更高且能获取元数据。</item>
    ///   <item><c>false</c>：通过文件系统直接扫描音频文件。</item>
    /// </list>
    /// 默认值为 <c>true</c>。
    /// </summary>
    public static bool UseMediaStore
    {
        get => GetPrefs().GetBoolean(KeyUseMediaStore, true);
        set => GetPrefs().Edit().PutBoolean(KeyUseMediaStore, value).Apply();
    }

    /// <summary>
    /// 获取或设置是否使用 SAF（Storage Access Framework）扫描文件夹。
    /// <list type="bullet">
    ///   <item><c>true</c>：通过 SAF 选择文件夹，返回 content:// URI，无需额外权限。</item>
    ///   <item><c>false</c>：通过自建文件浏览器选择文件夹，返回真实文件路径，需要"所有文件访问"权限。</item>
    /// </list>
    /// 默认值为 <c>false</c>。
    /// </summary>
    public static bool UseSafScanner
    {
        get => GetPrefs().GetBoolean(KeyUseSafScanner, false);
        set => GetPrefs().Edit().PutBoolean(KeyUseSafScanner, value).Apply();
    }

    /// <summary>
    /// 获取或设置是否过滤短音频文件。
    /// <list type="bullet">
    ///   <item><c>true</c>：仅保留时长大于等于 <see cref="MinDurationSec"/> 的音频。</item>
    ///   <item><c>false</c>：保留所有音频文件，不做时长过滤。</item>
    /// </list>
    /// 默认值为 <c>true</c>。
    /// </summary>
    public static bool FilterShortAudio
    {
        get => GetPrefs().GetBoolean(KeyFilterShortAudio, true);
        set => GetPrefs().Edit().PutBoolean(KeyFilterShortAudio, value).Apply();
    }

    /// <summary>
    /// 获取或设置短音频过滤的最小时长阈值（单位：秒）。
    /// 仅当 <see cref="FilterShortAudio"/> 为 <c>true</c> 时生效。
    /// 时长低于此值的音频文件将被过滤掉，默认值为 60 秒。
    /// </summary>
    public static int MinDurationSec
    {
        get => GetPrefs().GetInt(KeyMinDurationSec, 60);
        set => GetPrefs().Edit().PutInt(KeyMinDurationSec, value).Apply();
    }

    /// <summary>
    /// 判断指定的歌曲是否应包含在扫描结果中。
    /// 当 <see cref="FilterShortAudio"/> 为 <c>false</c> 时，所有歌曲均通过；
    /// 否则仅保留时长大于等于 <see cref="MinDurationSec"/> 的歌曲。
    /// </summary>
    /// <param name="song">待判断的歌曲对象</param>
    /// <returns><c>true</c> 表示应包含该歌曲；<c>false</c> 表示应过滤掉</returns>
    public static bool ShouldIncludeSong(Song song)
    {
        // 如果未启用短音频过滤，则所有歌曲均包含
        if (!FilterShortAudio) return true;
        // 启用过滤时，仅保留时长达到阈值的歌曲
        return song.Duration >= MinDurationSec;
    }

    // ═══════════════════════════════════════════════════════════
    //  本地文件夹路径管理（非 SAF，真实文件路径）
    // ═══════════════════════════════════════════════════════════

    private const string KeyLocalFolderPaths = "local_folder_paths";

    /// <summary>获取通过自建文件浏览器添加的本地文件夹路径列表</summary>
    public static List<string> GetLocalFolderPaths()
    {
        var list = GetPrefs().GetString(KeyLocalFolderPaths, null);
        if (string.IsNullOrEmpty(list)) return new List<string>();
        return list.Split('|', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    /// <summary>添加一个本地文件夹路径</summary>
    public static void AddLocalFolderPath(string path)
    {
        var paths = GetLocalFolderPaths();
        if (!paths.Any(p => p.Equals(path, StringComparison.OrdinalIgnoreCase)))
        {
            paths.Add(path);
            SaveLocalFolderPaths(paths);
        }
    }

    /// <summary>移除一个本地文件夹路径</summary>
    public static void RemoveLocalFolderPath(string path)
    {
        var paths = GetLocalFolderPaths()
            .Where(p => !p.Equals(path, StringComparison.OrdinalIgnoreCase))
            .ToList();
        SaveLocalFolderPaths(paths);
    }

    /// <summary>清空所有本地文件夹路径</summary>
    public static void ClearLocalFolderPaths()
    {
        GetPrefs().Edit().Remove(KeyLocalFolderPaths).Apply();
    }

    private static void SaveLocalFolderPaths(List<string> paths)
    {
        GetPrefs().Edit().PutString(KeyLocalFolderPaths, string.Join("|", paths)).Apply();
    }
}
