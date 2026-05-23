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

/// <summary>
/// SMB 目录浏览对话框，支持两步选择：先选共享名，再浏览目录，最终返回所选路径
/// </summary>
public class SmbBrowserDialog : Dialog
{
    private readonly ConnectionProfile _profile;
    private readonly SmbService _smbService;
    private readonly Activity _activity;

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

    // 两步模式：先选共享，再浏览目录
    private bool _isShareMode = true;
    private List<string> _shareNames = new();
    private string _selectedShare = "";

    /// <summary>用户最终选择的路径</summary>
    public string? SelectedPath { get; private set; }
    /// <summary>用户选择的共享名</summary>
    public string SelectedShareName { get; private set; } = "";

    /// <summary>创建 SMB 浏览器对话框</summary>
    /// <param name="activity">宿主 Activity</param>
    /// <param name="profile">SMB 连接配置</param>
    public SmbBrowserDialog(Activity activity, ConnectionProfile profile)
        : base(activity)
    {
        _activity = activity;
        _profile = profile;
        // 使用 DI 单例，与 NetworkMusicService/AudioPlayerService 保持一致
        _smbService = MainApplication.Services.GetServices<INetworkFileService>()
                         .FirstOrDefault(s => s is SmbService) as SmbService
                     ?? new SmbService();
    }

    /// <summary>对话框创建时初始化布局、视图和 RecyclerView，并异步列出共享名</summary>
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

        _ = InitializeAndLoadAsync();
    }

    private async Task InitializeAndLoadAsync()
    {
        System.Diagnostics.Debug.WriteLine("[SMB Browser] InitializeAndLoadAsync 开始");
        // 第一步：先列出服务器上的共享名
        await ListSharesAndShowAsync();
    }

    private async Task ListSharesAndShowAsync()
    {
        _isShareMode = true;
        RunOnUiThread(() =>
        {
            _tvCurrentPath.Text = "选择共享";
            _btnSelect.Visibility = ViewStates.Gone;
            ShowLoading("正在列出共享...");
        });

        (bool success, List<string> shares, string message) = await _smbService.ListSharesAsync(_profile);

        if (!success || shares.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine($"[SMB Browser] 列出共享失败: {message}");
            RunOnUiThread(() => ShowError(message));
            return;
        }

        _shareNames = shares;
        System.Diagnostics.Debug.WriteLine($"[SMB Browser] 列出共享 {shares.Count} 个: {string.Join(", ", shares)}");

        RunOnUiThread(() =>
        {
            _items.Clear();
            foreach (var s in _shareNames)
                _items.Add(new RemoteFile { Name = s, Path = s, IsDirectory = true });
            _adapter.NotifyDataSetChanged();

            _progressBar.Visibility = ViewStates.Gone;
            _recyclerView.Visibility = ViewStates.Visible;
            _tvEmpty.Visibility = ViewStates.Gone;
            _tvError.Visibility = ViewStates.Gone;
        });
    }

    private async Task ConnectAndBrowseShareAsync(string shareName)
    {
        _isShareMode = false;
        _selectedShare = shareName;
        SelectedShareName = shareName;
        _profile.ShareName = shareName;
        _pathStack.Clear();

        RunOnUiThread(() =>
        {
            _tvCurrentPath.Text = $"📁 \\\\{shareName}";
            _btnSelect.Visibility = ViewStates.Visible;
            ShowLoading("正在连接共享...");
        });

        var connected = false;
        string? errorMsg = null;
        await Task.Run(() =>
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[SMB Browser] 连接共享 {shareName}...");
                _smbService.Configure(_profile);
                connected = true;
                System.Diagnostics.Debug.WriteLine("[SMB Browser] 共享连接成功");
            }
            catch (Exception ex)
            {
                errorMsg = ex.Message;
                System.Diagnostics.Debug.WriteLine($"[SMB Browser] Configure 失败: {ex.Message}");
                RunOnUiThread(() => ShowError($"连接失败: {ex.Message}"));
            }
        });

        if (!connected)
        {
            System.Diagnostics.Debug.WriteLine($"[SMB Browser] 共享连接失败，回退: {errorMsg}");
            await ListSharesAndShowAsync();
            return;
        }

        _isInitialized = true;
        await LoadDirectoryInternalAsync("\\");
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
        if (_isLoading)
        {
            System.Diagnostics.Debug.WriteLine("[SMB Browser] OnItemClick 跳过: 加载中");
            return;
        }
        if (position < 0 || position >= _items.Count) return;

        var file = _items[position];

        // 共享选择模式：点击进入该共享浏览目录
        if (_isShareMode)
        {
            System.Diagnostics.Debug.WriteLine($"[SMB Browser] 选择共享: {file.Name}");
            _ = Task.Run(async () => await ConnectAndBrowseShareAsync(file.Name));
            return;
        }

        // 目录浏览模式：点击进入子目录
        if (!file.IsDirectory) return;

        System.Diagnostics.Debug.WriteLine($"[SMB Browser] 点击目录: {file.Name} path={file.Path}");
        _pathStack.Push(_currentPath);
        _ = Task.Run(async () => await LoadDirectoryInternalAsync(file.Path));
    }

    private async Task LoadDirectoryInternalAsync(string path)
    {
        System.Diagnostics.Debug.WriteLine($"[SMB Browser] LoadDirectoryInternalAsync path={path}");

        if (!_isInitialized)
        {
            System.Diagnostics.Debug.WriteLine("[SMB Browser] 未初始化，跳过");
            RunOnUiThread(() => ShowError("连接未初始化"));
            return;
        }
        if (_isLoading)
        {
            System.Diagnostics.Debug.WriteLine("[SMB Browser] 已在加载中，跳过");
            return;
        }
        _isLoading = true;

        _currentPath = path;
        RunOnUiThread(() => ShowLoading($"📁 {path}"));

        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var files = await _smbService.ListFilesAsync(path);
            sw.Stop();

            System.Diagnostics.Debug.WriteLine($"[SMB Browser] ListFilesAsync 返回 {files.Count} 项, 耗时 {sw.ElapsedMilliseconds}ms");

            var dirs = files.Where(f => f.IsDirectory).OrderBy(f => f.Name).ToList();
            System.Diagnostics.Debug.WriteLine($"[SMB Browser] 其中目录 {dirs.Count} 个");

            RunOnUiThread(() =>
            {
                _items.Clear();
                _items.AddRange(dirs);
                _adapter.NotifyDataSetChanged();

                // 更新路径显示
                var displayPath = string.IsNullOrEmpty(_selectedShare) ? path : $"\\\\{_selectedShare}{path}";
                _tvCurrentPath.Text = $"📁 {displayPath}";

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
                _progressBar.Visibility = ViewStates.Gone;
            });
        }
        catch (Java.Lang.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SMB Browser] Java异常: {ex.Message}");
            RunOnUiThread(() => ShowError($"加载失败: {ex.Message}"));
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SMB Browser] 加载目录异常: {ex}");
            RunOnUiThread(() => ShowError($"加载失败: {ex.Message}"));
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void RunOnUiThread(Action action)
    {
        if (_activity.IsDestroyed || _activity.IsFinishing) return;
        _activity.RunOnUiThread(() =>
        {
            try { action(); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SMB Browser] UI 线程异常: {ex}");
            }
        });
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

    /// <summary>返回键处理：在共享选择模式关闭对话框，在浏览模式返回上级目录或共享列表</summary>
    public override void OnBackPressed()
    {
        if (_isLoading)
        {
            System.Diagnostics.Debug.WriteLine("[SMB Browser] OnBackPressed 跳过: 加载中");
            return;
        }

        // 共享选择模式：直接关闭
        if (_isShareMode)
        {
            System.Diagnostics.Debug.WriteLine("[SMB Browser] 返回键关闭对话框");
            base.OnBackPressed();
            return;
        }

        // 目录浏览模式：有上级目录则返回，否则回到共享列表
        if (_pathStack.Count > 0)
        {
            var parentPath = _pathStack.Pop();
            System.Diagnostics.Debug.WriteLine($"[SMB Browser] 返回上级目录: {parentPath}");
            _ = Task.Run(async () => await LoadDirectoryInternalAsync(parentPath));
        }
        else
        {
            // 根目录：返回共享选择
            System.Diagnostics.Debug.WriteLine("[SMB Browser] 返回共享列表");
            _ = Task.Run(async () => await ListSharesAndShowAsync());
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
