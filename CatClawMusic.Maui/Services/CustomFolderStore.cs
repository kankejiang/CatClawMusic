using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Maui.Storage;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// 自定义音乐文件夹的持久化存储。
/// <para>
/// 不使用 MAUI <c>Preferences</c>：本应用为未打包（WindowsPackageType=None）的 WinUI 程序，
/// <c>Windows.Storage.ApplicationData.Current</c> 为 <c>null</c>，MAUI 的 Preferences 在 Windows 上会被内部
/// 空值守卫静默失效（<c>Set</c> 无操作、<c>Get</c> 返回默认值）。这正表现为
/// 「添加音乐文件夹后点击扫描，直接显示『本地音乐库已清空』」——文件夹看似添加成功（选择器正常返回路径，
/// 无报错弹窗），实则从未写入，扫描时 <c>GetCustomFolders()</c> 读回空列表。
/// </para>
/// <para>
/// 改用 <see cref="FileSystem.AppDataDirectory"/> 下的 JSON 文件存储，在打包/未打包 Windows 与 Android 上均可靠工作。
/// 首次读取时若 JSON 文件不存在，会尝试从 <c>Preferences</c> 迁移旧数据（兼容 Android 已有配置）。
/// </para>
/// </summary>
public static class CustomFolderStore
{
    private const string FileName = "custom_music_folders.json";
    private static readonly object _lock = new();

    private static string FilePath => Path.Combine(FileSystem.AppDataDirectory, FileName);

    /// <summary>读取全部自定义音乐文件夹路径（去重由调用方保证）。</summary>
    public static List<string> GetFolders()
    {
        try
        {
            var path = FilePath;
            if (!File.Exists(path))
            {
                // 首次：尝试从 Preferences 迁移（兼容已打包 / Android 的历史数据）
                try
                {
                    var legacy = Preferences.Get("custom_music_folders", "");
                    if (!string.IsNullOrEmpty(legacy))
                    {
                        var migrated = JsonSerializer.Deserialize<List<string>>(legacy);
                        if (migrated != null && migrated.Count > 0)
                        {
                            SaveLocked(migrated);
                            return migrated;
                        }
                    }
                }
                catch
                {
                    // Preferences 在当前平台不可用时忽略迁移
                }

                return new List<string>();
            }

            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return new List<string>();
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    /// <summary>添加（去重）一个自定义音乐文件夹路径。</summary>
    public static void AddFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return;
        try
        {
            lock (_lock)
            {
                var folders = GetFolders();
                if (!folders.Contains(folder))
                {
                    folders.Add(folder);
                    SaveLocked(folders);
                }
            }
        }
        catch
        {
            // 持久化失败时静默忽略（不影响 UI 交互）
        }
    }

    /// <summary>从存储中移除一个自定义音乐文件夹路径。</summary>
    public static void RemoveFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return;
        try
        {
            lock (_lock)
            {
                var folders = GetFolders();
                if (folders.Remove(folder))
                {
                    SaveLocked(folders);
                }
            }
        }
        catch
        {
            // 持久化失败时静默忽略
        }
    }

    private static void SaveLocked(List<string> folders)
    {
        var path = FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(folders));
    }
}
