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

    public async Task<string?> CheckUpdateAsync()
    {
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("CatClawMusic");
            client.Timeout = TimeSpan.FromSeconds(10);

            var response = await client.GetAsync(GithubApiUrl);
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

    public void MarkVersionNotified(string version)
    {
        Preferences.Default.Set(KeyIgnoredVersion, version);
        Preferences.Default.Set(KeyPendingVersion, "");
    }

    public string GetIgnoredVersion()
        => Preferences.Default.Get(KeyIgnoredVersion, "");

    public void SetPendingVersion(string version)
        => Preferences.Default.Set(KeyPendingVersion, version);

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
