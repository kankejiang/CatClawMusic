using Android.App;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.UI.Services;

/// <summary>Android 对话框服务，提供提示和确认对话框</summary>
public class DialogService : IDialogService
{
    /// <summary>显示提示对话框</summary>
    public Task ShowAlertAsync(string title, string message, string buttonText = "确定")
    {
        var tcs = new TaskCompletionSource<bool>();
        var activity = MainActivity.Instance;
        if (activity == null) { tcs.SetResult(false); return tcs.Task; }

        activity.RunOnUiThread(() =>
        {
            new AlertDialog.Builder(activity)
                .SetTitle(title)
                ?.SetMessage(message)
                ?.SetPositiveButton(buttonText, (s, e) => tcs.TrySetResult(true))
                ?.SetCancelable(false)
                ?.Show();
        });
        return tcs.Task;
    }

    /// <summary>显示确认对话框，返回用户选择结果</summary>
    public Task<bool> ShowConfirmAsync(string title, string message, string acceptText = "确定", string cancelText = "取消")
    {
        var tcs = new TaskCompletionSource<bool>();
        var activity = MainActivity.Instance;
        if (activity == null) { tcs.SetResult(false); return tcs.Task; }

        activity.RunOnUiThread(() =>
        {
            new AlertDialog.Builder(activity)
                .SetTitle(title)
                ?.SetMessage(message)
                ?.SetPositiveButton(acceptText, (s, e) => tcs.TrySetResult(true))
                ?.SetNegativeButton(cancelText, (s, e) => tcs.TrySetResult(false))
                ?.SetCancelable(true)
                ?.Show();
        });
        return tcs.Task;
    }
}
