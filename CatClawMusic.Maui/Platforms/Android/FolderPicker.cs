using Android.App;
using Android.Content;
using Android.Provider;
using AUri = Android.Net.Uri;

namespace CatClawMusic.Maui.Platforms.Android;

/// <summary>
/// Android SAF 文件夹选择器
/// </summary>
public static class FolderPicker
{
    private const int PickFolderCode = 9001;
    private const string PrefKey = "music_folder_uri";
    private const string PrefKeyList = "music_folder_uris";
    private static TaskCompletionSource<string?>? _tcs;

    /// <summary>打开系统文件夹选择器，返回 content:// URI</summary>
    public static async Task<string?> PickFolderAsync()
    {
        var activity = Platform.CurrentActivity;
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

    /// <summary>获取已保存的所有文件夹 URI 列表</summary>
    public static List<string> GetSavedFolderUris()
    {
        var ctx = global::Android.App.Application.Context;
        var prefs = ctx.GetSharedPreferences("catclaw_prefs", FileCreationMode.Private)!;

        var list = prefs.GetString(PrefKeyList, null);
        if (!string.IsNullOrEmpty(list))
            return list.Split('|', StringSplitOptions.RemoveEmptyEntries).ToList();

        var single = prefs.GetString(PrefKey, null);
        if (!string.IsNullOrEmpty(single))
            return new List<string> { single };

        return new List<string>();
    }

    /// <summary>获取第一个已保存的 URI（兼容）</summary>
    public static string? GetSavedFolderUri()
    {
        var uris = GetSavedFolderUris();
        return uris.Count > 0 ? uris[0] : null;
    }

    /// <summary>移除指定的已保存文件夹</summary>
    public static void RemoveSavedFolder(string uri)
    {
        var ctx = global::Android.App.Application.Context;
        var prefs = ctx.GetSharedPreferences("catclaw_prefs", FileCreationMode.Private)!;
        var list = prefs.GetString(PrefKeyList, null);
        if (string.IsNullOrEmpty(list)) return;
        var folders = list.Split('|', StringSplitOptions.RemoveEmptyEntries).Where(f => f != uri).ToList();

        var editor = prefs.Edit()!;
        if (folders.Count > 0)
        {
            editor.PutString(PrefKeyList, string.Join("|", folders));
            editor.PutString(PrefKey, folders[0]);
        }
        else
        {
            editor.Remove(PrefKeyList);
            editor.Remove(PrefKey);
        }
        editor.Commit();

        try
        {
            var treeUri = AUri.Parse(uri);
            if (treeUri != null)
            {
                var takeFlags = ActivityFlags.GrantReadUriPermission
                              | ActivityFlags.GrantWriteUriPermission;
                ctx.ContentResolver!.ReleasePersistableUriPermission(treeUri, takeFlags);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CatClaw] 释放 URI 权限失败: {ex.Message}");
        }
    }

    /// <summary>清空所有文件夹</summary>
    public static void ClearFolders()
    {
        var ctx = global::Android.App.Application.Context;
        var prefs = ctx.GetSharedPreferences("catclaw_prefs", FileCreationMode.Private)!;
        prefs.Edit()!.Remove(PrefKey)!.Remove(PrefKeyList)!.Commit();
    }

    /// <summary>验证已保存的文件夹 URI 权限是否仍有效</summary>
    public static int ValidateSavedFolders()
    {
        var uris = GetSavedFolderUris();
        if (uris.Count == 0) return 0;

        var ctx = global::Android.App.Application.Context;
        var valid = new List<string>();

        foreach (var uriStr in uris)
        {
            try
            {
                var treeUri = AUri.Parse(uriStr);
                if (treeUri == null) continue;
                var docId = DocumentsContract.GetTreeDocumentId(treeUri);
                var childrenUri = DocumentsContract.BuildChildDocumentsUriUsingTree(treeUri, docId);
                using var cursor = ctx.ContentResolver!.Query(childrenUri, null, null, null, null);
                if (cursor != null)
                {
                    valid.Add(uriStr);
                    cursor.Close();
                }
            }
            catch (Java.Lang.SecurityException)
            {
                System.Diagnostics.Debug.WriteLine($"[CatClaw] SAF 权限已丢失: {uriStr}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CatClaw] SAF 验证异常: {ex.Message}");
            }
        }

        if (valid.Count != uris.Count)
        {
            var prefs = ctx.GetSharedPreferences("catclaw_prefs", FileCreationMode.Private)!;
            if (valid.Count > 0)
            {
                prefs.Edit()!
                    .PutString(PrefKeyList, string.Join("|", valid))
                    .PutString(PrefKey, valid[0])
                    .Apply();
            }
            else
            {
                prefs.Edit()!.Remove(PrefKey)!.Remove(PrefKeyList)!.Apply();
            }
        }

        return valid.Count;
    }

    /// <summary>MainActivity.OnActivityResult 中调用</summary>
    public static bool HandleResult(int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode != PickFolderCode) return false;

        if (resultCode == Result.Ok && data?.Data != null)
        {
            var uri = data.Data;
            var ctx = global::Android.App.Application.Context;
            try
            {
                var takeFlags = ActivityFlags.GrantReadUriPermission
                              | ActivityFlags.GrantWriteUriPermission;
                ctx.ContentResolver!.TakePersistableUriPermission(uri, takeFlags);

                var uris = GetSavedFolderUris();
                var uriStr = uri.ToString();
                if (!uris.Contains(uriStr))
                    uris.Add(uriStr);

                var prefs = ctx.GetSharedPreferences("catclaw_prefs", FileCreationMode.Private)!;
                prefs.Edit()!
                    .PutString(PrefKeyList, string.Join("|", uris))
                    .PutString(PrefKey, uriStr)
                    .Commit();
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
