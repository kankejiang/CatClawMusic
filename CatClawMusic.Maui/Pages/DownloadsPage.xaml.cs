using CatClawMusic.Core.Interfaces;
using CatClawMusic.Maui.Services;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;

namespace CatClawMusic.Maui.Pages;

/// <summary>
/// 下载管理页：展示与管理下载任务（新建 URL 下载、暂停/继续/取消/重试/删除、更改下载路径）。
/// </summary>
public partial class DownloadsPage : ContentPage
{
    private readonly DownloadsViewModel _vm;
    private readonly DownloadManager _manager;
    private readonly IPermissionService _permissionService;

    public DownloadsPage(DownloadsViewModel vm, DownloadManager manager, IPermissionService permissionService)
    {
        InitializeComponent();
        _vm = vm;
        _manager = manager;
        _permissionService = permissionService;
        BindingContext = vm;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Transient 页面即将销毁：解除事件订阅，避免单例 DownloadManager 长引用本页 VM
        _vm.Dispose();
    }

    /// <summary>右上角 ＋ ：新建下载任务（支持 http/https 直链与 magnet: 磁力链接）</summary>
    private async void OnAddDownloadTapped(object? sender, EventArgs e)
    {
        var url = await PromptAsync("新建下载", "输入下载地址\n支持：http/https 直链、magnet: 磁力链接", "开始下载", "取消",
            placeholder: "https://... 或 magnet:?xt=urn:btih:...", keyboard: Keyboard.Url);
        if (string.IsNullOrWhiteSpace(url)) return;

        string? name = null;
        if (!url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
        {
            name = await PromptAsync("文件名称", "输入保存文件名（留空自动识别）", "开始下载", "取消",
                placeholder: "文件名.mp3", keyboard: Keyboard.Text);
        }
        _vm.AddUrlDownload(url, string.IsNullOrWhiteSpace(name) ? null : name);
    }

    /// <summary>右上角 ⚙ ：打开下载设置面板（同时下载任务数、每任务限速）</summary>
    private void OnSettingsTapped(object? sender, EventArgs e)
    {
        DownloadSettingsPopup.ClearContent();
        DownloadSettingsPopup.AddContent(BuildDownloadSettingsPanel());
        DownloadSettingsPopup.Open();
    }

    /// <summary>构建设置面板内容；chip 点击后改配置并就地刷新高亮（弹窗保持打开）</summary>
    private View BuildDownloadSettingsPanel()
    {
        var stack = new VerticalStackLayout { Spacing = 12 };
        stack.Children.Add(SectionLabel("同时下载任务数"));
        stack.Children.Add(BuildChipsRow(
            new[] { (1, "1"), (2, "2"), (3, "3"), (4, "4"), (5, "5") },
            _manager.ConcurrentLimit,
            v => { _manager.SetConcurrentLimit(v); RefreshSettingsPanel(); }));

        var curKbps = _manager.MaxDownloadBytesPerSecond > 0
            ? (int)(_manager.MaxDownloadBytesPerSecond / 1024) : 0;
        stack.Children.Add(SectionLabel("每任务下载速度限制"));
        stack.Children.Add(BuildChipsRow(
            new[]
            {
                (0, "不限"), (512, "512 KB/s"), (1024, "1 MB/s"),
                (2048, "2 MB/s"), (5120, "5 MB/s"), (10240, "10 MB/s")
            },
            curKbps,
            v => { _manager.SetSpeedLimitKbps(v); RefreshSettingsPanel(); }));

        stack.Children.Add(new Label
        {
            Text = "设置后立即生效，无需重启。",
            FontSize = 11,
            TextColor = (Color)Application.Current!.Resources["TextHintColor"]
        });
        return stack;
    }

    private void RefreshSettingsPanel()
    {
        DownloadSettingsPopup.ClearContent();
        DownloadSettingsPopup.AddContent(BuildDownloadSettingsPanel());
    }

    private static Label SectionLabel(string text) => new()
    {
        Text = text,
        FontSize = 13,
        FontFamily = "OpenSansSemibold",
        TextColor = (Color)Application.Current!.Resources["TextPrimaryColor"]
    };

    /// <summary>构建一组可选 chip；选中项高亮主色，点击触发 onSelect</summary>
    private View BuildChipsRow((int value, string label)[] options, int selectedValue, Action<int> onSelect)
    {
        var primary = (Color)Application.Current!.Resources["PrimaryColor"];
        var glass = (Color)Application.Current!.Resources["GlassButtonColor"];
        var textPrimary = (Color)Application.Current!.Resources["TextPrimaryColor"];

        var chips = new FlexLayout
        {
            Wrap = FlexWrap.Wrap,
            Direction = FlexDirection.Row,
            AlignItems = FlexAlignItems.Start,
            JustifyContent = FlexJustify.Start
        };
        foreach (var (value, label) in options)
        {
            var isSel = value == selectedValue;
            var chip = new Border
            {
                BackgroundColor = isSel ? primary : glass,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(17) },
                HeightRequest = 34,
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(14, 0),
                Content = new Label
                {
                    Text = label,
                    FontSize = 13,
                    TextColor = isSel ? Colors.White : textPrimary,
                    VerticalTextAlignment = TextAlignment.Center,
                    HorizontalTextAlignment = TextAlignment.Center
                }
            };
            var tap = new TapGestureRecognizer();
            var captured = value;
            tap.Tapped += (_, _) => onSelect(captured);
            chip.GestureRecognizers.Add(tap);
            chips.Children.Add(chip);
        }
        return chips;
    }

    /// <summary>弹输入框：Windows 嵌入模式本页不在窗口视觉树（Page.Window 为 null），
    /// DisplayPromptAsync 会静默失败——改由窗口根页面调用</summary>
    private Task<string?> PromptAsync(string title, string message, string accept, string cancel,
        string placeholder, Keyboard keyboard)
    {
        var root = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (root != null && root != this)
            return root.DisplayPromptAsync(title, message, accept, cancel,
                placeholder: placeholder, keyboard: keyboard);
        return DisplayPromptAsync(title, message, accept, cancel, placeholder: placeholder, keyboard: keyboard);
    }

    /// <summary>弹提示框：同上，Windows 嵌入模式用窗口根页面</summary>
    private Task AlertAsync(string title, string message, string cancel = "确定")
    {
        var root = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (root != null && root != this)
            return root.DisplayAlert(title, message, cancel);
        return DisplayAlert(title, message, cancel);
    }

    /// <summary>弹选择框（确认/取消双按钮）：同上，Windows 嵌入模式用窗口根页面</summary>
    private Task<bool> AlertAsync(string title, string message, string accept, string cancel)
    {
        var root = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (root != null && root != this)
            return root.DisplayAlert(title, message, accept, cancel);
        return DisplayAlert(title, message, accept, cancel);
    }

    /// <summary>任务操作按钮统一入口（ClassId 标记动作，Windows 嵌入模式不依赖绑定树）</summary>
    private async void OnTaskActionClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: string id } btn) return;
        switch (btn.ClassId)
        {
            case "pause": _vm.PauseTask(id); break;
            case "resume": _vm.ResumeTask(id); break;
            case "cancel": _vm.CancelTask(id); break;
            case "retry":
                var retryItem = _manager.Tasks.FirstOrDefault(t => t.Id == id);
                if (retryItem?.Status == DownloadStatus.Completed)
                {
                    var reOk = await AlertAsync("重新下载", "将重新下载该文件并覆盖本地，是否继续？", "重新下载", "取消");
                    if (!reOk) break;
                }
                _vm.RetryTask(id); break;
            case "delete":
                var delOk = await AlertAsync("删除任务", "仅删除任务记录（保留已下载文件），确定？", "删除", "取消");
                if (delOk) _vm.DeleteTask(id);
                break;
            case "deletefile":
                var delFileOk = await AlertAsync("删除任务及文件", "将删除任务并移除其已下载文件，确定？", "删除", "取消");
                if (!delFileOk) break;
                var error = await Task.Run(() => _manager.Delete(id, deleteFile: true));
                if (error != null)
                    await AlertAsync("文件删除失败", error, "确定");
                break;
        }
    }

    /// <summary>点击任务卡片：已完成任务打开文件/所在文件夹；磁力多文件种子打开所在目录</summary>
    private async void OnTaskTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Border { BindingContext: DownloadTaskItem item }) return;
        if (item.Status != DownloadStatus.Completed)
            return;

        var path = item.LocalPath;
        var isDir = Directory.Exists(path);
        if (!isDir && !File.Exists(path))
        {
            await AlertAsync("提示", "文件不存在或已被移动", "确定");
            return;
        }

        try
        {
#if WINDOWS
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
#elif ANDROID
            if (isDir)
            {
                // Android 打开目录需文件管理器授权，直接提示路径（BT 种子目录）
                await AlertAsync("下载完成", $"文件已保存到：\n{path}", "确定");
            }
            else
            {
                await Launcher.OpenAsync(new OpenFileRequest
                {
                    Title = "打开文件",
                    File = new ReadOnlyFile(path)
                });
            }
#endif
        }
        catch (Exception ex)
        {
            await AlertAsync("打开失败", ex.Message, "确定");
        }
    }

    /// <summary>更改下载位置：Android 走自研文件管理器（需所有文件访问权限），Windows 走系统文件夹选择器</summary>
    private async void OnChangePathClicked(object? sender, EventArgs e)
    {
#if ANDROID
        var granted = await _permissionService.CheckManageStoragePermissionAsync();
        if (!granted)
        {
            var goToSettings = await AlertAsync(
                "需要所有文件访问权限",
                "更改下载位置需要授予「所有文件访问」权限（管理所有文件），请在系统设置中开启。",
                "去设置", "仍要进入");
            if (goToSettings)
            {
                _permissionService.RequestManageStoragePermissionAsync();
                return;
            }
        }
        await Shell.Current.GoToAsync("folderbrowser?mode=download&title=选择下载文件夹");
#elif WINDOWS
        var path = await Platforms.Windows.WindowsFolderPicker.PickFolderAsync();
        if (!string.IsNullOrEmpty(path))
        {
            _manager.SetDownloadFolderPath(path);
            _vm.RefreshStats();
        }
        else
        {
            await AlertAsync("提示", "未能获取所选文件夹，请重试。", "确定");
        }
#endif
    }
}
