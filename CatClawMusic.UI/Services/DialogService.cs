using CatClawMusic.Core.Interfaces;
using CatClawMusic.UI.Helpers;

namespace CatClawMusic.UI.Services;

/// <summary>Android 对话框服务，使用毛玻璃风格弹窗</summary>
public class DialogService : IDialogService
{
    /// <summary>显示提示对话框（毛玻璃风格）</summary>
    public Task ShowAlertAsync(string title, string message, string buttonText = "确定")
    {
        var tcs = new TaskCompletionSource<bool>();
        var activity = MainActivity.Instance;
        if (activity == null) { tcs.SetResult(false); return tcs.Task; }

        activity.RunOnUiThread(() =>
        {
            new GlassDialog(activity)
                .SetTitle(title)
                .AddMessage(message)
                .AddPositiveButton(buttonText, (_) => tcs.TrySetResult(true))
                .Show();
        });
        return tcs.Task;
    }

    /// <summary>显示确认对话框，返回用户选择结果（毛玻璃风格）</summary>
    public Task<bool> ShowConfirmAsync(string title, string message, string acceptText = "确定", string cancelText = "取消")
    {
        var tcs = new TaskCompletionSource<bool>();
        var activity = MainActivity.Instance;
        if (activity == null) { tcs.SetResult(false); return tcs.Task; }

        activity.RunOnUiThread(() =>
        {
            new GlassDialog(activity)
                .SetTitle(title)
                .AddMessage(message)
                .AddPositiveButton(acceptText, (_) => tcs.TrySetResult(true))
                .AddNegativeButton(cancelText, () => tcs.TrySetResult(false))
                .Show();
        });
        return tcs.Task;
    }
}
