using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using Android.Widget;
using CatClawMusic.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Services;

/// <summary>
/// 音效对话框 — 多页面导航结构:
/// 主页: 播放倍速、均衡器入口、MAX Audio、MISOUND(小米专属)、压限器
/// 均衡器子页: 10 段 EQ (FFmpeg anequalizer 算法) + 低音增强 + 环绕声
/// </summary>
public class SoundEffectDialog : Dialog
{
    private readonly IAudioPlayerService _playerService;
    private readonly EqBandProcessor _eqProcessor;
    private int _audioSessionId;

    // JNI 硬件效果
    private Java.Lang.Object? _bass;
    private Java.Lang.Object? _virtual;
    private Java.Lang.Object? _reverb;

    // ===== 页面容器 =====
    private LinearLayout? _mainScreen;
    private LinearLayout? _eqScreen;
    private TextView? _headerTitle;

    // UI — 均衡器
    private Switch? _eqSwitch;
    private Spinner? _presetSpinner;
    private LinearLayout? _slidersContainer;
    private readonly VerticalSliderView?[] _bandSliders = new VerticalSliderView[EqBandProcessor.Bands];
    private readonly TextView?[] _bandDbTexts = new TextView[EqBandProcessor.Bands];

    // UI — 低音增强
    private Switch? _bassSwitch;
    private SeekBar? _bassSeekBar;
    private TextView? _bassValue;

    // UI — 环绕声
    private Switch? _virSwitch;
    private SeekBar? _virSeekBar;
    private TextView? _virValue;

    // UI — 压限器
    private Switch? _compSwitch;

    // UI — MAX Audio 音乐厅氛围
    private Switch? _maxAudioSwitch;

    private bool _isInitializing = true;

    // 持久化
    private const string PrefsName = "catclaw_eq10_prefs";
    private const string KeyEqEnabled = "eq_enabled";
    private const string KeyPreset = "eq_preset";
    private const string KeyBandLevelPrefix = "eq_band_";
    private const string KeyBassEnabled = "bass_enabled";
    private const string KeyBassStrength = "bass_strength";
    private const string KeyVirEnabled = "vir_enabled";
    private const string KeyVirStrength = "vir_strength";
    private const string KeyCompEnabled = "comp_enabled";
    private const string KeyMaxAudioEnabled = "maxaudio_enabled";

    /// <summary>10段预设均衡器曲线（单位：millibels，-15dB~+15dB = -1500~+1500）
    /// 频段: 31, 62, 125, 250, 500, 1k, 2k, 4k, 8k, 16k Hz</summary>
    private static readonly IReadOnlyDictionary<string, short[]> PresetCurves = new Dictionary<string, short[]>
    {
        ["普通"] =        [    0,    0,    0,    0,    0,    0,    0,    0,    0,    0],
        ["古典"] =        [  400,  400,  300,  200, -200, -200, -100,  100,  300,  400],
        ["舞曲"] =        [  500,  400,  100,    0,    0, -200, -300, -200,    0,  200],
        ["平坦"] =        [    0,    0,    0,    0,    0,    0,    0,    0,    0,    0],
        ["民谣"] =        [  200,  300,  200,  100, -100, -200, -200, -100,  100,  200],
        ["重金属"] =      [  400,  500,  200, -300, -400, -300,  100,  300,  400,  400],
        ["嘻哈"] =        [  500,  400,  100, -100, -200,  100,  200,  100,    0,  100],
        ["爵士"] =        [  300,  200,    0,  100,  200,  200,  100,  200,  300,  300],
        ["流行"] =        [  -50,  200,  400,  300, -100, -200, -100,  200,  300,  200],
        ["摇滚"] =        [  400,  300, -200, -400, -200,  100,  300,  400,  400,  400],
        ["节奏布鲁斯"] =  [  500,  400,  100, -300, -100,  100,  200,  300,  300,  200],
        ["拉丁"] =        [  300,  200, -100, -200, -200, -100,  100,  200,  300,  400],
        ["乡村"] =        [  200,  200,  100, -100, -200, -200, -100,  100,  200,  200],
        ["原声"] =        [  200,  300,  200,  100,  100,  100,  100,  200,  300,  300],
        ["人声增强"] =    [ -200, -100,  200,  400,  400,  300,  200,  100,    0, -100],
        ["低音增强"] =    [  600,  500,  300,  100,    0,    0,    0,    0,    0,    0],
        ["高音增强"] =    [    0,    0,    0,    0,    0,    0,  200,  400,  500,  600],
        ["响度"] =        [  400,  300,  100,    0,    0,    0,    0,  100,  200,  300],
        ["电子"] =        [  400,  300,    0, -200, -400, -200,  100,  300,  400,  400],
        ["小扬声器"] =    [  400,  300,  200,  100, -100, -200, -100,  100,  200,  300],
        ["现场"] =        [ -100,  200,  300,  200,  100,  100,  200,  300,  200,  100],
    };

    private static readonly List<string> PresetNames = new(PresetCurves.Keys);

    static SoundEffectDialog()
    {
        PresetNames.Sort((a, b) =>
        {
            if (a == "普通") return -1;
            if (b == "普通") return 1;
            return string.Compare(a, b, StringComparison.Ordinal);
        });
    }

    public SoundEffectDialog(Activity context, IAudioPlayerService playerService)
        : base(context, Android.Resource.Style.ThemeOverlayMaterialDark)
    {
        _playerService = playerService;
        _eqProcessor = MainApplication.Services.GetRequiredService<EqBandProcessor>();
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        RequestWindowFeature((int)WindowFeatures.NoTitle);
        SetContentView(CreateView());
        Window?.SetLayout(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        Window?.SetGravity(GravityFlags.Bottom);
        Window?.SetBackgroundDrawable(new ColorDrawable(Color.Transparent));
        Init();
    }

    #region View Creation

    private int Dp(int dp) => (int)(dp * (Context.Resources?.DisplayMetrics?.Density ?? 1f));

    private View CreateView()
    {
        var density = Context.Resources?.DisplayMetrics?.Density ?? 1f;
        int dp(int v) => (int)(v * density);

        var root = new LinearLayout(Context)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        root.SetBackgroundColor(Color.ParseColor("#E6222222"));

        // ===== Header =====
        var header = new LinearLayout(Context)
        {
            Orientation = Orientation.Horizontal,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        header.SetGravity(GravityFlags.CenterVertical);
        header.SetPadding(dp(16), dp(14), dp(16), dp(6));

        // Back button (hidden on main screen)
        var backBtn = new TextView(Context)
        {
            Text = "\u2039 ",
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent),
            Visibility = ViewStates.Gone,
        };
        backBtn.SetTextColor(Color.White);
        backBtn.TextSize = 24f;
        backBtn.Gravity = GravityFlags.Center;
        backBtn.SetPadding(dp(0), dp(0), dp(8), dp(0));
        backBtn.Click += (s, e) => NavigateToMain();
        header.AddView(backBtn);
        this._backBtn = backBtn;

        _headerTitle = new TextView(Context)
        {
            Text = "音效",
            LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f)
        };
        _headerTitle.SetTextColor(Color.White);
        _headerTitle.TextSize = 20f;
        _headerTitle.SetTypeface(null, TypefaceStyle.Bold);

        var closeBtn = new TextView(Context)
        {
            Text = "\u2715",
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
        };
        closeBtn.SetTextColor(Color.ParseColor("#AAFFFFFF"));
        closeBtn.TextSize = 18f;
        closeBtn.Gravity = GravityFlags.Center;
        closeBtn.SetPadding(dp(12), dp(4), dp(4), dp(4));
        closeBtn.Click += (s, e) => Dismiss();

        header.AddView(_headerTitle);
        header.AddView(closeBtn);
        root.AddView(header);

        // ===== MAIN SCREEN =====
        _mainScreen = new LinearLayout(Context)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };

        var mainScroll = new ScrollView(Context)
        {
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        var mainContent = new LinearLayout(Context)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new ScrollView.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        mainContent.SetPadding(0, 0, 0, dp(16));

        // --- Playback Speed ---
        mainContent.AddView(MakeSpeedSection(dp));

        mainContent.AddView(MakeDivider(dp));

        // --- EQ Entry ---
        mainContent.AddView(MakeMenuEntry("\u266B", "均衡器", "10 Band Equalizer",
            (s, e) => NavigateToEq()));

        // --- MAX Audio 音乐厅氛围 ---
        mainContent.AddView(MakeMaxAudioSection(dp));

        // --- MISOUND (仅小米手机显示) ---
        if (IsXiaomiDevice())
        {
            mainContent.AddView(MakeMenuEntry("\u2669", "MISOUND", "Xiaomi Audio Effect",
                (s, e) => LaunchMiSound()));
        }

        mainContent.AddView(MakeDivider(dp));

        // --- Compressor ---
        mainContent.AddView(MakeCompressorSection(dp));

        mainContent.AddView(MakeDivider(dp));

        // --- Footer ---
        var footer = new TextView(Context)
        {
            Text = "软件已适配 Poweramp Equalizer 等音效处理软件",
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        footer.SetTextColor(Color.ParseColor("#66FFFFFF"));
        footer.TextSize = 11f;
        footer.Gravity = GravityFlags.Center;
        footer.SetPadding(dp(16), dp(8), dp(16), dp(8));
        mainContent.AddView(footer);

        mainScroll.AddView(mainContent);
        _mainScreen.AddView(mainScroll);
        root.AddView(_mainScreen);

        // ===== EQ SCREEN (initially hidden) =====
        _eqScreen = new LinearLayout(Context)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent),
            Visibility = ViewStates.Gone
        };

        var eqScroll = new ScrollView(Context)
        {
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        var eqContent = new LinearLayout(Context)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new ScrollView.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        eqContent.SetPadding(0, 0, 0, dp(16));

        // EQ section
        eqContent.AddView(MakeSectionHeader("均衡器", out _eqSwitch));

        // Preset row
        var presetRow = new LinearLayout(Context)
        {
            Orientation = Orientation.Horizontal,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        presetRow.SetGravity(GravityFlags.CenterVertical);
        presetRow.SetPadding(dp(16), 0, dp(16), dp(4));

        var userLabel = new TextView(Context)
        {
            Text = "用户 ",
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
        };
        userLabel.SetTextColor(Color.ParseColor("#88FFFFFF"));
        userLabel.TextSize = 13f;
        presetRow.AddView(userLabel);

        _presetSpinner = new Spinner(Context)
        {
            LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f)
        };
        _presetSpinner.ItemSelected += OnPresetSelected;
        presetRow.AddView(_presetSpinner);
        eqContent.AddView(presetRow);

        // 10-band sliders (horizontal scroll)
        var hScrollView = new HorizontalScrollView(Context)
        {
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, dp(210))
        };
        _slidersContainer = new LinearLayout(Context)
        {
            Orientation = Orientation.Horizontal,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.MatchParent)
        };
        _slidersContainer.SetGravity(GravityFlags.CenterHorizontal);
        hScrollView.AddView(_slidersContainer);
        eqContent.AddView(hScrollView);

        _eqSwitch.SetOnCheckedChangeListener(new EqSwitchListener(this));

        // dB Scale labels
        var scaleRow = new LinearLayout(Context)
        {
            Orientation = Orientation.Horizontal,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        scaleRow.SetPadding(dp(16), 0, dp(16), dp(4));
        scaleRow.SetGravity(GravityFlags.CenterVertical);
        AddScaleLabel(scaleRow, "+15 dB", GravityFlags.Start, 1f);
        AddScaleLabel(scaleRow, "0 dB", GravityFlags.Center, 1f);
        AddScaleLabel(scaleRow, "-15 dB", GravityFlags.End, 1f);
        eqContent.AddView(scaleRow);

        eqContent.AddView(MakeDivider(dp));

        // Bass Boost
        eqContent.AddView(MakeSectionHeader("低音增强", out _bassSwitch));

        var bassRow = new LinearLayout(Context)
        {
            Orientation = Orientation.Horizontal,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        bassRow.SetGravity(GravityFlags.CenterVertical);
        bassRow.SetPadding(dp(16), 0, dp(16), dp(8));

        _bassSeekBar = new SeekBar(Context)
        {
            LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f),
            Max = 1000
        };
        _bassSeekBar.SetOnSeekBarChangeListener(new BassSeekBarListener(this));
        _bassValue = new TextView(Context)
        {
            LayoutParameters = new LinearLayout.LayoutParams(dp(50), ViewGroup.LayoutParams.WrapContent)
        };
        _bassValue.SetTextColor(Color.White);
        _bassValue.TextSize = 13f;
        _bassValue.Gravity = GravityFlags.Center;
        _bassValue.Text = "0%";

        bassRow.AddView(_bassSeekBar);
        bassRow.AddView(_bassValue);
        eqContent.AddView(bassRow);
        _bassSwitch.SetOnCheckedChangeListener(new BassSwitchListener(this));

        // Virtualizer
        eqContent.AddView(MakeSectionHeader("环绕声", out _virSwitch));

        var virRow = new LinearLayout(Context)
        {
            Orientation = Orientation.Horizontal,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        virRow.SetGravity(GravityFlags.CenterVertical);
        virRow.SetPadding(dp(16), 0, dp(16), dp(8));

        _virSeekBar = new SeekBar(Context)
        {
            LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f),
            Max = 1000
        };
        _virSeekBar.SetOnSeekBarChangeListener(new VirSeekBarListener(this));
        _virValue = new TextView(Context)
        {
            LayoutParameters = new LinearLayout.LayoutParams(dp(50), ViewGroup.LayoutParams.WrapContent)
        };
        _virValue.SetTextColor(Color.White);
        _virValue.TextSize = 13f;
        _virValue.Gravity = GravityFlags.Center;
        _virValue.Text = "0%";

        virRow.AddView(_virSeekBar);
        virRow.AddView(_virValue);
        eqContent.AddView(virRow);
        _virSwitch.SetOnCheckedChangeListener(new VirSwitchListener(this));

        eqScroll.AddView(eqContent);
        _eqScreen.AddView(eqScroll);
        root.AddView(_eqScreen);

        return root;
    }

    #region Menu Entry Builder

    private LinearLayout MakeMenuEntry(string icon, string title, string subtitle, EventHandler onClick)
    {
        int dp(int v) => (int)(v * (Context.Resources?.DisplayMetrics?.Density ?? 1f));

        var row = new LinearLayout(Context)
        {
            Orientation = Orientation.Horizontal,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent),
            Clickable = true,
            Focusable = true
        };
        row.SetGravity(GravityFlags.CenterVertical);
        row.SetPadding(dp(16), dp(14), dp(16), dp(14));

        var iconTv = new TextView(Context)
        {
            Text = icon,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
        };
        iconTv.SetTextColor(Color.ParseColor("#88FFFFFF"));
        iconTv.TextSize = 18f;
        iconTv.Gravity = GravityFlags.Center;
        iconTv.SetPadding(dp(0), dp(0), dp(12), dp(0));

        var textContainer = new LinearLayout(Context)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f)
        };

        var titleTv = new TextView(Context)
        {
            Text = title,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
        };
        titleTv.SetTextColor(Color.White);
        titleTv.TextSize = 15f;
        textContainer.AddView(titleTv);

        var subTv = new TextView(Context)
        {
            Text = subtitle,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
        };
        subTv.SetTextColor(Color.ParseColor("#88FFFFFF"));
        subTv.TextSize = 11f;
        textContainer.AddView(subTv);

        var chevron = new TextView(Context)
        {
            Text = "\u203A",
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
        };
        chevron.SetTextColor(Color.ParseColor("#88FFFFFF"));
        chevron.TextSize = 22f;
        chevron.Gravity = GravityFlags.Center;

        row.AddView(iconTv);
        row.AddView(textContainer);
        row.AddView(chevron);
        row.Click += onClick;

        return row;
    }

    #endregion

    #region Speed Section

    private LinearLayout MakeSpeedSection(Func<int, int> dp)
    {
        var container = new LinearLayout(Context)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };

        var label = new TextView(Context)
        {
            Text = "播放倍速",
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        label.SetTextColor(Color.White);
        label.TextSize = 15f;
        label.SetTypeface(null, TypefaceStyle.Bold);
        label.SetPadding(dp(16), dp(10), dp(16), dp(6));
        container.AddView(label);

        var speedRow = new HorizontalScrollView(Context)
        {
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent),
            HorizontalScrollBarEnabled = false
        };
        var speedContainer = new LinearLayout(Context)
        {
            Orientation = Orientation.Horizontal,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
        };
        speedContainer.SetPadding(dp(12), dp(4), dp(12), dp(8));

        float[] speeds = [0.25f, 0.5f, 0.75f, 0.8f, 0.9f, 0.95f, 1.0f];
        foreach (var speed in speeds)
        {
            var btn = new TextView(Context)
            {
                Text = $"x{speed:0.##}",
                LayoutParameters = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
            };
            btn.SetPadding(dp(10), dp(6), dp(10), dp(6));
            btn.Gravity = GravityFlags.Center;
            btn.TextSize = 13f;

            var layoutParams = btn.LayoutParameters as LinearLayout.LayoutParams;
            layoutParams!.SetMargins(dp(3), 0, dp(3), 0);
            btn.LayoutParameters = layoutParams;

            var currentSpeed = GetCurrentPlaybackSpeed();
            bool isSelected = Math.Abs(speed - currentSpeed) < 0.01f;
            UpdateSpeedButtonStyle(btn, isSelected);

            var s = speed;
            btn.Click += (sender, e) =>
            {
                SetPlaybackSpeed(s);
                // Update all speed button styles
                for (int i = 0; i < speedContainer.ChildCount; i++)
                {
                    if (speedContainer.GetChildAt(i) is TextView tv)
                    {
                        bool sel = tv.Text == $"x{s:0.##}";
                        UpdateSpeedButtonStyle(tv, sel);
                    }
                }
            };

            speedContainer.AddView(btn);
        }

        speedRow.AddView(speedContainer);
        container.AddView(speedRow);
        return container;
    }

    private void UpdateSpeedButtonStyle(TextView btn, bool selected)
    {
        if (selected)
        {
            btn.SetBackgroundColor(Color.ParseColor("#2196F3"));
            btn.SetTextColor(Color.White);
        }
        else
        {
            var gd = new GradientDrawable();
            gd.SetColor(Color.ParseColor("#33FFFFFF"));
            gd.SetCornerRadius(Dp(16));
            btn.Background = gd;
            btn.SetTextColor(Color.ParseColor("#CCFFFFFF"));
        }
    }

    private float GetCurrentPlaybackSpeed()
    {
        try
        {
            var speedProp = _playerService.GetType().GetProperty("PlaybackSpeed");
            if (speedProp != null)
                return (float)(speedProp.GetValue(_playerService) ?? 1.0f);
        }
        catch { }
        return 1.0f;
    }

    private void SetPlaybackSpeed(float speed)
    {
        try
        {
            var method = _playerService.GetType().GetMethod("SetPlaybackSpeed");
            method?.Invoke(_playerService, new object[] { speed });
        }
        catch (Exception ex)
        {
            Android.Util.Log.Warn("CatClaw.SFX", $"设置播放速度失败: {ex.Message}");
        }
    }

    #endregion

    #region Compressor Section

    private LinearLayout MakeCompressorSection(Func<int, int> dp)
    {
        var container = new LinearLayout(Context)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };

        var row = new LinearLayout(Context)
        {
            Orientation = Orientation.Horizontal,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        row.SetGravity(GravityFlags.CenterVertical);
        row.SetPadding(dp(16), dp(10), dp(16), 0);

        var titleTv = new TextView(Context)
        {
            Text = "压限器 (Compressor)",
            LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f)
        };
        titleTv.SetTextColor(Color.White);
        titleTv.TextSize = 15f;
        titleTv.SetTypeface(null, TypefaceStyle.Bold);

        _compSwitch = new Switch(Context)
        {
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
        };
        _compSwitch.SetOnCheckedChangeListener(new CompSwitchListener(this));

        row.AddView(titleTv);
        row.AddView(_compSwitch);
        container.AddView(row);

        var desc = new TextView(Context)
        {
            Text = "控制电平，使音乐响亮的部分更安静，而安静的部分更响亮",
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        desc.SetTextColor(Color.ParseColor("#88FFFFFF"));
        desc.TextSize = 12f;
        desc.SetPadding(dp(16), dp(4), dp(16), dp(8));
        container.AddView(desc);

        return container;
    }

    #endregion

    #region UI Helpers

    private TextView? _backBtn;

    private LinearLayout MakeSectionHeader(string title, out Switch sw)
    {
        int dp(int v) => (int)(v * (Context.Resources?.DisplayMetrics?.Density ?? 1f));
        var row = new LinearLayout(Context)
        {
            Orientation = Orientation.Horizontal,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        row.SetGravity(GravityFlags.CenterVertical);
        row.SetPadding(dp(16), dp(10), dp(16), 0);

        var tv = new TextView(Context)
        {
            Text = title,
            LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f)
        };
        tv.SetTextColor(Color.White);
        tv.TextSize = 15f;
        tv.SetTypeface(null, TypefaceStyle.Bold);

        sw = new Switch(Context)
        {
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
        };

        row.AddView(tv);
        row.AddView(sw);
        return row;
    }

    private View MakeDivider(Func<int, int> dp)
    {
        var divider = new View(Context)
        {
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, dp(1))
        };
        divider.SetBackgroundColor(Color.ParseColor("#22FFFFFF"));
        return divider;
    }

    private void AddScaleLabel(LinearLayout parent, string text, GravityFlags gravity, float weight)
    {
        var tv = new TextView(Context)
        {
            Text = text,
            LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, weight)
        };
        tv.SetTextColor(Color.ParseColor("#66FFFFFF"));
        tv.TextSize = 10f;
        tv.Gravity = gravity;
        parent.AddView(tv);
    }

    #endregion

    #endregion

    #region Navigation

    private void NavigateToEq()
    {
        _mainScreen!.Visibility = ViewStates.Gone;
        _eqScreen!.Visibility = ViewStates.Visible;
        _backBtn!.Visibility = ViewStates.Visible;
        _headerTitle!.Text = "均衡器";
    }

    private void NavigateToMain()
    {
        _eqScreen!.Visibility = ViewStates.Gone;
        _mainScreen!.Visibility = ViewStates.Visible;
        _backBtn!.Visibility = ViewStates.Gone;
        _headerTitle!.Text = "音效";
    }

    public override void OnBackPressed()
    {
        if (_eqScreen?.Visibility == ViewStates.Visible)
        {
            NavigateToMain();
        }
        else
        {
            base.OnBackPressed();
        }
    }

    #endregion

    #region JNI Helpers (BassBoost / Virtualizer)

    private static Java.Lang.Class? _bassClass, _virClass, _reverbClass;

    private static Java.Lang.Class BassClass =>
        _bassClass ??= Java.Lang.Class.ForName("android.media.audiofx.BassBoost");
    private static Java.Lang.Class VirClass =>
        _virClass ??= Java.Lang.Class.ForName("android.media.audiofx.Virtualizer");
    private static Java.Lang.Class ReverbClass =>
        _reverbClass ??= Java.Lang.Class.ForName("android.media.audiofx.EnvironmentalReverb");

    private static Java.Lang.Object NewEffect(Java.Lang.Class cls, int priority, int sessionId)
    {
        var ctor = cls.GetConstructor(Java.Lang.Integer.Type, Java.Lang.Integer.Type);
        return ctor.NewInstance(Java.Lang.Integer.ValueOf(priority), Java.Lang.Integer.ValueOf(sessionId));
    }

    private static void JniCallShort(Java.Lang.Object? obj, Java.Lang.Class cls, string name, short arg)
    {
        var method = cls.GetMethod(name, Java.Lang.Short.Type);
        method.Invoke(obj, Java.Lang.Short.ValueOf(arg));
    }

    private static void JniCallBool(Java.Lang.Object? obj, Java.Lang.Class cls, string name, bool arg)
    {
        var method = cls.GetMethod(name, Java.Lang.Boolean.Type);
        method.Invoke(obj, Java.Lang.Boolean.ValueOf(arg));
    }

    private static void JniCallShortParam(Java.Lang.Object? obj, Java.Lang.Class cls, string name, short arg)
    {
        var method = cls.GetMethod(name, Java.Lang.Short.Type);
        method.Invoke(obj, Java.Lang.Short.ValueOf(arg));
    }

    private void BassSetEnabled(bool enabled) => JniCallBool(_bass, BassClass, "setEnabled", enabled);
    private void BassSetStrength(short strength) => JniCallShort(_bass, BassClass, "setStrength", strength);
    private void VirSetEnabled(bool enabled) => JniCallBool(_virtual, VirClass, "setEnabled", enabled);
    private void VirSetStrength(short strength) => JniCallShort(_virtual, VirClass, "setStrength", strength);

    private void ReverbSetEnabled(bool enabled) => JniCallBool(_reverb, ReverbClass, "setEnabled", enabled);

    /// <summary>应用音乐厅混响参数</summary>
    private void ApplyConcertHallPreset()
    {
        if (_reverb == null) return;
        try { JniCallShortParam(_reverb, ReverbClass, "setRoom", -600); } catch { }
        try { JniCallShortParam(_reverb, ReverbClass, "setRoomHF", -800); } catch { }
        try { JniCallShortParam(_reverb, ReverbClass, "setDecayTime", 3000); } catch { }
        try { JniCallShortParam(_reverb, ReverbClass, "setDiffusion", 800); } catch { }
        try { JniCallShortParam(_reverb, ReverbClass, "setDensity", 600); } catch { }
    }

    #endregion

    #region Initialization

    private void Init()
    {
        try
        {
            _audioSessionId = _playerService.AudioSessionId;

            // 创建 BassBoost / Virtualizer
            if (_audioSessionId > 0)
            {
                try { _bass = NewEffect(BassClass, 0, _audioSessionId); } catch { }
                try { _virtual = NewEffect(VirClass, 0, _audioSessionId); } catch { }
                try { _reverb = NewEffect(ReverbClass, 0, _audioSessionId); } catch { }
            }

            // 创建 10 段滑块 UI
            CreateBandSliders();

            // 填充预设列表
            FillPresets();

            // 恢复设置
            _isInitializing = true;
            var prefs = Context.GetSharedPreferences(PrefsName, FileCreationMode.Private);

            // 均衡器
            bool eqEnabled = prefs.GetBoolean(KeyEqEnabled, false);
            _eqSwitch!.Checked = eqEnabled;
            _eqProcessor.Enabled = eqEnabled;

            int savedPreset = prefs.GetInt(KeyPreset, -1);
            if (savedPreset >= 0 && savedPreset < PresetNames.Count)
            {
                _presetSpinner!.SetSelection(savedPreset + 1); // +1 for "自定义"
                var presetName = PresetNames[savedPreset];
                if (PresetCurves.TryGetValue(presetName, out var curve))
                    ApplyCurve(curve);
            }
            else if (eqEnabled)
            {
                RestoreBandLevels();
            }

            // 低音增强
            _bassSwitch!.Checked = prefs.GetBoolean(KeyBassEnabled, false);
            if (_bass != null) BassSetEnabled(_bassSwitch.Checked);
            _bassSeekBar!.Progress = prefs.GetInt(KeyBassStrength, 0);
            _bassValue!.Text = $"{_bassSeekBar.Progress / 10}%";
            if (_bassSwitch.Checked && _bass != null) BassSetStrength((short)_bassSeekBar.Progress);

            // 环绕声
            _virSwitch!.Checked = prefs.GetBoolean(KeyVirEnabled, false);
            if (_virtual != null) VirSetEnabled(_virSwitch.Checked);
            _virSeekBar!.Progress = prefs.GetInt(KeyVirStrength, 0);
            _virValue!.Text = $"{_virSeekBar.Progress / 10}%";
            if (_virSwitch.Checked && _virtual != null) VirSetStrength((short)_virSeekBar.Progress);

            // 压限器
            _compSwitch!.Checked = prefs.GetBoolean(KeyCompEnabled, false);

            // MAX Audio 音乐厅氛围
            bool maxAudioEnabled = prefs.GetBoolean(KeyMaxAudioEnabled, false);
            _maxAudioSwitch!.Checked = maxAudioEnabled;
            if (maxAudioEnabled && _reverb != null)
            {
                ReverbSetEnabled(true);
                ApplyConcertHallPreset();
            }

            _isInitializing = false;
        }
        catch (Exception ex)
        {
            Android.Util.Log.Warn("CatClaw.SFX", $"音效初始化失败: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private void CreateBandSliders()
    {
        if (_slidersContainer == null) return;

        for (int i = 0; i < EqBandProcessor.Bands; i++)
        {
            var bandLayout = new LinearLayout(Context)
            {
                Orientation = Orientation.Vertical,
                LayoutParameters = new LinearLayout.LayoutParams(Dp(38), ViewGroup.LayoutParams.MatchParent)
            };
            bandLayout.SetGravity(GravityFlags.CenterHorizontal);
            bandLayout.SetPadding(Dp(3), Dp(2), Dp(3), Dp(2));

            // 频率标签
            int freqHz = EqBandProcessor.Freqs[i];
            string freqLabel = freqHz >= 1000 ? $"{freqHz / 1000}k" : $"{freqHz}";
            var freqText = new TextView(Context)
            {
                Text = freqLabel,
                LayoutParameters = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
            };
            freqText.SetTextColor(Color.ParseColor("#AAFFFFFF"));
            freqText.TextSize = 9f;
            freqText.Gravity = GravityFlags.Center;

            // 垂直滑块
            var slider = new VerticalSliderView(Context)
            {
                LayoutParameters = new LinearLayout.LayoutParams(Dp(32), 0, 1f),
                Min = -1500,
                Max = 1500,
                Value = 0
            };
            var bandIdx = i;
            slider.ValueChanged += (s, v) => OnBandSliderChanged(bandIdx, (short)(int)v);

            // dB 值标签
            var dbText = new TextView(Context)
            {
                Text = "0.0",
                LayoutParameters = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
            };
            dbText.SetTextColor(Color.White);
            dbText.TextSize = 9f;
            dbText.Gravity = GravityFlags.Center;

            _bandSliders[i] = slider;
            _bandDbTexts[i] = dbText;

            bandLayout.AddView(freqText);
            bandLayout.AddView(slider);
            bandLayout.AddView(dbText);
            _slidersContainer.AddView(bandLayout);
        }
    }

    private void FillPresets()
    {
        if (_presetSpinner == null) return;

        var items = new List<string> { "自定义" };
        items.AddRange(PresetNames);

        var adapter = new ArrayAdapter<string>(Context,
            Android.Resource.Layout.SimpleSpinnerDropDownItem, items);
        _presetSpinner.Adapter = adapter;
    }

    #endregion

    #region Band Slider Logic

    private void OnBandSliderChanged(int band, short millibels)
    {
        if (_isInitializing) return;

        _eqProcessor.SetBandLevelMillibels(band, millibels);

        if (_bandDbTexts[band] != null)
        {
            float db = millibels / 100f;
            _bandDbTexts[band]!.Text = $"{db:+0.0;-0.0;0.0}";
        }

        // 切到"自定义"
        if (_presetSpinner != null && _presetSpinner.SelectedItemPosition != 0)
        {
            _isInitializing = true;
            _presetSpinner.SetSelection(0);
            _isInitializing = false;
        }

        SaveSettings();
    }

    private void ApplyCurve(short[] curve)
    {
        for (int i = 0; i < EqBandProcessor.Bands && i < curve.Length; i++)
        {
            _eqProcessor.SetBandLevelMillibels(i, curve[i]);
            if (_bandSliders[i] != null)
                _bandSliders[i]!.Value = curve[i];
            if (_bandDbTexts[i] != null)
            {
                float db = curve[i] / 100f;
                _bandDbTexts[i]!.Text = $"{db:+0.0;-0.0;0.0}";
            }
        }
    }

    private void UpdateSlidersFromProcessor()
    {
        for (int i = 0; i < EqBandProcessor.Bands; i++)
        {
            var millibels = _eqProcessor.GetBandLevelMillibels(i);
            if (_bandSliders[i] != null)
                _bandSliders[i]!.Value = millibels;
            if (_bandDbTexts[i] != null)
            {
                float db = millibels / 100f;
                _bandDbTexts[i]!.Text = $"{db:+0.0;-0.0;0.0}";
            }
        }
    }

    #endregion

    #region Event Handlers

    private void OnPresetSelected(object? sender, AdapterView.ItemSelectedEventArgs e)
    {
        if (_isInitializing) return;

        int pos = e.Position;
        if (pos == 0) return; // "自定义"

        var presetName = PresetNames[pos - 1];
        if (PresetCurves.TryGetValue(presetName, out var curve))
        {
            _eqSwitch!.Checked = true;
            _eqProcessor.Enabled = true;
            ApplyCurve(curve);
            SaveSettings();
        }
    }

    // === Switch / SeekBar Listeners ===

    private class EqSwitchListener : Java.Lang.Object, CompoundButton.IOnCheckedChangeListener
    {
        private readonly WeakReference<SoundEffectDialog> _ref;
        public EqSwitchListener(SoundEffectDialog d) => _ref = new(d);
        public void OnCheckedChanged(CompoundButton? b, bool enabled)
        {
            if (!_ref.TryGetTarget(out var d)) return;
            d._eqProcessor.Enabled = enabled;
            d.SaveSettings();
        }
    }

    private class BassSwitchListener : Java.Lang.Object, CompoundButton.IOnCheckedChangeListener
    {
        private readonly WeakReference<SoundEffectDialog> _ref;
        public BassSwitchListener(SoundEffectDialog d) => _ref = new(d);
        public void OnCheckedChanged(CompoundButton? b, bool enabled)
        {
            if (!_ref.TryGetTarget(out var d) || d._bass == null) return;
            d.BassSetEnabled(enabled);
            if (enabled) d.BassSetStrength((short)d._bassSeekBar!.Progress);
            d.SaveSettings();
        }
    }

    private class VirSwitchListener : Java.Lang.Object, CompoundButton.IOnCheckedChangeListener
    {
        private readonly WeakReference<SoundEffectDialog> _ref;
        public VirSwitchListener(SoundEffectDialog d) => _ref = new(d);
        public void OnCheckedChanged(CompoundButton? b, bool enabled)
        {
            if (!_ref.TryGetTarget(out var d) || d._virtual == null) return;
            d.VirSetEnabled(enabled);
            if (enabled) d.VirSetStrength((short)d._virSeekBar!.Progress);
            d.SaveSettings();
        }
    }

    private class CompSwitchListener : Java.Lang.Object, CompoundButton.IOnCheckedChangeListener
    {
        private readonly WeakReference<SoundEffectDialog> _ref;
        public CompSwitchListener(SoundEffectDialog d) => _ref = new(d);
        public void OnCheckedChanged(CompoundButton? b, bool enabled)
        {
            if (!_ref.TryGetTarget(out var d)) return;
            d.SaveSettings();
        }
    }

    private class MaxAudioSwitchListener : Java.Lang.Object, CompoundButton.IOnCheckedChangeListener
    {
        private readonly WeakReference<SoundEffectDialog> _ref;
        public MaxAudioSwitchListener(SoundEffectDialog d) => _ref = new(d);
        public void OnCheckedChanged(CompoundButton? b, bool enabled)
        {
            if (!_ref.TryGetTarget(out var d) || d._isInitializing) return;
            if (d._reverb != null)
            {
                d.ReverbSetEnabled(enabled);
                if (enabled) d.ApplyConcertHallPreset();
            }
            d.SaveSettings();
        }
    }

    private class BassSeekBarListener : Java.Lang.Object, SeekBar.IOnSeekBarChangeListener
    {
        private readonly WeakReference<SoundEffectDialog> _ref;
        public BassSeekBarListener(SoundEffectDialog d) => _ref = new(d);
        public void OnProgressChanged(SeekBar? sb, int p, bool fromUser)
        {
            if (!_ref.TryGetTarget(out var d) || d._bass == null) return;
            d._bassValue!.Text = $"{p / 10}%";
            if (fromUser && d._bassSwitch!.Checked) d.BassSetStrength((short)p);
            if (fromUser) d.SaveSettings();
        }
        public void OnStartTrackingTouch(SeekBar? sb) { }
        public void OnStopTrackingTouch(SeekBar? sb) { }
    }

    private class VirSeekBarListener : Java.Lang.Object, SeekBar.IOnSeekBarChangeListener
    {
        private readonly WeakReference<SoundEffectDialog> _ref;
        public VirSeekBarListener(SoundEffectDialog d) => _ref = new(d);
        public void OnProgressChanged(SeekBar? sb, int p, bool fromUser)
        {
            if (!_ref.TryGetTarget(out var d) || d._virtual == null) return;
            d._virValue!.Text = $"{p / 10}%";
            if (fromUser && d._virSwitch!.Checked) d.VirSetStrength((short)p);
            if (fromUser) d.SaveSettings();
        }
        public void OnStartTrackingTouch(SeekBar? sb) { }
        public void OnStopTrackingTouch(SeekBar? sb) { }
    }

    #endregion

    #region External Audio Effects


    private LinearLayout MakeMaxAudioSection(Func<int, int> dp)
    {
        var container = new LinearLayout(Context)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };

        var row = new LinearLayout(Context)
        {
            Orientation = Orientation.Horizontal,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        row.SetGravity(GravityFlags.CenterVertical);
        row.SetPadding(dp(16), dp(10), dp(16), 0);

        var titleTv = new TextView(Context)
        {
            Text = "MAX Audio 音乐厅氛围",
            LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f)
        };
        titleTv.SetTextColor(Color.White);
        titleTv.TextSize = 15f;
        titleTv.SetTypeface(null, TypefaceStyle.Bold);

        _maxAudioSwitch = new Switch(Context)
        {
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
        };
        _maxAudioSwitch.SetOnCheckedChangeListener(new MaxAudioSwitchListener(this));

        row.AddView(titleTv);
        row.AddView(_maxAudioSwitch);
        container.AddView(row);

        var desc = new TextView(Context)
        {
            Text = "沉浸式音乐厅混响效果，增强空间感与临场感",
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        desc.SetTextColor(Color.ParseColor("#88FFFFFF"));
        desc.TextSize = 12f;
        desc.SetPadding(dp(16), dp(4), dp(16), dp(8));
        container.AddView(desc);

        return container;
    }

    private static bool IsXiaomiDevice()
    {
        var manufacturer = Android.OS.Build.Manufacturer ?? "";
        return manufacturer.Equals("Xiaomi", StringComparison.OrdinalIgnoreCase);
    }

    private void LaunchMiSound()
    {
        try
        {
            // 尝试跳转到小米系统音质音效设置页
            var intents = new[]
            {
                new Intent("miui.intent.action.MISOUND_MAIN"),
                new Intent("miui.intent.action.SOUND_EFFECT"),
                new Intent(Android.Provider.Settings.ActionSettings)
                    .PutExtra(":android:show_fragment", "com.miui.misound"),
            };

            foreach (var intent in intents)
            {
                if (intent.ResolveActivity(Context.PackageManager!) != null)
                {
                    intent.SetFlags(ActivityFlags.NewTask);
                    Context.StartActivity(intent);
                    return;
                }
            }

            // 兜底：尝试直接启动 misound 包
            var launchIntent = Context.PackageManager!
                .GetLaunchIntentForPackage("com.miui.misound");
            if (launchIntent != null)
            {
                launchIntent.SetFlags(ActivityFlags.NewTask);
                Context.StartActivity(launchIntent);
            }
            else
            {
                Toast.MakeText(Context, "未找到音质音效设置", ToastLength.Short)?.Show();
            }
        }
        catch (Exception ex)
        {
            Android.Util.Log.Warn("CatClaw.SFX", $"打开 MiSound 失败: {ex.Message}");
            Toast.MakeText(Context, "打开 MiSound 失败", ToastLength.Short)?.Show();
        }
    }

    #endregion

    #region Persistence

    private void SaveSettings()
    {
        try
        {
            var prefs = Context.GetSharedPreferences(PrefsName, FileCreationMode.Private);
            var editor = prefs.Edit();
            editor.PutBoolean(KeyEqEnabled, _eqSwitch?.Checked ?? false);
            editor.PutInt(KeyPreset, (_presetSpinner?.SelectedItemPosition ?? 0) - 1);
            editor.PutBoolean(KeyBassEnabled, _bassSwitch?.Checked ?? false);
            editor.PutInt(KeyBassStrength, _bassSeekBar?.Progress ?? 0);
            editor.PutBoolean(KeyVirEnabled, _virSwitch?.Checked ?? false);
            editor.PutInt(KeyVirStrength, _virSeekBar?.Progress ?? 0);
            editor.PutBoolean(KeyCompEnabled, _compSwitch?.Checked ?? false);
            editor.PutBoolean(KeyMaxAudioEnabled, _maxAudioSwitch?.Checked ?? false);

            for (int i = 0; i < EqBandProcessor.Bands; i++)
                editor.PutInt(KeyBandLevelPrefix + i, _eqProcessor.GetBandLevelMillibels(i));

            editor.Apply();
        }
        catch { }
    }

    private void RestoreBandLevels()
    {
        var prefs = Context.GetSharedPreferences(PrefsName, FileCreationMode.Private);
        for (int i = 0; i < EqBandProcessor.Bands; i++)
        {
            int saved = prefs.GetInt(KeyBandLevelPrefix + i, int.MinValue);
            if (saved != int.MinValue)
            {
                short millibels = (short)saved;
                _eqProcessor.SetBandLevelMillibels(i, millibels);
            }
        }
        UpdateSlidersFromProcessor();
    }

    #endregion

    protected override void OnStop()
    {
        SaveSettings();
        base.OnStop();
    }

    public void Release()
    {
        try { BassClass.GetMethod("release").Invoke(_bass); } catch { }
        try { VirClass.GetMethod("release").Invoke(_virtual); } catch { }
        try { ReverbClass.GetMethod("release").Invoke(_reverb); } catch { }
        _bass = _virtual = _reverb = null;
    }
}
