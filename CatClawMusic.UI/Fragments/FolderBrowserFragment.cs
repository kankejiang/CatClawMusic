using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.UI.Helpers;
using CatClawMusic.UI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

/// <summary>
/// 文件夹浏览器 Fragment — 浏览设备目录并选择音乐文件夹
/// <para>需要 MANAGE_EXTERNAL_STORAGE 权限（Android 11+）才能访问共享存储目录</para>
/// </summary>
public class FolderBrowserFragment : SettingsSubPageFragment
{
    /// <summary>选择结果回调</summary>
    public static event EventHandler<string>? FolderSelected;

    private RecyclerView? _rvFolders;
    private TextView? _tvCurrentPath;
    private Button? _btnSelect;
    private ImageButton? _btnGoUp;
    private FolderAdapter? _adapter;

    /// <summary>当前浏览的目录路径</summary>
    private string _currentPath = "/storage/emulated/0";

    /// <summary>起始根路径</summary>
    private const string RootPath = "/storage/emulated/0";

    protected override string GetTitle() => "选择文件夹";

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
        => inflater.Inflate(Resource.Layout.fragment_folder_browser, container, false)!;

    protected override void OnSubViewCreated(View view, Bundle? state)
    {
        _tvCurrentPath = view.FindViewById<TextView>(Resource.Id.tv_current_path);
        _rvFolders = view.FindViewById<RecyclerView>(Resource.Id.rv_folders);
        _btnSelect = view.FindViewById<Button>(Resource.Id.btn_select_folder);
        _btnGoUp = view.FindViewById<ImageButton>(Resource.Id.btn_go_up);

        _adapter = new FolderAdapter();
        _rvFolders!.SetLayoutManager(new LinearLayoutManager(Context));
        _rvFolders.SetAdapter(_adapter);
        _rvFolders.AddItemDecoration(new ItemSpacingDecoration(4));

        _adapter.FolderClick += OnFolderClick;
        _btnSelect!.Click += (_, _) => SelectCurrentFolder();
        _btnGoUp!.Click += (_, _) => NavigateUp();

        // 恢复上次浏览位置
        var args = Arguments;
        if (args != null && args.ContainsKey("current_path"))
            _currentPath = args.GetString("current_path") ?? RootPath;

        LoadDirectoriesAsync(_currentPath);
    }

    /// <summary>点击子文件夹，进入下一级</summary>
    private void OnFolderClick(object? sender, string path) => LoadDirectoriesAsync(path);

    /// <summary>返回上级目录</summary>
    private void NavigateUp()
    {
        var parent = Path.GetDirectoryName(_currentPath);
        if (!string.IsNullOrEmpty(parent) && parent.StartsWith("/storage"))
            LoadDirectoriesAsync(parent!);
    }

    /// <summary>确认选择当前目录</summary>
    private void SelectCurrentFolder()
    {
        // 直接保存到 ScanSettings
        ScanSettings.AddLocalFolderPath(_currentPath);

        // 通知外部（可选，用于需要即时响应的场景）
        FolderSelected?.Invoke(this, _currentPath);
        Nav?.GoBack();
    }

    /// <summary>异步加载指定目录下的子文件夹（I/O 操作放在后台线程）</summary>
    private void LoadDirectoriesAsync(string path)
    {
        _currentPath = path;
        _tvCurrentPath!.Text = path;

        // 后台线程读取目录
        _ = Task.Run(() =>
        {
            var dirs = new List<FolderItem>();
            try
            {
                if (!Directory.Exists(path))
                {
                    ShowError("目录不存在");
                    return;
                }

                var entries = Directory.GetDirectories(path);
                foreach (var fullPath in entries)
                {
                    var name = Path.GetFileName(fullPath);
                    // 跳过隐藏目录和 Android/data 等
                    if (name.StartsWith(".") || name == "data") continue;
                    try
                    {
                        // 尝试列举子目录以确认可读
                        Directory.GetDirectories(fullPath);
                        dirs.Add(new FolderItem(name, fullPath));
                    }
                    catch { /* 无权限，跳过 */ }
                }
            }
            catch (UnauthorizedAccessException)
            {
                ShowError("无权限访问此目录");
            }
            catch (Exception ex)
            {
                ShowError($"读取失败: {ex.Message}");
            }

            dirs.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            Activity?.RunOnUiThread(() =>
            {
                _adapter!.UpdateItems(dirs);
                _rvFolders!.ScrollToPosition(0);
            });
        });
    }

    private void ShowError(string message)
    {
        Activity?.RunOnUiThread(() =>
        {
            Toast.MakeText(Activity, message, ToastLength.Short)?.Show();
        });
    }

    /// <summary>文件夹列表项数据</summary>
    private record FolderItem(string Name, string Path);

    /// <summary>文件夹列表适配器</summary>
    private class FolderAdapter : RecyclerView.Adapter
    {
        private List<FolderItem> _items = new();

        public event EventHandler<string>? FolderClick;

        public void UpdateItems(List<FolderItem> items)
        {
            _items = items;
            NotifyDataSetChanged();
        }

        public override int ItemCount => _items.Count;

        public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
        {
            if (holder is FolderViewHolder vh)
            {
                var item = _items[position];
                vh.Name.Text = item.Name;
                vh.Path.Text = item.Path;
                vh.ItemView.Click -= vh.Handler;
                vh.Handler = (_, _) => FolderClick?.Invoke(this, item.Path);
                vh.ItemView.Click += vh.Handler;
            }
        }

        public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
        {
            var view = LayoutInflater.From(parent.Context)!
                .Inflate(Resource.Layout.item_folder_browser, parent, false)!;
            return new FolderViewHolder(view);
        }
    }

    /// <summary>文件夹列表项 ViewHolder</summary>
    private class FolderViewHolder : RecyclerView.ViewHolder
    {
        public TextView Name { get; }
        public TextView Path { get; }
        public EventHandler? Handler;

        public FolderViewHolder(View view) : base(view)
        {
            Name = view.FindViewById<TextView>(Resource.Id.tv_folder_name)!;
            Path = view.FindViewById<TextView>(Resource.Id.tv_folder_path)!;
        }
    }

    /// <summary>列表项间距装饰</summary>
    private class ItemSpacingDecoration : RecyclerView.ItemDecoration
    {
        private readonly int _spacing;
        public ItemSpacingDecoration(int spacing) => _spacing = spacing;
        public override void GetItemOffsets(Android.Graphics.Rect outRect, View view, RecyclerView parent, RecyclerView.State state)
        {
            outRect.Bottom = _spacing;
        }
    }
}
