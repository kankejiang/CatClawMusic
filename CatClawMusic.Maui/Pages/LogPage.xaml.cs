using CatClawMusic.Maui.ViewModels;
using AppTheme = Microsoft.Maui.ApplicationModel.AppTheme;

namespace CatClawMusic.Maui.Pages;

/// <summary>
/// 诊断日志页面（对应 docs/log-page-prototype.html）。
/// 展示设备信息、日志统计、按级别/标签筛选和搜索、导出诊断包。
/// </summary>
public partial class LogPage : ContentPage
{
    private readonly LogViewModel _vm;
    /// <summary>级别筛选当前选中项（all/i/w/e）</summary>
    private string _currentLevel = "all";
    /// <summary>标签筛选当前选中项（all/具体标签）</summary>
    private string _currentTag = "all";

    public LogPage(LogViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _vm = viewModel;
        BuildLevelChips();
    }

    /// <summary>页面出现时加载日志并构建标签 chips</summary>
    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.LoadLogs();
        BuildTagChips();
    }

    /// <summary>构建级别筛选 chips（全部/信息/警告/错误）</summary>
    private void BuildLevelChips()
    {
        LevelChips.Children.Clear();
        string[] levels = { "all", "i", "w", "e" };
        string[] labels = { "全部", "信息", "警告", "错误" };
        for (int i = 0; i < levels.Length; i++)
        {
            var level = levels[i];
            var chip = CreateChip(labels[i], isActive: level == "all");
            chip.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() => SelectLevel(level))
            });
            LevelChips.Children.Add(chip);
        }
    }

    /// <summary>根据 ViewModel 的 AvailableTags 动态构建标签筛选 chips</summary>
    private void BuildTagChips()
    {
        TagChips.Children.Clear();
        foreach (var tag in _vm.AvailableTags)
        {
            var chip = CreateChip(tag, isActive: tag == "全部模块");
            var capturedTag = tag;
            chip.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() => SelectTag(capturedTag))
            });
            TagChips.Children.Add(chip);
        }
    }

    /// <summary>创建一个 chip（Border + Label），根据 isActive 应用样式</summary>
    private Border CreateChip(string text, bool isActive)
    {
        var label = new Label
        {
            Text = text,
            FontSize = 12,
            FontFamily = "OpenSansSemibold",
            VerticalOptions = Center,
            HorizontalOptions = Center
        };
        var border = new Border
        {
            Padding = new(14, 7),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(999) },
            StrokeThickness = 1,
            Content = label
        };
        ApplyChipStyle(border, label, isActive);
        return border;
    }

    /// <summary>选中某个级别：更新样式并通知 ViewModel</summary>
    private void SelectLevel(string level)
    {
        _currentLevel = level;
        // 更新所有 chips 的样式
        for (int i = 0; i < LevelChips.Children.Count; i++)
        {
            if (LevelChips.Children[i] is Border b && b.Content is Label l)
            {
                string[] levels = { "all", "i", "w", "e" };
                ApplyChipStyle(b, l, isActive: levels[i] == level);
            }
        }
        _vm.SetLevelFilter(level);
    }

    /// <summary>选中某个标签：更新样式并通知 ViewModel</summary>
    private void SelectTag(string tag)
    {
        _currentTag = tag;
        foreach (var child in TagChips.Children)
        {
            if (child is Border b && b.Content is Label l)
                ApplyChipStyle(b, l, isActive: l.Text == tag);
        }
        _vm.SetTagFilter(tag);
    }

    /// <summary>应用 chip 样式：选中=渐变主色，未选中=玻璃色</summary>
    private void ApplyChipStyle(Border border, Label label, bool isActive)
    {
        if (isActive)
        {
            border.BackgroundColor = Application.Current?.RequestedTheme == AppTheme.Dark
                ? Color.FromArgb("#8C7BFF") : Color.FromArgb("#7B6AFF");
            border.Stroke = Colors.Transparent;
            label.TextColor = Colors.White;
        }
        else
        {
            border.BackgroundColor = Application.Current?.RequestedTheme == AppTheme.Dark
                ? Color.FromArgb("#1A1F3A") : Color.FromArgb("#F2F4FF");
            border.Stroke = Application.Current?.RequestedTheme == AppTheme.Dark
                ? Color.FromArgb("#2A3870") : Color.FromArgb("#E0E5F5");
            label.TextColor = Application.Current?.RequestedTheme == AppTheme.Dark
                ? Color.FromArgb("#828AB8") : Color.FromArgb("#5A6488");
        }
    }

    private static LayoutOptions Center => LayoutOptions.Center;

    private void OnBackClicked(object? sender, EventArgs e) => Shell.Current.GoToAsync("..");

    private void OnRefreshClicked(object? sender, EventArgs e)
    {
        _vm.LoadLogs();
        BuildTagChips();
    }

    private void OnScrollToTopClicked(object? sender, EventArgs e)
    {
        if (LogListView.ItemsSource != null)
            LogListView.ScrollTo(0, position: ScrollToPosition.Start, animate: true);
    }

    private void OnToggleDeviceOption(object? sender, EventArgs e) => _vm.IncludeDeviceInfo = !_vm.IncludeDeviceInfo;
    private void OnToggleStartupOption(object? sender, EventArgs e) => _vm.IncludeStartupLog = !_vm.IncludeStartupLog;
}
