using Android.Content;
using Android.Views;
using Android.Widget;
using Google.Android.Material.Dialog;
using AlertDialog = AndroidX.AppCompat.App.AlertDialog;

namespace CatClawMusic.UI.Services;

/// <summary>
/// 基于 MaterialAlertDialog 的水平进度对话框，替代已过时的 Android.App.ProgressDialog。
/// 使用方式：using var dlg = new MaterialProgressDialog(context, "标题", "消息"); dlg.Show(); ... dlg.Update(status, percent); dlg.Dismiss();
/// </summary>
public sealed class MaterialProgressDialog : IDisposable
{
    private readonly AlertDialog _dialog;
    private readonly ProgressBar _progressBar;
    private readonly TextView _messageView;

    /// <summary>创建进度对话框</summary>
    /// <param name="context">Context</param>
    /// <param name="title">标题</param>
    /// <param name="message">初始消息</param>
    /// <param name="max">进度最大值（默认 100）</param>
    /// <param name="cancelable">是否可取消（默认 false）</param>
    public MaterialProgressDialog(Context context, string title, string message, int max = 100, bool cancelable = false)
    {
        var container = new LinearLayout(context)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        int pad = (int)(24 * context.Resources?.DisplayMetrics?.Density!);
        container.SetPadding(pad, pad, pad, pad);

        _messageView = new TextView(context) { Text = message };
        _messageView.SetTextColor(new Android.Graphics.Color(unchecked((int)0xDE000000)));
        _messageView.SetTextSize(Android.Util.ComplexUnitType.Sp, 14);
        container.AddView(_messageView);

        var msgLp = (LinearLayout.LayoutParams)_messageView.LayoutParameters!;
        msgLp.BottomMargin = pad;
        _messageView.LayoutParameters = msgLp;

        _progressBar = new ProgressBar(context, null, Android.Resource.Attribute.ProgressBarStyleHorizontal)
        {
            Max = max,
            Progress = 0,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        container.AddView(_progressBar);

        _dialog = new MaterialAlertDialogBuilder(context)
            .SetTitle(title)
            .SetView(container)
            .SetCancelable(cancelable)
            .Create();
    }

    /// <summary>显示对话框</summary>
    public void Show() => _dialog.Show();

    /// <summary>更新进度和消息（在 UI 线程调用）</summary>
    /// <param name="message">消息文本</param>
    /// <param name="percent">进度百分比（0~Max）</param>
    public void Update(string message, int percent)
    {
        _messageView.Text = message;
        _progressBar.Progress = percent;
    }

    /// <summary>关闭对话框</summary>
    public void Dismiss()
    {
        if (_dialog.IsShowing)
            _dialog.Dismiss();
    }

    /// <summary>释放资源</summary>
    public void Dispose()
    {
        Dismiss();
        _dialog.Dispose();
    }
}
