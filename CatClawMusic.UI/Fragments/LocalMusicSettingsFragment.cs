using Android.OS;
using Android.Views;
using Android.Widget;
using CatClawMusic.UI.Services;
using CatClawMusic.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

/// <summary>
/// 本地音乐设置页面 Fragment。
/// <para>提供本地音乐扫描相关的配置项，包括：</para>
/// <list type="bullet">
///   <item>是否使用 MediaStore 作为扫描数据源</item>
///   <item>是否过滤短音频文件</item>
///   <item>自定义音乐文件夹的添加与删除</item>
///   <item>手动触发本地音乐扫描</item>
/// </list>
/// <para>继承自 <see cref="SettingsSubPageFragment"/>，作为设置页的子页面呈现。</para>
/// </summary>
public class LocalMusicSettingsFragment : SettingsSubPageFragment
{
    /// <summary>
    /// "使用 MediaStore" 开关控件，控制扫描时是否优先使用系统 MediaStore 数据库。
    /// </summary>
    private Switch? _switchMediaStore;

    /// <summary>
    /// "过滤短音频" 开关控件，控制是否过滤掉时长过短的音频文件。
    /// </summary>
    private Switch? _switchFilterShort;

    /// <summary>
    /// 文件夹列表的容器布局，用于动态渲染用户添加的自定义音乐文件夹条目。
    /// </summary>
    private LinearLayout? _folderListContainer;

    /// <summary>
    /// 扫描进度条，在扫描过程中显示当前进度。
    /// </summary>
    private ProgressBar? _scanProgress;

    /// <summary>
    /// 扫描状态文本，显示当前扫描的进度信息或状态描述。
    /// </summary>
    private TextView? _scanStatus;

    /// <summary>
    /// 设置页面的 ViewModel，用于管理音乐文件夹的增删操作及与 UI 的数据绑定。
    /// </summary>
    private SettingsViewModel? _viewModel;

    /// <summary>
    /// 获取当前子页面的标题文本。
    /// </summary>
    /// <returns>返回 "本地音乐" 作为页面标题。</returns>
    protected override string GetTitle() => "本地音乐";

    /// <summary>
    /// 创建 Fragment 的视图层次结构，从布局资源中加载本地音乐设置页面的 UI。
    /// </summary>
    /// <param name="inflater">用于填充布局的 LayoutInflater。</param>
    /// <param name="container">父视图容器，可为 null。</param>
    /// <param name="state">保存的实例状态，可为 null。</param>
    /// <returns>填充后的根视图。</returns>
    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
        => inflater.Inflate(Resource.Layout.fragment_local_music_settings, container, false)!;

    /// <summary>
    /// 子视图创建完成后的初始化入口。
    /// <para>完成以下初始化工作：</para>
    /// <list type="number">
    ///   <item>从 DI 容器获取 <see cref="SettingsViewModel"/> 实例</item>
    ///   <item>绑定所有 UI 控件引用</item>
    ///   <item>将扫描设置同步到开关控件状态</item>
    ///   <item>注册开关状态变更事件，将用户操作写回 <see cref="ScanSettings"/></item>
    ///   <item>注册"添加文件夹"按钮点击事件</item>
    ///   <item>注册"开始扫描"按钮点击事件</item>
///   <item>刷新文件夹列表</item>
    /// </list>
    /// </summary>
    /// <param name="view">已创建的根视图。</param>
    /// <param name="state">保存的实例状态，可为 null。</param>
    protected override void OnSubViewCreated(View view, Bundle? state)
    {
        _viewModel = MainApplication.Services.GetRequiredService<SettingsViewModel>();

        _switchMediaStore = view.FindViewById<Switch>(Resource.Id.switch_use_media_store);
        _switchFilterShort = view.FindViewById<Switch>(Resource.Id.switch_filter_short);
        _folderListContainer = view.FindViewById<LinearLayout>(Resource.Id.folder_list);
        _scanProgress = view.FindViewById<ProgressBar>(Resource.Id.scan_progress);
        _scanStatus = view.FindViewById<TextView>(Resource.Id.scan_status);

        _switchMediaStore!.Checked = ScanSettings.UseMediaStore;
        _switchFilterShort!.Checked = ScanSettings.FilterShortAudio;

        _switchMediaStore.CheckedChange += (s, e) => ScanSettings.UseMediaStore = e.IsChecked;
        _switchFilterShort.CheckedChange += (s, e) => ScanSettings.FilterShortAudio = e.IsChecked;

        var btnAdd = view.FindViewById<Button>(Resource.Id.btn_add_folder);
        btnAdd!.Click += async (s, e) =>
        {
            await _viewModel.AddMusicFolderCommand.ExecuteAsync(null);
            RefreshFolderList();
        };

        var btnScan = view.FindViewById<Button>(Resource.Id.btn_start_scan);
        btnScan!.Click += async (s, e) => await StartScanAsync();

        RefreshFolderList();
    }

    /// <summary>
    /// Fragment 恢复可见时调用。
    /// </summary>
    public override void OnResume()
    {
        base.OnResume();
    }

    /// <summary>
    /// 异步执行本地音乐扫描流程。
    /// <para>扫描流程如下：</para>
    /// <list type="number">
    ///   <item>显示进度条和状态文本，初始化为"准备扫描..."</item>
    ///   <item>订阅 <see cref="LibraryViewModel"/> 的 PropertyChanged 事件以实时更新进度</item>
    ///   <item>调用 <see cref="LibraryViewModel.LoadLocalAsync"/> 强制重新加载本地音乐</item>
    ///   <item>轮询等待扫描完成（IsScanning 变为 false）</item>
    ///   <item>扫描完成后将进度设为 100%，状态文本设为"扫描完成"</item>
    ///   <item>延迟 800ms 后隐藏进度条和状态文本</item>
    /// </list>
    /// </summary>
    private async Task StartScanAsync()
    {
        var libVm = MainApplication.Services.GetRequiredService<LibraryViewModel>();

        if (_scanProgress != null) _scanProgress.Visibility = ViewStates.Visible;
        if (_scanStatus != null) { _scanStatus.Visibility = ViewStates.Visible; _scanStatus.Text = "准备扫描..."; }

        // 订阅扫描进度变更事件，用于实时更新 UI 上的进度条和状态文本
        libVm.PropertyChanged += OnScanProgressChanged;

        try
        {
            // 强制重新加载本地音乐库，触发扫描
            await libVm.LoadLocalAsync(forceReload: true);
            // 轮询等待扫描完成，每 200ms 检查一次
            while (libVm.IsScanning) await Task.Delay(200);
        }
        catch { }

        // 扫描结束，取消订阅进度事件避免内存泄漏
        libVm.PropertyChanged -= OnScanProgressChanged;

        if (_scanProgress != null) _scanProgress.Progress = 100;
        if (_scanStatus != null) _scanStatus.Text = "扫描完成";

        // 延迟一段时间后隐藏进度 UI，给用户一个视觉反馈窗口
        await Task.Delay(800);
        if (_scanProgress != null) _scanProgress.Visibility = ViewStates.Gone;
        if (_scanStatus != null) _scanStatus.Visibility = ViewStates.Gone;
    }

    /// <summary>
    /// 扫描进度变更事件处理器。
    /// <para>监听 <see cref="LibraryViewModel"/> 的 <see cref="LibraryViewModel.ScanProgress"/> 和
    /// <see cref="LibraryViewModel.ScanStatus"/> 属性变更，在 UI 线程上更新进度条和状态文本。</para>
    /// </summary>
    /// <param name="sender">事件源，即 LibraryViewModel 实例。</param>
    /// <param name="e">属性变更事件参数，包含变更的属性名称。</param>
    private void OnScanProgressChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // 仅处理扫描进度和扫描状态相关的属性变更
        if (e.PropertyName is not (nameof(LibraryViewModel.ScanProgress) or nameof(LibraryViewModel.ScanStatus))) return;
        var libVm = MainApplication.Services.GetRequiredService<LibraryViewModel>();
        if (Activity == null) return;
        // 确保在 UI 线程上更新控件，避免跨线程异常
        Activity.RunOnUiThread(() =>
        {
            if (_scanProgress != null) _scanProgress.Progress = libVm.ScanProgress;
            if (_scanStatus != null) _scanStatus.Text = libVm.ScanStatus;
        });
    }

    /// <summary>
    /// 刷新自定义音乐文件夹列表的 UI 显示。
    /// <para>清空现有列表后，遍历 <see cref="SettingsViewModel.MusicFolders"/> 中的每个文件夹，
    /// 为每个文件夹动态创建一行布局，包含：</para>
    /// <list type="bullet">
    ///   <item>文件夹路径文本（带 📁 图标前缀）</item>
    ///   <item>"删除"按钮，点击后移除对应文件夹并刷新列表</item>
    /// </list>
    /// <para>若文件夹列表为空，则显示"尚未添加自定义文件夹"的提示文本。</para>
    /// </summary>
    private void RefreshFolderList()
    {
        if (_folderListContainer == null || _viewModel == null) return;
        // 清空容器中所有已有子视图，准备重新渲染
        _folderListContainer.RemoveAllViews();

        var folderIndex = 0;
        foreach (var folder in _viewModel.MusicFolders)
        {
            // 捕获当前索引到局部变量，避免闭包中索引错位
            var currentIndex = folderIndex;

            // 创建文件夹条目行：水平排列的 LinearLayout
            var row = new LinearLayout(Context!) { Orientation = Android.Widget.Orientation.Horizontal };
            var rowBg = new Android.Graphics.Drawables.GradientDrawable();
            rowBg.SetColor(Android.Graphics.Color.ParseColor("#0F000000"));
            rowBg.SetCornerRadius(24);
            row.Background = rowBg;
            row.SetPadding(16, 8, 12, 8);

            // 文件夹路径文本，权重为 1 以占据剩余空间
            var text = new TextView(Context!) { Text = $"📁 {folder}", TextSize = 13 };
            text.SetTextColor(Android.Graphics.Color.ParseColor("#2D2438"));
            var textLp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1)
            { Gravity = GravityFlags.CenterVertical };
            text.LayoutParameters = textLp;
            row.AddView(text);

            // 删除按钮：点击后从 ViewModel 中移除对应索引的文件夹，并刷新列表
            var delBtn = new Android.Widget.Button(Context!) { Text = "删除", TextSize = 12 };
            delBtn.SetTextColor(Android.Graphics.Color.ParseColor("#E04040"));
            var btnBg = new Android.Graphics.Drawables.GradientDrawable();
            btnBg.SetColor(Android.Graphics.Color.ParseColor("#1AE04040"));
            btnBg.SetCornerRadius(32);
            delBtn.Background = btnBg;
            delBtn.SetPadding(20, 8, 20, 8);
            var btnLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
            { Gravity = GravityFlags.CenterVertical };
            delBtn.LayoutParameters = btnLp;
            delBtn.Click += (s, e) => { _viewModel.RemoveMusicFolder(currentIndex); RefreshFolderList(); };
            row.AddView(delBtn);

            // 设置行间距并添加到容器
            var rowLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
            rowLp.BottomMargin = 8;
            row.LayoutParameters = rowLp;
            _folderListContainer.AddView(row);
            folderIndex++;
        }

        // 文件夹列表为空时，显示空状态提示
        if (_viewModel.MusicFolders.Count == 0)
        {
            var empty = new TextView(Context!)
            {
                Text = "尚未添加自定义文件夹",
                TextSize = 13,
                Gravity = Android.Views.GravityFlags.Center,
            };
            empty.SetTextColor(Android.Graphics.Color.ParseColor("#B0A8BA"));
            empty.SetPadding(16, 24, 16, 24);
            _folderListContainer.AddView(empty);
        }
    }
}
