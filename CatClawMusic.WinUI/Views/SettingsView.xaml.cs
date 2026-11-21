using System.Diagnostics;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.WinUI.Infrastructure;
using CatClawMusic.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CatClawMusic.WinUI.Views;

/// <summary>
/// 设置页（照 Android 端 LandscapeSettings）：三列分组卡。
/// 曲库偏好与诊断状态是固定的几行，用代码生成比在 XAML 里重复写更清楚。
/// </summary>
public sealed partial class SettingsView : UserControl
{
    private readonly WinUiThemeService _theme;
    private bool _syncing;

    public SettingsView(WinUiThemeService theme)
    {
        _theme = theme;
        InitializeComponent();

        DataPathText.Text = AppPaths.DataDirectory;
        LogPathText.Text = AppPaths.LogDirectory;

        var version = GetType().Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
        VersionText.Text = $"猫爪音乐 {version}";
        VersionSubtitle.Text = $"猫爪音乐 {version} · WinUI 3";

        BuildPrefs();
        BuildDiagnostics();

        Loaded += (_, _) => SyncThemeButtons();
    }

    /// <summary>三颗主题胶囊的选中态（冷灰蓝 22% 底）。</summary>
    private void SyncThemeButtons()
    {
        _syncing = true;
        var current = _theme.DarkModeSetting;
        StyleButton(ThemeFollow, current == DarkModeSetting.FollowSystem);
        StyleButton(ThemeLight, current == DarkModeSetting.Light);
        StyleButton(ThemeDark, current == DarkModeSetting.Dark);
        _syncing = false;
    }

    private static void StyleButton(Button button, bool selected)
    {
        button.Background = selected
            ? (Brush)Application.Current.Resources["AccentSoftBrush"]
            : (Brush)Application.Current.Resources["CardBackgroundBrush"];
        button.BorderBrush = selected
            ? (Brush)Application.Current.Resources["AccentBrush"]
            : (Brush)Application.Current.Resources["CardBorderBrush"];
        button.Foreground = selected
            ? (Brush)Application.Current.Resources["TextPrimaryBrush"]
            : (Brush)Application.Current.Resources["TextTertiaryBrush"];
    }

    private void OnThemeFollowClick(object sender, RoutedEventArgs e) => ApplyTheme(DarkModeSetting.FollowSystem);

    private void OnThemeLightClick(object sender, RoutedEventArgs e) => ApplyTheme(DarkModeSetting.Light);

    private void OnThemeDarkClick(object sender, RoutedEventArgs e) => ApplyTheme(DarkModeSetting.Dark);

    private void ApplyTheme(DarkModeSetting setting)
    {
        if (_syncing)
            return;

        _theme.SetDarkModeSetting(setting);
        SyncThemeButtons();
    }

    /// <summary>中列：曲库 + 播放 两组静态偏好（值暂为只读展示，与 Android 端行为一致）。</summary>
    private void BuildPrefs()
    {
        PrefsList.Children.Clear();
        PrefsList.Children.Add(SectionCaption("曲库"));

        foreach (var (label, value) in new (string, string)[]
                 {
                     ("音乐文件夹", "见「数据目录」旁的配置"),
                     ("自动扫描", "开启"),
                     ("扫描子目录", "开启"),
                     ("忽略短于", "10 秒"),
                 })
        {
            PrefsList.Children.Add(PrefRow(label, value));
        }

        PrefsList.Children.Add(SectionCaption("播放", topMargin: 14));

        foreach (var (label, value) in new (string, string)[]
                 {
                     ("播放模式", "列表循环"),
                     ("音量", "100%"),
                     ("淡入淡出", "关闭"),
                     ("桌面歌词", "按需开启"),
                 })
        {
            PrefsList.Children.Add(PrefRow(label, value));
        }
    }

    private TextBlock SectionCaption(string text, double topMargin = 0) => new()
    {
        Text = text,
        FontSize = 8,
        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
        CharacterSpacing = 130,
        Margin = new Thickness(0, topMargin, 0, 2),
        Foreground = (Brush)Application.Current.Resources["TextTertiaryBrush"],
    };

    private Border PrefRow(string label, string value)
    {
        var name = new TextBlock
        {
            Text = label,
            FontSize = 10.5,
            Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        };

        var valueText = new TextBlock
        {
            Text = value,
            FontSize = 9,
            Foreground = (Brush)Application.Current.Resources["TextTertiaryBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        };

        var grid = new Grid { Padding = new Thickness(0, 9, 0, 9) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(name, 0);
        Grid.SetColumn(valueText, 1);
        grid.Children.Add(name);
        grid.Children.Add(valueText);

        return new Border
        {
            Child = grid,
            BorderBrush = (Brush)Application.Current.Resources["DividerBrush"],
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
    }

    /// <summary>右下列：运行状态三项（数据库 / 播放内核 / 在线音源）。</summary>
    private void BuildDiagnostics()
    {
        DiagnosticsList.Children.Clear();
        DiagnosticsList.Children.Add(SectionCaption("诊断"));

        foreach (var (text, ok) in new (string, bool)[]
                 {
                     ("数据库正常", true),
                     ("播放内核就绪", true),
                     ("未接入在线音源", false),
                 })
        {
            var dot = new Border
            {
                Width = 6,
                Height = 6,
                CornerRadius = new CornerRadius(3),
                VerticalAlignment = VerticalAlignment.Center,
                Background = ok
                    ? (Brush)Application.Current.Resources["MintBrush"]
                    : (Brush)Application.Current.Resources["QuickVioletBrush"],
            };

            var label = new TextBlock
            {
                Text = text,
                FontSize = 10,
                Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            };

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 7, 0, 7),
            };
            row.Children.Add(dot);
            row.Children.Add(label);
            DiagnosticsList.Children.Add(row);
        }
    }

    private void OnOpenDataFolderClick(object sender, RoutedEventArgs e) => OpenFolder(AppPaths.DataDirectory);

    private void OnOpenLogFolderClick(object sender, RoutedEventArgs e) => OpenFolder(AppPaths.LogDirectory);

    private static void OpenFolder(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn("settings", $"打开目录失败：{ex.Message}");
        }
    }
}
