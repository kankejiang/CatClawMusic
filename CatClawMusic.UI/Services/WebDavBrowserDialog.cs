using Android.App;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Services;

/// <summary>
/// WebDAV 目录浏览对话框——让用户可视化选择远程路径
/// </summary>
public class WebDavBrowserDialog : Dialog
{
    private readonly ConnectionProfile _profile;
    private readonly INetworkFileService _webDav;

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
    private string _currentPath = "/";
    private bool _isInitialized = false;
    private bool _isLoading = false;

    /// <summary>用户最终选择的路径</summary>
    public string? SelectedPath { get; private set; }

    public WebDavBrowserDialog(Activity activity, ConnectionProfile profile)
        : base(activity)
    {
        _profile = profile;
        _webDav = MainApplication.Services.GetRequiredService<INetworkFileService>();
        System.Diagnostics.Debug.WriteLine($"[WebDAV Browser] 创建对话框: {profile.Host}:{profile.Port}");
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        RequestWindowFeature((int)WindowFeatures.NoTitle);
        SetContentView(Resource.Layout.dialog_webdav_browser);

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

        System.Diagnostics.Debug.WriteLine("[WebDAV Browser] 开始初始化连接...");
        // 在后台线程初始化连接并加载根目录
        _ = InitializeAndLoadAsync();
    }

    private async Task InitializeAndLoadAsync()
    {
        ShowLoading("正在连接...");
        System.Diagnostics.Debug.WriteLine("[WebDAV Browser] InitializeAndLoadAsync 开始");

        try
        {
            // 使用 async lambda 确保正确 await
            System.Diagnostics.Debug.WriteLine("[WebDAV Browser] 调用 TestConnectionAsync...");
            var result = await Task.Run(async () =>
            {
                System.Diagnostics.Debug.WriteLine("[WebDAV Browser] 在后台线程执行 TestConnectionAsync");
                return await _webDav.TestConnectionAsync(_profile);
            });

            System.Diagnostics.Debug.WriteLine($"[WebDAV Browser] 连接结果: Success={result.Success}, Message={result.Message}");

            if (!result.Success)
            {
                ShowError($"连接失败: {result.Message}");
                return;
            }

            _isInitialized = true;
            await LoadDirectoryAsync("/");
        }
        catch (Exception ex)
        {
            ShowError($"初始化失败: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[WebDAV Browser] 初始化异常: {ex}");
        }
    }

    private void InitViews()
    {
        _recyclerView = FindViewById<RecyclerView>(Resource.Id.recycler_view)!;
        _tvCurrentPath = FindViewById<TextView>(Resource.Id.tv_current_path)!;
        _progressBar = FindViewById<ProgressBar>(Resource.Id.progress_bar)!;
        _tvEmpty = FindViewById<TextView>(Resource.Id.tv_empty)!;
        _tvError = FindViewById<TextView>(Resource.Id.tv_error)!;
        _btnSelect = FindViewById<Button>(Resource.Id.btn_select)!;
        _btnClose = FindViewById<ImageButton>(Resource.Id.btn_close)!;
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

        // URL 解码路径
        var clickedPath = System.Net.WebUtility.UrlDecode(file.Path);
        // 从完整 URL 中提取路径部分
        var path = "/";
        try
        {
            var uri = new Uri(clickedPath);
            path = uri.AbsolutePath;
        }
        catch
        {
            path = clickedPath;
        }

        _pathStack.Push(_currentPath);
        _ = LoadDirectoryAsync(path);
    }

    private async Task LoadDirectoryAsync(string path)
    {
        if (!_isInitialized) { ShowError("连接未初始化"); return; }
        if (_isLoading) return;
        _isLoading = true;

        _currentPath = path;
        ShowLoading($"📁 {path}");
        System.Diagnostics.Debug.WriteLine($"[WebDAV Browser] 加载目录: {path}");

        try
        {
            // 使用 async lambda 确保正确 await
            var files = await Task.Run(async () =>
            {
                System.Diagnostics.Debug.WriteLine($"[WebDAV Browser] 在后台线程执行 ListFilesAsync: {path}");
                return await _webDav.ListFilesAsync(path);
            });

            System.Diagnostics.Debug.WriteLine($"[WebDAV Browser] 获取到 {files.Count} 个文件/目录");

            // 只显示目录，按名称排序
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
        catch (Exception ex)
        {
            ShowError($"加载失败: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[WebDAV Browser] 加载目录异常: {ex}");
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

    /// <summary>
    /// 目录列表适配器——使用 position 索引避免闭包问题
    /// </summary>
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
