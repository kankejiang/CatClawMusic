using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;

namespace CatClawMusic.UI.Platforms.Android;

/// <summary>
/// Android 文件夹选择器——使用 SAF 系统文件管理器
/// </summary>
public static class FolderPicker
{
    private const int PickFolderCode = 9001;
    private static TaskCompletionSource<string?>? _tcs;

    /// <summary>打开系统文件夹选择器</summary>
    public static async Task<string?> PickFolderAsync()
    {
        var activity = MainActivity.Instance;
        if (activity == null) return null;

        _tcs = new TaskCompletionSource<string?>();

        var intent = new Intent(Intent.ActionOpenDocumentTree);
        activity.StartActivityForResult(intent, PickFolderCode);

        return await _tcs.Task;
    }

    /// <summary>在 MainActivity.OnActivityResult 中调用</summary>
    public static bool HandleResult(int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode != PickFolderCode) return false;

        if (resultCode == Result.Ok && data?.Data != null)
        {
            var uri = data.Data;
            var path = ResolvePath(uri);

            // 持久化访问权限
            var ctx = global::Android.App.Application.Context;
            try
            {
                var flags = data.Flags & ActivityFlags.GrantReadUriPermission;
                if (flags != 0)
                    ctx.ContentResolver?.TakePersistableUriPermission(uri, flags);
            }
            catch { }

            _tcs?.TrySetResult(path);
        }
        else
        {
            _tcs?.TrySetResult(null);
        }

        return true;
    }

    private static string? ResolvePath(global::Android.Net.Uri uri)
    {
        try
        {
            if ("com.android.externalstorage.documents".Equals(uri.Authority))
            {
                var docId = DocumentsContract.GetDocumentId(uri);
                var parts = docId.Split(':');
                var type = parts[0];
                var subPath = parts.Length > 1 ? parts[1] : "";
                if ("primary".Equals(type, StringComparison.OrdinalIgnoreCase))
                    return $"/storage/emulated/0/{subPath}";
                else if (type.Length > 0)
                    return $"/storage/{type}/{subPath}";
            }
            // 返回 content URI 作为回退
            return uri.ToString();
        }
        catch
        {
            return uri.ToString();
        }
    }
}
