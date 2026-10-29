namespace CatClawMusic.WinUI.Infrastructure;

/// <summary>
/// 应用数据目录（替代 MAUI 的 <c>FileSystem.AppDataDirectory</c> / <c>CacheDirectory</c>）。
/// 与 MAUI 版刻意分开：MAUI 落在 <c>%LOCALAPPDATA%\CatClawMusic.Maui\...</c>，
/// 本工程落在 <c>%LOCALAPPDATA%\CatClawMusic\</c> —— 两版可以同时运行而不抢 SQLite 锁。
/// </summary>
public static class AppPaths
{
    /// <summary>应用根目录</summary>
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CatClawMusic");

    /// <summary>数据目录（数据库、设置、插件、封面缓存）</summary>
    public static string DataDirectory => EnsureDirectory(Root);

    /// <summary>缓存目录</summary>
    public static string CacheDirectory => EnsureDirectory(Path.Combine(Root, "cache"));

    /// <summary>日志目录</summary>
    public static string LogDirectory => EnsureDirectory(Path.Combine(Root, "logs"));

    /// <summary>插件目录（.ccp）</summary>
    public static string PluginDirectory => EnsureDirectory(Path.Combine(Root, "Plugin"));

    /// <summary>封面缓存目录（从音频内嵌图抽出后的落盘位置）</summary>
    public static string CoversDirectory => EnsureDirectory(Path.Combine(CacheDirectory, "covers"));

    /// <summary>SQLite 数据库</summary>
    public static string DatabasePath => Path.Combine(DataDirectory, "catclaw.db");

    /// <summary>设置文件（替代 MAUI Preferences）</summary>
    public static string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    private static string EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
