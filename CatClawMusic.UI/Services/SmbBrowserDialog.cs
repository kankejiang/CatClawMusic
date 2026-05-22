using Android.App;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Data;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Services;

public class SmbBrowserDialog : Dialog
{
    private readonly ConnectionProfile _profile;
    private readonly SmbService _smbService;

    private RecyclerView _recyclerView = null!;
    private TextView _tvCurrentPath = null!;
    private ProgressBar _progressBar = null!;
    private TextView _tvEmpty = null!;
    private TextView _tvError = null!;
    private Button _btnSelect = null!;
    private ImageButton _btnClose = null!;

    private readonly List<RemoteFile> _items = new();
    private FileListAdapter _adapter = null!;
    private readonly Stack<string> _pathStack = new();
    private string _currentPath = "\\";
    private bool _isInitialized = false;
    private bool _isLoading = false;

    public string? SelectedPath { get; private set; }

    public SmbBrowserDialog(Activity activity, ConnectionProfile profile)
        : base(activity)
    {
        _profile = profile;
        _smbService = (MainApplication.Services.GetService(typeof(SmbService)) as SmbService)
            ?? (MainApplication.Services.GetServices<INetworkFileService>()
                .FirstOrDefault(s => s is SmbService) as SmbService)
            ?? new SmbService();
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        RequestWindowFeature((int)WindowFeatures.NoTitle);

        try
        {
            SetContentView(Resource.Layout.dialog_webdav_browser);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SMB Browser] SetContentView 失败: {ex}");
            Dismiss();
            return;
        }

        Window?.SetLayout(
            (int)(Context.Resources!.DisplayMetrics!.WidthPixels * 0.92),
            ViewGroup.LayoutParams.WrapContent);
        Window?.SetBackgroundDrawableResource(Android.Resource.Drawable.DialogFrame);
        Window?.SetGravity(GravityFlags.Center);

        InitViews();
        InitRecyclerView();

        _btnClose.Click += (s, e) => Dismiss();
        _btnSelect.Click += (s, e) =>
        {
            SelectedPath = _currentPath;
            Dismiss();
        };

        InitializeAndLoadAsync().ContinueWith(t =>
        {
            if (t.IsFaulted && t.Exception != null)
            {
                System.Diagnostics.Debug.WriteLine($"[SMB Browser] 初始化异常: {t.Exception}");
                try
                {
                    var owner = OwnerActivity;
                    if (owner != null)
                    {
                        owner.RunOnUiThread(() =>
                        {
                            try { ShowError($"初始化失败: {t.Exception.InnerException?.Message ?? t.Exception.Message}"); }
                            catch { }
                        });
                    }
                }
                catch { }
            }
        });
    }

    private async Task InitializeAndLoadAsync()
    {
        ShowLoading("正在连接...");

        try
        {
            _smbService.Configure(_profile);
            _isInitialized = true;
            await LoadDirectoryAsync("");
        }
        catch (Java.Lang.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SMB Browser] Java异常: {ex.Message}");
            ShowError($"连接异常: {ex.Message}");
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SMB Browser] 初始化异常: {ex}");
            ShowError($"初始化失败: {ex.Message}");
        }
    }

    private void InitViews()
    {
        _recyclerView = FindViewById<RecyclerView>(Resource.Id.recycler_view) ?? throw new InvalidOperationException("找不到 recycler_view");
        _tvCurrentPath = FindViewById<TextView>(Resource.Id.tv_current_path) ?? throw new InvalidOperationException("找不到 tv_current_path");
        _progressBar = FindViewById<ProgressBar>(Resource.Id.progress_bar) ?? throw new InvalidOperationException("找不到 progress_bar");
        _tvEmpty = FindViewById<TextView>(Resource.Id.tv_empty) ?? throw new InvalidOperationException("找不到 tv_empty");
        _tvError = FindViewById<TextView>(Resource.Id.tv_error) ?? throw new InvalidOperationException("找不到 tv_error");
        _btnSelect = FindViewById<Button>(Resource.Id.btn_select) ?? throw new InvalidOperationException("找不到 btn_select");
        _btnClose = FindViewById<ImageButton>(Resource.Id.btn_close) ?? throw new InvalidOperationException("找不到 btn_close");
    }

    private void InitRecyclerView()
    {
        _adapter = new FileListAdapter(_items, OnItemClick);
        _recyclerView.SetLayoutManager(new LinearLayoutManager(Context));
        _recyclerView.SetAdapter(_adapter);
    }

    private void OnItemClick(int position)
    {
        if (_isLoading) return;
        if (position < 0 || position >= _items.Count) return;

        var file = _items[position];
        if (!file.IsDirectory) return;

        _pathStack.Push(_currentPath);
        _ = LoadDirectoryAsync(file.Path);
    }

    private async Task LoadDirectoryAsync(string path)
    {
        if (!_isInitialized) { ShowError("连接未初始化"); return; }
        if (_isLoading) return;
        _isLoading = true;

        _currentPath = path;
        ShowLoading($"📁 {path}");

        try
        {
            var files = await _smbService.ListFilesAsync(path);

            var dirs = files.Where(f => f.IsDirectory).OrderBy(f => f.Name).ToList();

            _items.Clear();
            _items.AddRange(dirs);
            _adapter.NotifyDataSetChanged();

            if (dirs.Count == 0)
            {
                _tvEmpty.Visibility = ViewStates.Visible;
                _recyclerView.Visibility = ViewStates.Gone;
            }
            else
            {
                _recyclerView.Visibility = ViewStates.Visible;
                _tvEmpty.Visibility = ViewStates.Gone;
            }
        }
        catch (Java.Lang.Exception ex)
        {
            ShowError($"加载失败: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[SMB Browser] Java异常: {ex.Message}");
        }
        catch (System.Exception ex)
        {
            ShowError($"加载失败: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[SMB Browser] 加载目录异常: {ex}");
        }
        finally
        {
            _progressBar.Visibility = ViewStates.Gone;
            _isLoading = false;
        }
    }

    private void ShowLoading(string message)
    {
        _tvCurrentPath.Text = message;
        _progressBar.Visibility = ViewStates.Visible;
        _tvEmpty.Visibility = ViewStates.Gone;
        _tvError.Visibility = ViewStates.Gone;
        _recyclerView.Visibility = ViewStates.Gone;
    }

    private void ShowError(string message)
    {
        _tvError.Visibility = ViewStates.Visible;
        _tvError.Text = message;
        _progressBar.Visibility = ViewStates.Gone;
        _tvEmpty.Visibility = ViewStates.Gone;
        _recyclerView.Visibility = ViewStates.Gone;
    }

    public override void OnBackPressed()
    {
        if (_isLoading) return;
        if (_pathStack.Count > 0)
        {
            var parentPath = _pathStack.Pop();
            _ = LoadDirectoryAsync(parentPath);
        }
        else
        {
            base.OnBackPressed();
        }
    }

    private class FileListAdapter : RecyclerView.Adapter
    {
        private readonly IList<RemoteFile> _files;
        private readonly Action<int> _onItemClick;

        public FileListAdapter(IList<RemoteFile> files, Action<int> onItemClick)
        {
            _files = files;
            _onItemClick = onItemClick;
        }

        public override int ItemCount => _files.Count;

        public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
        {
            var file = _files[position];
            var vh = (FileViewHolder)holder;
            vh.Name.Text = file.Name;
            vh.Icon.SetColorFilter(Color.ParseColor("#9B7ED8"), PorterDuff.Mode.SrcIn);
            vh.Position = position;
        }

        public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
        {
            var view = LayoutInflater.From(parent.Context)!
                .Inflate(Resource.Layout.item_webdav_file, parent, false);
            return new FileViewHolder(view, _onItemClick);
        }
    }

    private class FileViewHolder : RecyclerView.ViewHolder
    {
        public TextView Name { get; }
        public ImageView Icon { get; }
        public int Position { get; set; }

        public FileViewHolder(View view, Action<int> onItemClick) : base(view)
        {
            Name = view.FindViewById<TextView>(Resource.Id.tv_name)!;
            Icon = view.FindViewById<ImageView>(Resource.Id.iv_icon)!;
            ItemView.Click += (s, e) => onItemClick(Position);
        }
    }
}
