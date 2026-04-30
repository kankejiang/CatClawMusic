using Android.App;
using Android.Content;
using Android.Provider;

namespace CatClawMusic.UI.Platforms.Android;

/// <summary>
/// Android 文件夹选择器——使用 SAF（Storage Access Framework）
/// 返回并保存 content:// URI，配合 SafeContentScanner 使用
/// </summary>
public static class FolderPicker
{
    private const int PickFolderCode = 9001;
    private const string PrefKey = "music_folder_uri";
    private const string PrefKeyList = "music_folder_uris";
    private static TaskCompletionSource<string?>? _tcs;

    /// <summary>打开系统文件夹选择器，返回 content:// URI（追加到已有列表）</summary>
    public static async Task<string?> PickFolderAsync()
    {
        var activity = MainActivity.Instance;
        if (activity == null) return null;

        _tcs = new TaskCompletionSource<string?>();

        var intent = new Intent(Intent.ActionOpenDocumentTree);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission
                      | ActivityFlags.GrantWriteUriPermission
                      | ActivityFlags.GrantPersistableUriPermission
                      | ActivityFlags.GrantPrefixUriPermission);
        activity.StartActivityForResult(intent, PickFolderCode);

        return await _tcs.Task;
    }

    /// <summary>获取之前保存的所有文件夹 URI（兼容旧版单文件夹）</summary>
    public static List<string> GetSavedFolderUris()
    {
        var ctx = global::Android.App.Application.Context;
        var prefs = ctx.GetSharedPreferences("catclaw_prefs", FileCreationMode.Private)!;

        // 新版：多文件夹列表
        var list = prefs.GetString(PrefKeyList, null);
        if (!string.IsNullOrEmpty(list))
        {
            return list.Split('|', StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        // 兼容旧版单文件夹
        var single = prefs.GetString(PrefKey, null);
        if (!string.IsNullOrEmpty(single))
            return new List<string> { single };

        return new List<string>();
    }

    /// <summary>获取之前保存的 content URI（可能为 null）—— 兼容方法</summary>
    public static string? GetSavedFolderUri()
    {
        var uris = GetSavedFolderUris();
        return uris.Count > 0 ? uris[0] : null;
    }

    public static void RemoveSavedFolder(string uri)
    {
        var ctx = global::Android.App.Application.Context;
        var prefs = ctx.GetSharedPreferences("catclaw_prefs", FileCreationMode.Private)!;
        var list = prefs.GetString(PrefKeyList, null);
        if (string.IsNullOrEmpty(list)) return;
        var folders = list.Split('|').Where(f => f != uri).ToList();
        prefs.Edit()!.PutString(PrefKeyList, string.Join("|", folders))!.Apply();
    }

    /// <summary>清空所有文件夹</summary>
    public static void ClearFolders()
    {
        var ctx = global::Android.App.Application.Context;
        var prefs = ctx.GetSharedPreferences("catclaw_prefs", FileCreationMode.Private)!;
        prefs.Edit()!.Remove(PrefKey)!.Remove(PrefKeyList)!.Commit();
    }

    /// <summary>MainActivity.OnActivityResult 中调用</summary>
    public static bool HandleResult(int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode != PickFolderCode) return false;

        if (resultCode == Result.Ok && data?.Data != null)
        {
            var uri = data.Data;

            // 持久化读写访问权限
            var ctx = global::Android.App.Application.Context;
            try
            {
                var takeFlags = ActivityFlags.GrantReadUriPermission
                              | ActivityFlags.GrantWriteUriPermission;
                ctx.ContentResolver!.TakePersistableUriPermission(uri, takeFlags);

                // 追加新 URI 到已有列表（去重）
                var uris = GetSavedFolderUris();
                var uriStr = uri.ToString();
                if (!uris.Contains(uriStr))
                    uris.Add(uriStr);

                // 保存为管道分隔列表
                var prefs = ctx.GetSharedPreferences("catclaw_prefs", FileCreationMode.Private)!;
                prefs.Edit()!
                    .PutString(PrefKeyList, string.Join("|", uris))
                    !.PutString(PrefKey, uriStr) // 兼容
                    !.Commit();

                System.Diagnostics.Debug.WriteLine($"[CatClaw] SAF folder added: {uri} (total: {uris.Count})");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CatClaw] SAF persist failed: {ex.Message}");
            }

            _tcs?.TrySetResult(uri.ToString());
        }
        else
        {
            _tcs?.TrySetResult(null);
        }

        return true;
    }
}
