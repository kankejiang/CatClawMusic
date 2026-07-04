using Android.App;
using Android.Content;
using Android.Provider;
using AUri = Android.Net.Uri;

namespace CatClawMusic.Maui.Platforms.Android;

/// <summary>
/// Android SAF（Storage Access Framework）文件夹选择器，
/// 负责打开系统文件夹选择器、持久化授权、查询/移除/验证已保存的文件夹 URI 列表。
/// </summary>
public static class FolderPicker
{
    /// <summary>文件夹选择请求码，用于 OnActivityResult 中区分来源</summary>
    private const int PickFolderCode = 9001;
    /// <summary>SharedPreferences 中保存单个文件夹 URI 的键（兼容旧版本）</summary>
    private const string PrefKey = "music_folder_uri";
    /// <summary>SharedPreferences 中保存多个文件夹 URI 列表的键（以 | 分隔）</summary>
    private const string PrefKeyList = "music_folder_uris";
    /// <summary>等待文件夹选择结果的 TaskCompletionSource</summary>
    private static TaskCompletionSource<string?>? _tcs;

    /// <summary>打开系统文件夹选择器，返回所选文件夹的 content:// URI 字符串；用户取消时返回 null</summary>
    /// <returns>所选文件夹的 content:// URI 字符串，取消时为 null</returns>
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

    /// <summary>获取已保存的所有文件夹 URI 列表，优先读取列表键，缺失时回退到单个键（兼容旧版本）</summary>
    /// <returns>已保存的文件夹 URI 字符串列表</returns>
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

    /// <summary>获取第一个已保存的文件夹 URI（兼容旧版本的单文件夹场景）</summary>
    /// <returns>第一个已保存的文件夹 URI，无则返回 null</returns>
    public static string? GetSavedFolderUri()
    {
        var uris = GetSavedFolderUris();
        return uris.Count > 0 ? uris[0] : null;
    }

    /// <summary>移除指定的已保存文件夹：从列表中删除并释放其持久化 URI 权限</summary>
    /// <param name="uri">要移除的文件夹 URI 字符串</param>
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

    /// <summary>清空所有已保存的文件夹 URI（不释放持久化权限）</summary>
    public static void ClearFolders()
    {
        var ctx = global::Android.App.Application.Context;
        var prefs = ctx.GetSharedPreferences("catclaw_prefs", FileCreationMode.Private)!;
        prefs.Edit()!.Remove(PrefKey)!.Remove(PrefKeyList)!.Commit();
    }

    /// <summary>验证已保存的文件夹 URI 权限是否仍有效，移除已失效的 URI，并返回仍有效的数量</summary>
    /// <returns>仍有效的文件夹 URI 数量</returns>
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

    /// <summary>处理 MainActivity.OnActivityResult 中的文件夹选择结果：申请持久化权限、保存 URI 并完成 TaskCompletionSource</summary>
    /// <param name="requestCode">请求码</param>
    /// <param name="resultCode">结果码</param>
    /// <param name="data">返回的 Intent 数据</param>
    /// <returns>是否由本选择器处理</returns>
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
