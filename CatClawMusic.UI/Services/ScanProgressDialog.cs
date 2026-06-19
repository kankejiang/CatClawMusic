using Android.App;
using Android.OS;
using Android.Views;
using Android.Widget;

namespace CatClawMusic.UI.Services;

public class ScanProgressDialog : Dialog
{
    private TextView _tvTitle = null!;
    private TextView _tvStatus = null!;
    private ProgressBar _progressBar = null!;
    private TextView _tvDetail = null!;
    private TextView _tvCount = null!;
    private ImageButton _btnClose = null!;

    private bool _isCompleted;

    public ScanProgressDialog(Activity activity, string title = "正在扫描") : base(activity)
    {
        _title = title;
    }

    private readonly string _title;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        RequestWindowFeature((int)WindowFeatures.NoTitle);
        SetContentView(Resource.Layout.dialog_scan_progress);

        Window?.SetLayout(
            (int)(Context.Resources!.DisplayMetrics!.WidthPixels * 0.88),
            ViewGroup.LayoutParams.WrapContent);
        Window?.SetBackgroundDrawableResource(Android.Resource.Drawable.DialogFrame);
        Window?.SetGravity(GravityFlags.Center);

        SetCancelable(false);
        SetCanceledOnTouchOutside(false);

        _tvTitle = FindViewById<TextView>(Resource.Id.tv_scan_title)!;
        _tvStatus = FindViewById<TextView>(Resource.Id.tv_scan_status)!;
        _progressBar = FindViewById<ProgressBar>(Resource.Id.progress_bar)!;
        _tvDetail = FindViewById<TextView>(Resource.Id.tv_scan_detail)!;
        _tvCount = FindViewById<TextView>(Resource.Id.tv_scan_count)!;
        _btnClose = FindViewById<ImageButton>(Resource.Id.btn_close)!;

        _tvTitle.Text = _title;
        _btnClose.Click += (_, _) => Dismiss();
    }

    public void UpdateProgress(int progress, string status)
    {
        if (_tvStatus == null) return;
        _tvStatus.Text = status;
        _progressBar.Progress = progress;
        _tvDetail.Text = $"{progress}%";
    }

    public void UpdateCount(int count)
    {
        if (_tvCount == null) return;
        _tvCount.Text = $"已发现 {count} 首歌曲";
    }

    public void SetCountText(string? text)
    {
        if (_tvCount == null) return;
        if (string.IsNullOrEmpty(text))
            _tvCount.Visibility = ViewStates.Gone;
        else
        {
            _tvCount.Visibility = ViewStates.Visible;
            _tvCount.Text = text;
        }
    }

    public void SetCompleted(string title, string message)
    {
        if (_tvStatus == null) return;
        _isCompleted = true;
        _tvTitle.Text = title;
        _tvStatus.Text = message;
        _progressBar.Progress = 100;
        _tvDetail.Text = "100%";
        _btnClose.Enabled = true;
        _btnClose.Alpha = 1.0f;
        SetCancelable(true);
        SetCanceledOnTouchOutside(true);
    }

    public void SetCompleted(string message)
        => SetCompleted("扫描完成", message);

    public void SetError(string title, string message)
    {
        if (_tvStatus == null) return;
        _isCompleted = true;
        _tvTitle.Text = title;
        _tvStatus.Text = message;
        _btnClose.Enabled = true;
        _btnClose.Alpha = 1.0f;
        SetCancelable(true);
        SetCanceledOnTouchOutside(true);
    }

    public void SetError(string message)
        => SetError("扫描失败", message);

    public override void OnBackPressed()
    {
        if (!_isCompleted) return;
        base.OnBackPressed();
    }
}
