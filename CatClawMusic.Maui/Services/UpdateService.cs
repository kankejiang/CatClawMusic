using System.Net.Http;
using System.Text.Json;
using CatClawMusic.Core.Services;

namespace CatClawMusic.Maui.Services;

/// <summary>GitHub Release 版本检查服务（跨平台实现）</summary>
public class UpdateService : IUpdateService
{
    private const string GithubApiUrl =
        "https://api.github.com/repos/kankejiang/CatClawMusic/releases/latest";

    private const string KeyIgnoredVersion = "update_ignored_version";
    private const string KeyPendingVersion = "update_pending_version";

    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    static UpdateService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("CatClawMusic");
    }

    /// <summary>
    /// 异步检查 GitHub Release 是否有新版本。
    /// 比较远端最新版本号与本地版本号（仅比较前 3 段）。
    /// </summary>
    /// <returns>有新版本时返回最新版本号字符串；否则返回 null</returns>
    public async Task<string?> CheckUpdateAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync(GithubApiUrl);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("tag_name", out var tagProp))
                return null;

            var latestTag = tagProp.GetString() ?? "";
            if (string.IsNullOrWhiteSpace(latestTag)) return null;

            var latestVersion = latestTag.TrimStart('v');
            var currentVersion = GetCurrentVersion();
            if (CompareVersion(latestVersion, currentVersion) > 0)
                return latestVersion;

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>标记指定版本为"已通知用户"，同时清空待处理版本</summary>
    /// <param name="version">已通知的版本号</param>
    public void MarkVersionNotified(string version)
    {
        Preferences.Default.Set(KeyIgnoredVersion, version);
        Preferences.Default.Set(KeyPendingVersion, "");
    }

    /// <summary>获取用户已忽略的版本号</summary>
    /// <returns>已忽略的版本号字符串；未设置时返回空字符串</returns>
    public string GetIgnoredVersion()
        => Preferences.Default.Get(KeyIgnoredVersion, "");

    /// <summary>设置待处理的更新版本号（用于应用启动时提示用户）</summary>
    /// <param name="version">待处理版本号</param>
    public void SetPendingVersion(string version)
        => Preferences.Default.Set(KeyPendingVersion, version);

    /// <summary>获取待处理的更新版本号</summary>
    /// <returns>待处理版本号字符串；未设置时返回空字符串</returns>
    public string GetPendingVersion()
        => Preferences.Default.Get(KeyPendingVersion, "");

    private static string GetCurrentVersion()
    {
        try
        {
            return AppInfo.Current.VersionString;
        }
        catch
        {
            return "0.0.0";
        }
    }

    private static int CompareVersion(string a, string b)
    {
        var pa = ParseVersion(a);
        var pb = ParseVersion(b);
        for (int i = 0; i < 3; i++)
        {
            if (pa[i] > pb[i]) return 1;
            if (pa[i] < pb[i]) return -1;
        }
        return 0;
    }

    private static int[] ParseVersion(string v)
    {
        var parts = (v ?? "0.0.0").Split('.');
        var result = new int[3];
        for (int i = 0; i < 3; i++)
        {
            if (i < parts.Length && int.TryParse(parts[i], out var n))
                result[i] = n;
        }
        return result;
    }
}
