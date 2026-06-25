using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Android.Content;
using Android.Preferences;
using CatClawMusic.Core.Services;

namespace CatClawMusic.UI.Services;

/// <summary>GitHub Release 版本检查服务（Android 实现）</summary>
public class UpdateService : IUpdateService
{
    private const string GithubApiUrl =
        "https://api.github.com/repos/kankejiang/CatClawMusic/releases/latest";

    private const string PrefName = "catclaw_update";
    private const string KeyIgnoredVersion = "ignored_version";
    private const string KeyPendingVersion = "pending_version";  // 有待提示的版本

    private readonly Context _context;

    public UpdateService()
    {
        _context = Android.App.Application.Context 
            ?? global::Android.App.Application.Context;
    }

    public async Task<string?> CheckUpdateAsync()
    {
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("CatClawMusic-Android");
            client.Timeout = TimeSpan.FromSeconds(10);

            var response = await client.GetAsync(GithubApiUrl);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("tag_name", out var tagProp))
                return null;

            var latestTag = tagProp.GetString() ?? "";
            if (string.IsNullOrWhiteSpace(latestTag))
                return null;

            // tag_name 可能是 "v1.5.3" 或 "1.5.3"，统一去掉 v 前缀
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
        var prefs = _context.GetSharedPreferences(PrefName, FileCreationMode.Private)!;
        prefs.Edit()!.PutString(KeyIgnoredVersion, version)!.Commit();
        // 清除待提示标记
        prefs.Edit()!.PutString(KeyPendingVersion, "")?.Commit();
    }

    public string GetIgnoredVersion()
    {
        var prefs = _context.GetSharedPreferences(PrefName, FileCreationMode.Private)!;
        return prefs.GetString(KeyIgnoredVersion, "") ?? "";
    }

    /// <summary>设置有待提示的版本（设置页红点依赖此标记）</summary>
    public void SetPendingVersion(string version)
    {
        var prefs = _context.GetSharedPreferences(PrefName, FileCreationMode.Private)!;
        prefs.Edit()!.PutString(KeyPendingVersion, version)!.Commit();
    }

    /// <summary>获取待提示的版本（设置页读此标记显示红点）</summary>
    public string GetPendingVersion()
    {
        var prefs = _context.GetSharedPreferences(PrefName, FileCreationMode.Private)!;
        return prefs.GetString(KeyPendingVersion, "") ?? "";
    }

    // ── 版本号工具 ─────────────────────────────────────────────

    private static string GetCurrentVersion()
    {
        try
        {
            var ctx = Android.App.Application.Context;
            var pInfo = ctx?.PackageManager?.GetPackageInfo(ctx.PackageName ?? "", 0);
            return pInfo?.VersionName?.TrimStart('v') ?? "0.0.0";
        }
        catch
        {
            return "0.0.0";
        }
    }

    // 比较版本号，a > b 返回 1，a == b 返回 0，a < b 返回 -1
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
