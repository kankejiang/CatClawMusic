namespace CatClawMusic.Core.Services;

/// <summary>
/// 音乐工具类（从方糖音乐播放器移植）
/// </summary>
public static class MusicUtility
{
    private static readonly string[] AudioExtensions = { ".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a" };

    /// <summary>
    /// 秒转时间格式
    /// </summary>
    public static string SecToHms(double duration)
    {
        var ts = new TimeSpan(0, 0, Convert.ToInt32(duration));
        if (ts.Hours > 0)
            return $"{ts.Hours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
        if (ts.Minutes > 0)
            return $"00:{ts.Minutes:00}:{ts.Seconds:00}";
        return $"00:00:{ts.Seconds:00}";
    }

    /// <summary>
    /// 秒转 mm:ss 格式（用于播放时间显示）
    /// </summary>
    public static string SecToMinSec(double duration)
    {
        var ts = TimeSpan.FromSeconds(duration);
        if (ts.Hours > 0)
            return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
    }

    /// <summary>
    /// 截取两个字符串之间的内容
    /// </summary>
    public static string Substring(string source, string startStr, string endStr)
    {
        var startIndex = source.IndexOf(startStr, StringComparison.Ordinal);
        if (startIndex == -1) return string.Empty;

        var tmpStr = source.Substring(startIndex + startStr.Length);
        var endIndex = tmpStr.IndexOf(endStr, StringComparison.Ordinal);
        if (endIndex == -1) return string.Empty;

        return tmpStr.Substring(0, endIndex);
    }

    /// <summary>
    /// 单文件夹扫描音频文件
    /// </summary>
    public static List<string> ScanFolder(string folderPath)
    {
        var results = new List<string>();
        if (!Directory.Exists(folderPath)) return results;

        foreach (var ext in AudioExtensions)
        {
            try
            {
                var files = Directory.GetFiles(folderPath, "*" + ext);
                results.AddRange(files);
            }
            catch
            {
                // 忽略权限错误
            }
        }

        return results;
    }

    /// <summary>
    /// 递归扫描目录下所有音频文件
    /// </summary>
    public static List<string> ScanFolderRecursive(string rootPath)
    {
        var results = new List<string>();
        if (!Directory.Exists(rootPath)) return results;

        ScanDirectoryRecursive(new DirectoryInfo(rootPath), results);
        return results;
    }

    private static void ScanDirectoryRecursive(DirectoryInfo dir, List<string> results)
    {
        if (!dir.Exists) return;

        try
        {
            foreach (var file in dir.EnumerateFiles())
            {
                var ext = file.Extension.ToUpperInvariant();
                foreach (var audioExt in AudioExtensions)
                {
                    if (ext == audioExt.ToUpperInvariant())
                    {
                        if (!results.Contains(file.FullName))
                            results.Add(file.FullName);
                        break;
                    }
                }
            }

            foreach (var subDir in dir.EnumerateDirectories())
            {
                ScanDirectoryRecursive(subDir, results);
            }
        }
        catch
        {
            // 忽略权限错误
        }
    }
}
