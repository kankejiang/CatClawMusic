using Android.App;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.UI.Services;

public class DialogService : IDialogService
{
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
