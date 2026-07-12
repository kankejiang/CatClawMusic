using System.Text.Json;
using Microsoft.Maui.Storage;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// 猫爪圈设置的持久化存储。
/// <para>
/// 不使用 MAUI <c>Preferences</c>：本应用为未打包（WindowsPackageType=None）的 WinUI 程序，
/// <c>Windows.Storage.ApplicationData.Current</c> 为 <c>null</c>，Preferences 在 Windows 上会被静默失效。
/// 改用 <see cref="FileSystem.AppDataDirectory"/> 下的 JSON 文件存储，在所有平台上可靠工作。
/// </para>
/// </summary>
public static class ClawCircleSettingsStore
{
    private const string FileName = "clawcircle_settings.json";
    private static readonly object _lock = new();

    private static string FilePath => Path.Combine(FileSystem.AppDataDirectory, FileName);

    /// <summary>读取猫爪圈设置；文件不存在时返回带默认值的新实例。</summary>
    public static ClawCircleSettings Load()
    {
        try
        {
            var path = FilePath;
            if (!File.Exists(path)) return new ClawCircleSettings();

            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return new ClawCircleSettings();
            var loaded = JsonSerializer.Deserialize<ClawCircleSettings>(json);
            return loaded ?? new ClawCircleSettings();
        }
        catch
        {
            return new ClawCircleSettings();
        }
    }

    /// <summary>保存猫爪圈设置到磁盘。</summary>
    public static void Save(ClawCircleSettings settings)
    {
        if (settings == null) return;
        try
        {
            lock (_lock)
            {
                var path = FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(settings));
            }
        }
        catch
        {
            // 持久化失败时静默忽略（不影响 UI 交互）
        }
    }
}

/// <summary>猫爪圈设置数据。</summary>
public class ClawCircleSettings
{
    /// <summary>是否启用猫爪圈（总开关）</summary>
    public bool Enabled { get; set; }

    /// <summary>本机在圈内显示的设备名</summary>
    public string DeviceName { get; set; } = "";

    /// <summary>是否对外共享本地曲库</summary>
    public bool ShareLibrary { get; set; } = true;

    /// <summary>启动应用时自动开启猫爪圈</summary>
    public bool AutoStart { get; set; }

    /// <summary>Stage 2 tracker 的 WebSocket 地址（形如 ws://nas-ip:37823/ws/clawcircle）。空=仅局域网。</summary>
    public string TrackerUrl { get; set; } = "";

    /// <summary>tracker 访问令牌（与媒体中心 AccessToken 一致）。</summary>
    public string TrackerToken { get; set; } = "";
}
