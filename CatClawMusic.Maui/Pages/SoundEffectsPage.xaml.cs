using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.Helpers;
using CatClawMusic.Maui.Services;
using CatClawMusic.Maui.Services.Equalizer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using static CatClawMusic.Maui.Controls.PopupUiHelpers;

namespace CatClawMusic.Maui.Pages;

/// <summary>
/// 全屏「音效」页面：集中放置虚拟环绕声、低音增强、响度、淡入淡出等音效设置，
/// 并提供一个「均衡器」入口，点击进入独立的均衡器页面。
/// 打开：Shell.Current.Navigation.PushModalAsync（从下往上滑入）；关闭 PopModalAsync。
/// </summary>
public partial class SoundEffectsPage : ContentPage
{
    private readonly AudioPlayerService _audioPlayer;

    public SoundEffectsPage()
    {
        InitializeComponent();
        _audioPlayer = IPlatformApplication.Current!.Services.GetRequiredService<AudioPlayerService>();
        BuildHeader();
        BuildContent();
    }

    private void AddContent(View v) => BodyStack.Add(v);

    private async Task CloseAsync() => await Navigation.PopModalAsync();

    private void BuildHeader()
    {
        var textPrimary = (Color)Application.Current!.Resources["TextPrimaryColor"];
        var primary = (Color)Application.Current!.Resources["PrimaryColor"];

        var backBtn = new Border
        {
            BackgroundColor = new Color(1, 1, 1, 0.06f),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(17) },
            StrokeThickness = 0,
            WidthRequest = 36, HeightRequest = 36,
            HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = "←", FontSize = 18, TextColor = textPrimary,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, -2, 0, 0)
            }
        };
        backBtn.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await CloseAsync()) });
        HeaderGrid.Add(backBtn, 0);

        HeaderGrid.Add(new Label
        {
            Text = "音效",
            FontSize = 24,
            FontAttributes = FontAttributes.Bold,
            TextColor = textPrimary,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(12, 0, 0, 0)
        }, 1);

        var resetLabel = new Label
        {
            Text = "重置",
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor = primary,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.End
        };
        resetLabel.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() =>
            {
                EqualizerSettings.SpatialAudioEnabled = false;
                EqualizerSettings.BassBoost = 0;
                EqualizerSettings.Loudness = 0;
                EqualizerSettings.CrossfadeEnabled = false;
                EqualizerSettings.CrossfadeDuration = 4;
                BuildContent();
                ApplyEffectsLive();
            })
        });
        HeaderGrid.Add(resetLabel, 2);
    }

    private void BuildContent()
    {
        BodyStack.Clear();

        var textHint = (Color)Application.Current!.Resources["TextHintColor"];

        // 音效设置卡片
        AddContent(SectionLabel("音效设置"));
        var card = new Border
        {
            BackgroundColor = new Color(1, 1, 1, 0.06f),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
            StrokeThickness = 0,
            Padding = new Thickness(14, 8),
            Margin = new Thickness(0, 0, 0, 22),
            Content = new VerticalStackLayout
            {
                Spacing = 0,
                Children =
                {
                    CreateToggleRow("虚拟环绕声", "3D 空间音频声场", EqualizerSettings.SpatialAudioEnabled,
                        v => { EqualizerSettings.SpatialAudioEnabled = v; ApplyEffectsLive(); },
                        margin: new Thickness(0, 0, 0, 8)),
                    CreateHSliderRow("低音增强", 0, 100, EqualizerSettings.BassBoost,
                        v => { EqualizerSettings.BassBoost = (int)v; ApplyEffectsLive(); }, v => $"{(int)v}"),
                    CreateHSliderRow("响度增益", 0, 100, EqualizerSettings.Loudness,
                        v => { EqualizerSettings.Loudness = (int)v; ApplyEffectsLive(); }, v => $"{(int)v}"),
                    CreateToggleRow("淡入淡出", "曲目间交叉淡变", EqualizerSettings.CrossfadeEnabled,
                        v => { EqualizerSettings.CrossfadeEnabled = v; },
                        margin: new Thickness(0, 8, 0, 8)),
                    CreateHSliderRow("淡入淡出时长", 0, 12, EqualizerSettings.CrossfadeDuration,
                        v => { EqualizerSettings.CrossfadeDuration = (int)v; },
                        v => $"{(int)v}s")
                }
            }
        };
        AddContent(card);

        // 均衡器入口
        AddContent(SectionLabel("均衡器"));
        var eqTitle = new Label
        {
            Text = "图形均衡器", FontSize = 15, FontAttributes = FontAttributes.Bold,
            TextColor = (Color)Application.Current!.Resources["TextPrimaryColor"],
            VerticalOptions = LayoutOptions.Center
        };
        var eqChevron = new Label
        {
            Text = "›", FontSize = 22, TextColor = textHint,
            HorizontalTextAlignment = TextAlignment.End,
            VerticalOptions = LayoutOptions.Center
        };
        var eqGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection { new() { Width = GridLength.Star }, new() { Width = GridLength.Auto } },
            ColumnSpacing = 10,
            Children = { eqTitle, eqChevron }
        };
        Grid.SetColumn(eqChevron, 1);

        var eqEntry = new Border
        {
            BackgroundColor = new Color(1, 1, 1, 0.06f),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
            StrokeThickness = 0,
            Padding = new Thickness(14, 14),
            Margin = new Thickness(0, 0, 0, 22),
            Content = eqGrid
        };
        eqEntry.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () =>
            {
                var eqPage = new EqualizerPage();
                if (Shell.Current?.Navigation is { } nav)
                    await nav.PushModalAsync(eqPage);
                else
                    await Navigation.PushModalAsync(eqPage);
            })
        });
        AddContent(eqEntry);

        // 完成按钮
        var doneBtn = CreatePopupButton("完成", true);
        doneBtn.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () => await CloseAsync())
        });
        AddContent(doneBtn);
    }

    private static Label SectionLabel(string text) => new()
    {
        Text = text,
        FontSize = 13,
        FontAttributes = FontAttributes.Bold,
        TextColor = (Color)Application.Current!.Resources["TextSecondaryColor"],
        Margin = new Thickness(2, 0, 0, 10)
    };

    private void ApplyEffectsLive()
    {
        _audioPlayer.ApplyEqualizer();
        if (EqualizerSettings.UseFFmpegEq && EqualizerSettings.Enabled && _audioPlayer.IsPlaying)
            _audioPlayer.ReapplyEqualizerLive();
    }
}
