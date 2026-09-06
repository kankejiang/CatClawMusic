using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.Services;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;
using Path = System.IO.Path; // 消歧义：Microsoft.Maui.Controls.Shapes.Path vs System.IO.Path

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

    /// <summary>用系统弹窗打开已完成文件（Android 弹系统"打开文件"底部选择器，任何格式由系统分发给可处理的应用；
    /// Windows 走 ShellExecute）。目录则提示保存路径。</summary>
    private async Task OpenWithSystemAsync(string path)
    {
        try
        {
            var isDir = Directory.Exists(path);
            if (!isDir && !File.Exists(path))
            {
                await AlertAsync("提示", "文件不存在或已被移动", "确定");
                return;
            }
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
                // 原生 Intent 直发（零拷贝、免 Essentials 封装层）：复用 MAUI 注入的 Essentials
                // FileProvider（authority 实际为 {package}.fileProvider，见 dumpsys package），
                // 其 file_paths external-path 已覆盖 /storage/emulated/0，秒弹系统选择器
                var ctx = global::Android.App.Application.Context;
                var uri = global::AndroidX.Core.Content.FileProvider.GetUriForFile(
                    ctx,
                    ctx.PackageName + ".fileProvider",
                    new Java.IO.File(path));
                var ext = System.IO.Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
                var mime = global::Android.Webkit.MimeTypeMap.Singleton.GetMimeTypeFromExtension(ext)
                           ?? "application/octet-stream";
                var intent = new global::Android.Content.Intent(global::Android.Content.Intent.ActionView);
                intent.SetDataAndType(uri, mime);
                intent.AddFlags(global::Android.Content.ActivityFlags.GrantReadUriPermission);
                var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
                if (activity != null)
                    activity.StartActivity(intent);
                else
                    intent.AddFlags(global::Android.Content.ActivityFlags.NewTask);
                if (activity == null)
                    ctx.StartActivity(intent);
            }
#endif
        }
        catch (Exception ex)
        {
            await AlertAsync("打开失败", ex.Message, "确定");
        }
    }

    /// <summary>支持菜单操作（播放/入库/元数据匹配）的音频扩展名</summary>
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".wav", ".m4a", ".aac", ".ogg", ".oga", ".opus", ".wma", ".ape", ".alac", ".aiff", ".aif", ".wv", ".mka", ".tta"
    };

    private static bool IsAudioFile(string path)
        => !string.IsNullOrWhiteSpace(path) && AudioExtensions.Contains(Path.GetExtension(path));

    /// <summary>点击任务卡片：已完成音频文件弹出操作菜单（播放/加入音乐库/匹配元数据），
    /// 其他文件保持打开行为（音频外的文件直接打开；磁力目录提示路径）。</summary>
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

        if (!isDir && IsAudioFile(path))
        {
            ShowCompletedAudioMenu(item, path);
            return;
        }

        // 非音频文件：系统弹窗打开（音频也可经"更多"按钮走此路径）
        await OpenWithSystemAsync(path);
    }

    // ═══ 已完成音频任务：底部抽屉操作菜单（播放 / 加入音乐库 / 匹配元数据） ═══

    /// <summary>弹出已完成音频任务的操作菜单。匹配元数据项由 IMenuContributorPlugin 贡献
    /// （如歌词搜索插件），未配置对应插件时不显示。</summary>
    private void ShowCompletedAudioMenu(DownloadTaskItem item, string path)
    {
        var host = Content as Grid;
        if (host == null) return;

        // 读取文件标签构造临时 Song（FilePath 为插件/入库的唯一必填依据），失败回退文件名
        var song = TagReader.ReadSongInfo(path, readDuration: true)
            ?? new Song
            {
                Title = Path.GetFileNameWithoutExtension(path),
                FilePath = path,
                Source = SongSource.Local
            };
        if (string.IsNullOrWhiteSpace(song.FilePath)) song.FilePath = path;

        var popup = new Controls.ContextMenuPopup();
        BuildDownloadMenu(popup, song, item);

        // 全窗覆盖挂到页面根 Grid（推入页不设 RowSpan 全覆盖问题：本页 2 行）
        Grid.SetRow(popup, 0);
        Grid.SetRowSpan(popup, Math.Max(1, host.RowDefinitions.Count));
        Grid.SetColumn(popup, 0);
        Grid.SetColumnSpan(popup, Math.Max(1, host.ColumnDefinitions.Count));
        host.Children.Add(popup);

        EventHandler? closed = null;
        closed = (_, _) =>
        {
            popup.Closed -= closed;
            try { if (popup.Parent is Layout parent) parent.Children.Remove(popup); } catch { }
        };
        popup.Closed += closed;

        var maxW = Width > 0 ? Width : (Application.Current?.Windows.FirstOrDefault()?.Width ?? 0);
        var maxH = Height > 0 ? Height : (Application.Current?.Windows.FirstOrDefault()?.Height ?? 0);
        // Android 抽屉贴底弹出忽略锚点；Windows 下拉卡片锚在卡片中下部
        popup.ShowAt(maxW / 2, maxH * 0.7, maxW, maxH);
    }

    /// <summary>构建已完成音频任务菜单：头部文件信息 + 播放 / 加入音乐库 / 插件贡献项（元数据匹配等）。</summary>
    private void BuildDownloadMenu(Controls.ContextMenuPopup popup, Song song, DownloadTaskItem item)
    {
        popup.ClearContent();

        var textPrimary = (Color)Application.Current!.Resources["TextPrimaryColor"];
        var textSecondary = (Color)Application.Current!.Resources["TextSecondaryColor"];

        // 头部：标题 + 歌手/大小副文本
        var header = new VerticalStackLayout
        {
            Spacing = 3,
            Padding = new Thickness(14, 10, 14, 8)
        };
        header.Add(new Label
        {
            Text = string.IsNullOrWhiteSpace(song.Title) ? item.DisplayName : song.Title,
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = textPrimary,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1
        });
        var sizeText = item.TotalBytes > 0 ? DownloadTaskItem.FormatBytes(item.TotalBytes) : "";
        header.Add(new Label
        {
            Text = string.IsNullOrWhiteSpace(song.Artist)
                ? (string.IsNullOrEmpty(sizeText) ? "本地音频文件" : sizeText)
                : (string.IsNullOrEmpty(sizeText) ? song.Artist : $"{song.Artist} · {sizeText}"),
            FontSize = 12,
            TextColor = textSecondary,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1
        });
        popup.AddContent(header);
        popup.AddContent(new BoxView
        {
            HeightRequest = 1,
            Color = (Color)Application.Current!.Resources["DividerColor"],
            Opacity = 0.6,
            Margin = new Thickness(10, 0, 10, 4)
        });

        popup.AddContent(CreateMenuRow("▶", "播放", async () =>
        {
            await popup.CloseAsync();
            await PlayDownloadedAsync(song);
        }));
        popup.AddContent(CreateMenuRow("＋", "加入音乐库", async () =>
        {
            await popup.CloseAsync();
            await ImportToLibraryAsync(song);
        }));

        // 插件贡献菜单项（如歌词搜索插件提供「元数据匹配」；未配置对应插件时不显示）
        try
        {
            var pluginMgr = MauiProgram.Services?.GetService<IPluginManager>();
            if (pluginMgr != null)
            {
                foreach (var contributor in pluginMgr.GetEnabledPlugins<IMenuContributorPlugin>())
                {
                    List<MenuItemEntry>? items = null;
                    try { items = contributor.GetMenuItems(song); } catch { }
                    if (items == null || items.Count == 0) continue;

                    foreach (var mi in items)
                    {
                        var contributorRef = contributor;
                        var itemId = mi.Id;
                        popup.AddContent(CreateMenuRow("✎", mi.Title, async () =>
                        {
                            await popup.CloseAsync();
                            try { await contributorRef.OnMenuItemClicked(itemId, song, this); }
                            catch (Exception ex) { SongContextMenu.Toast($"打开失败：{ex.Message}"); }
                        }));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug("DownloadsPage", $"[DownloadsMenu] 加载插件菜单失败: {ex.Message}");
        }

        // 弹窗底部「更多」：先完整播放抽屉关闭动画（应用内独占渲染不掉帧），
        // 结束后再拉起系统"打开文件"弹窗——串行顺序，避免两段动画并行抢帧
        popup.AddContent(CreateMenuRow("⋯", "更多", async () =>
        {
            await popup.CloseAsync();
            await OpenWithSystemAsync(item.LocalPath);
        }));
    }

    /// <summary>播放下载完成的文件：先幂等入库（本地歌曲走完整封面/歌词管线，并取得真实 Id
    /// 供播放队列选中），再以单曲队列播放。</summary>
    private async Task PlayDownloadedAsync(Song song)
    {
        try
        {
            Song playSong = song;
            var lib = MauiProgram.Services?.GetService<IMusicLibraryService>();
            if (lib != null && !string.IsNullOrWhiteSpace(song.FilePath))
            {
                var imported = await lib.ImportSongsAsync(new List<Song> { song });
                playSong = imported.FirstOrDefault(s => string.Equals(s.FilePath, song.FilePath, StringComparison.OrdinalIgnoreCase))
                           ?? song;
            }

            var queue = MauiProgram.Services?.GetService<PlayQueue>();
            var audio = MauiProgram.Services?.GetService<IAudioPlayerService>();
            if (queue != null)
            {
                queue.SetSongs(new[] { playSong });
                queue.SelectSong(playSong.Id);
            }
            if (audio != null && !string.IsNullOrWhiteSpace(playSong.FilePath))
                await audio.PlayAsync(playSong.FilePath);
            SongContextMenu.Toast("开始播放");
        }
        catch (Exception ex)
        {
            Log.Debug("DownloadsPage", $"[DownloadsMenu] 播放失败: {ex.Message}");
            SongContextMenu.Toast($"播放失败：{ex.Message}");
        }
    }

    /// <summary>把下载文件导入音乐库（幂等：重复导入按 FilePath 去重）。</summary>
    private async Task ImportToLibraryAsync(Song song)
    {
        try
        {
            var lib = MauiProgram.Services?.GetService<IMusicLibraryService>();
            if (lib == null || string.IsNullOrWhiteSpace(song.FilePath))
            {
                SongContextMenu.Toast("音乐库服务不可用");
                return;
            }
            await lib.ImportSongsAsync(new List<Song> { song });
            SongContextMenu.Toast("已加入音乐库");
        }
        catch (Exception ex)
        {
            Log.Debug("DownloadsPage", $"[DownloadsMenu] 入库失败: {ex.Message}");
            SongContextMenu.Toast($"加入失败：{ex.Message}");
        }
    }

    /// <summary>创建一行菜单项（图标 + 文字，与歌曲上下文菜单同款紧凑样式）。</summary>
    private static View CreateMenuRow(string icon, string text, Func<Task> onTap)
    {
        var textPrimary = (Color)Application.Current!.Resources["TextPrimaryColor"];
        var primary = (Color)Application.Current!.Resources["PrimaryColor"];

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new() { Width = 28 },
                new() { Width = GridLength.Star }
            },
            ColumnSpacing = 10
        };
        row.Add(new Label
        {
            Text = icon,
            FontSize = 14,
            TextColor = primary,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        }, 0);
        row.Add(new Label
        {
            Text = text,
            FontSize = 14,
            TextColor = textPrimary,
            VerticalTextAlignment = TextAlignment.Center
        }, 1);

        var border = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(9) },
            StrokeThickness = 0,
            BackgroundColor = Colors.Transparent,
            Padding = new Thickness(12, 11),
            Margin = new Thickness(2, 2),
            HorizontalOptions = LayoutOptions.Fill,
            Content = row
        };
        border.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () => await onTap())
        });
        return border;
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
