using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using Android.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.UI.Services.Effects;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Services;

/// <summary>
/// 音效对话框 — 栈式多页面导航结构:
/// 主页: 播放倍速、均衡器入口、压限器入口、混响入口、音效增强入口、MAX Audio、MISOUND
/// 均衡器子页: 10 段 EQ + 低音增强 + 环绕声
/// 压限器子页: Threshold / Ratio / Attack / Release / Makeup Gain
/// 混响子页: 预设 + Decay / WetDry / PreDelay / Damping
/// 音效增强子页: 立体声扩展 / 磁带饱和 / 去齿音 / 限幅器
/// </summary>
public class SoundEffectDialog : Dialog
{
    private readonly IAudioPlayerService _playerService;
    private readonly EqBandProcessor _eqProcessor;
    private readonly CompressorProcessor _compressor;
    private readonly ReverbProcessor _reverbFx;
    private readonly StereoWidenerProcessor _widener;
    private readonly TapeSaturationProcessor _saturation;
    private readonly DeEsserProcessor _deEsser;
    private readonly LimiterProcessor _limiter;
    private int _audioSessionId;

    // JNI 硬件效果
    private Java.Lang.Object? _bass;
    private Java.Lang.Object? _virtual;
    private Java.Lang.Object? _hwReverb;

    // ===== 栈式导航 =====
    private readonly Stack<string> _navStack = new();
    private readonly Dictionary<string, LinearLayout> _screens = new();
    private readonly Dictionary<string, string> _screenTitles = new();
    private TextView? _headerTitle;
    private TextView? _backBtn;

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
    private SeekBar? _compThresholdSeek, _compRatioSeek, _compAttackSeek, _compReleaseSeek, _compMakeupSeek;
    private TextView? _compThresholdVal, _compRatioVal, _compAttackVal, _compReleaseVal, _compMakeupVal;

    // UI — 混响
    private Switch? _reverbSwitch;
    private LinearLayout? _reverbPresetRow;
    private SeekBar? _reverbDecaySeek, _reverbWetDrySeek, _reverbPreDelaySeek, _reverbDampingSeek;
    private TextView? _reverbDecayVal, _reverbWetDryVal, _reverbPreDelayVal, _reverbDampingVal;
    private int _selectedReverbPreset = (int)ReverbProcessor.ReverbPreset.Hall;

    // UI — 立体声扩展
    private Switch? _widenerSwitch;
    private SeekBar? _widenerWidthSeek;
    private TextView? _widenerWidthVal;

    // UI — 磁带饱和
    private Switch? _satSwitch;
    private SeekBar? _satDriveSeek, _satWarmthSeek, _satToneSeek;
    private TextView? _satDriveVal, _satWarmthVal, _satToneVal;

    // UI — 去齿音
    private Switch? _deesserSwitch;
    private SeekBar? _deesserFreqSeek, _deesserSensSeek, _deesserReductionSeek;
    private TextView? _deesserFreqVal, _deesserSensVal, _deesserReductionVal;

    // UI — 限幅器
    private Switch? _limiterSwitch;
    private SeekBar? _limiterCeilingSeek, _limiterReleaseSeek;
    private TextView? _limiterCeilingVal, _limiterReleaseVal;

    // UI — MAX Audio
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
    private const string KeyCompThreshold = "comp_threshold";
    private const string KeyCompRatio = "comp_ratio";
    private const string KeyCompAttack = "comp_attack";
    private const string KeyCompRelease = "comp_release";
    private const string KeyCompMakeup = "comp_makeup";
    private const string KeyMaxAudioEnabled = "maxaudio_enabled";
    private const string KeyReverbEnabled = "reverb_enabled";
    private const string KeyReverbPreset = "reverb_preset";
    private const string KeyReverbDecay = "reverb_decay";
    private const string KeyReverbWetDry = "reverb_wetdry";
    private const string KeyReverbPreDelay = "reverb_predelay";
    private const string KeyReverbDamping = "reverb_damping";
    private const string KeyWidenerEnabled = "widener_enabled";
    private const string KeyWidenerWidth = "widener_width";
    private const string KeySatEnabled = "sat_enabled";
    private const string KeySatDrive = "sat_drive";
    private const string KeySatWarmth = "sat_warmth";
    private const string KeySatTone = "sat_tone";
    private const string KeyDeesserEnabled = "deesser_enabled";
    private const string KeyDeesserFreq = "deesser_freq";
    private const string KeyDeesserSens = "deesser_sens";
    private const string KeyDeesserReduction = "deesser_reduction";
    private const string KeyLimiterEnabled = "limiter_enabled";
    private const string KeyLimiterCeiling = "limiter_ceiling";
    private const string KeyLimiterRelease = "limiter_release";

    /// <summary>10段预设均衡器曲线（单位：millibels）</summary>
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

    private static readonly string[] ReverbPresetNames =
        ["录音棚", "房间", "密室", "大厅", "大教堂", "板式", "弹簧"];

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
        var sp = MainApplication.Services;
        _eqProcessor = sp.GetRequiredService<EqBandProcessor>();
        _compressor = sp.GetRequiredService<CompressorProcessor>();
        _reverbFx = sp.GetRequiredService<ReverbProcessor>();
        _widener = sp.GetRequiredService<StereoWidenerProcessor>();
        _saturation = sp.GetRequiredService<TapeSaturationProcessor>();
        _deEsser = sp.GetRequiredService<DeEsserProcessor>();
        _limiter = sp.GetRequiredService<LimiterProcessor>();
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

        _backBtn = new TextView(Context)
        {
            Text = "\u2039 ",
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent),
            Visibility = ViewStates.Gone,
        };
        _backBtn.SetTextColor(Color.White);
        _backBtn.TextSize = 24f;
        _backBtn.Gravity = GravityFlags.Center;
        _backBtn.SetPadding(dp(0), dp(0), dp(8), dp(0));
        _backBtn.Click += (s, e) => NavigateBack();
        header.AddView(_backBtn);

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

        // ===== 创建各页面 =====
        var mainScreen = CreateMainScreen(dp);
        var eqScreen = CreateEqScreen(dp);
        var compScreen = CreateCompressorScreen(dp);
        var reverbScreen = CreateReverbScreen(dp);
        var enhanceScreen = CreateEnhancementScreen(dp);

        _screens["main"] = mainScreen;
        _screens["eq"] = eqScreen;
        _screens["comp"] = compScreen;
        _screens["reverb"] = reverbScreen;
        _screens["enhance"] = enhanceScreen;

        _screenTitles["main"] = "音效";
        _screenTitles["eq"] = "均衡器";
        _screenTitles["comp"] = "压限器";
        _screenTitles["reverb"] = "混响效果";
        _screenTitles["enhance"] = "音效增强";

        // 初始: 只显示主页
        foreach (var kv in _screens)
        {
            kv.Value.Visibility = kv.Key == "main" ? ViewStates.Visible : ViewStates.Gone;
            root.AddView(kv.Value);
        }

        return root;
    }

    #region Main Screen
    private LinearLayout CreateMainScreen(Func<int, int> dp)
    {
        var screen = new LinearLayout(Context)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        var scroll = new ScrollView(Context)
        {
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        var content = new LinearLayout(Context)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new ScrollView.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        content.SetPadding(0, 0, 0, dp(16));

        content.AddView(MakeSpeedSection(dp));
        content.AddView(MakeDivider(dp));
        content.AddView(MakeMenuEntry("\u266B", "均衡器", "10 Band Equalizer", (s, e) => NavigateTo("eq")));
        content.AddView(MakeMenuEntry("\u25B6", "压限器", "Dynamic Compressor", (s, e) => NavigateTo("comp")));
        content.AddView(MakeMenuEntry("\u223F", "混响效果", "Schroeder Reverb", (s, e) => NavigateTo("reverb")));
        content.AddView(MakeMenuEntry("\u2605", "音效增强", "Stereo / Saturation / De-esser / Limiter", (s, e) => NavigateTo("enhance")));
        content.AddView(MakeMaxAudioSection(dp));
        if (IsXiaomiDevice())
            content.AddView(MakeMenuEntry("\u2669", "MISOUND", "Xiaomi Audio Effect", (s, e) => LaunchMiSound()));
        content.AddView(MakeDivider(dp));

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
        content.AddView(footer);

        scroll.AddView(content);
        screen.AddView(scroll);
        return screen;
    }
    #endregion

    #region EQ Screen
    private LinearLayout CreateEqScreen(Func<int, int> dp)
    {
        var screen = new LinearLayout(Context)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        var scroll = new ScrollView(Context)
        {
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        var content = new LinearLayout(Context)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new ScrollView.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        content.SetPadding(0, 0, 0, dp(16));

        content.AddView(MakeSectionHeader("均衡器", out _eqSwitch));

        // Preset row
        var presetRow = new LinearLayout(Context) { Orientation = Orientation.Horizontal, LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) };
        presetRow.SetGravity(GravityFlags.CenterVertical);
        presetRow.SetPadding(dp(16), 0, dp(16), dp(4));
        var userLabel = new TextView(Context) { Text = "用户 ", LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) };
        userLabel.SetTextColor(Color.ParseColor("#88FFFFFF"));
        userLabel.TextSize = 13f;
        presetRow.AddView(userLabel);
        _presetSpinner = new Spinner(Context) { LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f) };
        _presetSpinner.ItemSelected += OnPresetSelected;
        presetRow.AddView(_presetSpinner);
        content.AddView(presetRow);

        // 10-band sliders
        var hScrollView = new HorizontalScrollView(Context) { LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, dp(210)) };
        _slidersContainer = new LinearLayout(Context) { Orientation = Orientation.Horizontal, LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.MatchParent) };
        _slidersContainer.SetGravity(GravityFlags.CenterHorizontal);
        hScrollView.AddView(_slidersContainer);
        content.AddView(hScrollView);
        _eqSwitch.SetOnCheckedChangeListener(new EqSwitchListener(this));

        // dB Scale
        var scaleRow = new LinearLayout(Context) { Orientation = Orientation.Horizontal, LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) };
        scaleRow.SetPadding(dp(16), 0, dp(16), dp(4));
        scaleRow.SetGravity(GravityFlags.CenterVertical);
        AddScaleLabel(scaleRow, "+15 dB", GravityFlags.Start, 1f);
        AddScaleLabel(scaleRow, "0 dB", GravityFlags.Center, 1f);
        AddScaleLabel(scaleRow, "-15 dB", GravityFlags.End, 1f);
        content.AddView(scaleRow);

        content.AddView(MakeDivider(dp));

        // Bass Boost
        content.AddView(MakeSectionHeader("低音增强", out _bassSwitch));
        var bassRow = new LinearLayout(Context) { Orientation = Orientation.Horizontal, LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) };
        bassRow.SetGravity(GravityFlags.CenterVertical);
        bassRow.SetPadding(dp(16), 0, dp(16), dp(8));
        _bassSeekBar = new SeekBar(Context) { LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f), Max = 1000 };
        _bassSeekBar.SetOnSeekBarChangeListener(new BassSeekBarListener(this));
        _bassValue = new TextView(Context) { LayoutParameters = new LinearLayout.LayoutParams(dp(50), ViewGroup.LayoutParams.WrapContent) };
        _bassValue.SetTextColor(Color.White); _bassValue.TextSize = 13f; _bassValue.Gravity = GravityFlags.Center; _bassValue.Text = "0%";
        bassRow.AddView(_bassSeekBar); bassRow.AddView(_bassValue);
        content.AddView(bassRow);
        _bassSwitch.SetOnCheckedChangeListener(new BassSwitchListener(this));

        // Virtualizer
        content.AddView(MakeSectionHeader("环绕声", out _virSwitch));
        var virRow = new LinearLayout(Context) { Orientation = Orientation.Horizontal, LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) };
        virRow.SetGravity(GravityFlags.CenterVertical);
        virRow.SetPadding(dp(16), 0, dp(16), dp(8));
        _virSeekBar = new SeekBar(Context) { LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f), Max = 1000 };
        _virSeekBar.SetOnSeekBarChangeListener(new VirSeekBarListener(this));
        _virValue = new TextView(Context) { LayoutParameters = new LinearLayout.LayoutParams(dp(50), ViewGroup.LayoutParams.WrapContent) };
        _virValue.SetTextColor(Color.White); _virValue.TextSize = 13f; _virValue.Gravity = GravityFlags.Center; _virValue.Text = "0%";
        virRow.AddView(_virSeekBar); virRow.AddView(_virValue);
        content.AddView(virRow);
        _virSwitch.SetOnCheckedChangeListener(new VirSwitchListener(this));

        scroll.AddView(content);
        screen.AddView(scroll);
        return screen;
    }
    #endregion

    #region Compressor Screen
    private LinearLayout CreateCompressorScreen(Func<int, int> dp)
    {
        var screen = new LinearLayout(Context) { Orientation = Orientation.Vertical, LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) };
        var scroll = new ScrollView(Context) { LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) };
        var content = new LinearLayout(Context) { Orientation = Orientation.Vertical, LayoutParameters = new ScrollView.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) };
        content.SetPadding(0, 0, 0, dp(16));

        content.AddView(MakeSectionHeader("压限器 (Compressor)", out _compSwitch));
        _compSwitch.SetOnCheckedChangeListener(new GenericSwitchListener(this, (d, on) => { d._compressor.Enabled = on; d.SaveSettings(); }));

        var desc = new TextView(Context) { Text = "控制动态范围，使响亮部分更安静，安静部分更响亮", LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) };
        desc.SetTextColor(Color.ParseColor("#88FFFFFF")); desc.TextSize = 12f; desc.SetPadding(dp(16), dp(2), dp(16), dp(8));
        content.AddView(desc);

        // Threshold: -60 ~ 0 dB, default -20
        content.AddView(MakeParamSlider("阈值 (Threshold)", "dB", -60, 0, -20, dp, out _compThresholdSeek, out _compThresholdVal));
        _compThresholdSeek.SetOnSeekBarChangeListener(new GenericSeekListener(this, (d, p) =>
        {
            float val = -60f + p / 1000f * 60f;
            d._compressor.ThresholdDb = val;
            d._compThresholdVal!.Text = $"{val:F0} dB";
            if (d._isInitializing) return; d.SaveSettings();
        }));

        // Ratio: 1.0 ~ 20.0 (×10 stored as 10~200), default 4.0
        content.AddView(MakeParamSlider("压缩比 (Ratio)", ":1", 10, 200, 40, dp, out _compRatioSeek, out _compRatioVal));
        _compRatioSeek.SetOnSeekBarChangeListener(new GenericSeekListener(this, (d, p) =>
        {
            float val = p / 10f;
            d._compressor.Ratio = val;
            d._compRatioVal!.Text = $"{val:F1}:1";
            if (d._isInitializing) return; d.SaveSettings();
        }));

        // Attack: 1 ~ 100 ms, default 10
        content.AddView(MakeParamSlider("启动 (Attack)", "ms", 1, 100, 10, dp, out _compAttackSeek, out _compAttackVal));
        _compAttackSeek.SetOnSeekBarChangeListener(new GenericSeekListener(this, (d, p) =>
        {
            float val = 1f + p / 1000f * 99f;
            d._compressor.AttackMs = val;
            d._compAttackVal!.Text = $"{val:F0} ms";
            if (d._isInitializing) return; d.SaveSettings();
        }));

        // Release: 10 ~ 1000 ms, default 100
        content.AddView(MakeParamSlider("释放 (Release)", "ms", 10, 1000, 100, dp, out _compReleaseSeek, out _compReleaseVal));
        _compReleaseSeek.SetOnSeekBarChangeListener(new GenericSeekListener(this, (d, p) =>
        {
            float val = 10f + p / 1000f * 990f;
            d._compressor.ReleaseMs = val;
            d._compReleaseVal!.Text = $"{val:F0} ms";
            if (d._isInitializing) return; d.SaveSettings();
        }));

        // Makeup Gain: 0 ~ 30 dB, default 0
        content.AddView(MakeParamSlider("补偿增益 (Makeup)", "dB", 0, 300, 0, dp, out _compMakeupSeek, out _compMakeupVal));
        _compMakeupSeek.SetOnSeekBarChangeListener(new GenericSeekListener(this, (d, p) =>
        {
            float val = p / 10f;
            d._compressor.MakeupDb = val;
            d._compMakeupVal!.Text = $"+{val:F1} dB";
            if (d._isInitializing) return; d.SaveSettings();
        }));

        scroll.AddView(content);
        screen.AddView(scroll);
        return screen;
    }
    #endregion

    #region Reverb Screen
    private LinearLayout CreateReverbScreen(Func<int, int> dp)
    {
        var screen = new LinearLayout(Context) { Orientation = Orientation.Vertical, LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) };
        var scroll = new ScrollView(Context) { LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) };
        var content = new LinearLayout(Context) { Orientation = Orientation.Vertical, LayoutParameters = new ScrollView.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) };
        content.SetPadding(0, 0, 0, dp(16));

        content.AddView(MakeSectionHeader("混响效果 (Reverb)", out _reverbSwitch));
        _reverbSwitch.SetOnCheckedChangeListener(new GenericSwitchListener(this, (d, on) => { d._reverbFx.Enabled = on; d.SaveSettings(); }));

        // Preset chips
        _reverbPresetRow = new LinearLayout(Context) { Orientation = Orientation.Horizontal, LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) };
        _reverbPresetRow.SetPadding(dp(12), dp(4), dp(12), dp(8));
        var reverbHScroll = new HorizontalScrollView(Context) { LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent), HorizontalScrollBarEnabled = false };
        for (int i = 0; i < ReverbPresetNames.Length; i++)
        {
            var btn = new TextView(Context) { Text = ReverbPresetNames[i], LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) };
            btn.SetPadding(dp(12), dp(6), dp(12), dp(6));
            btn.TextSize = 12f; btn.Gravity = GravityFlags.Center;
            var lp = btn.LayoutParameters as LinearLayout.LayoutParams;
            lp!.SetMargins(dp(3), 0, dp(3), 0);
            btn.LayoutParameters = lp;
            var idx = i;
            btn.Click += (s, e) =>
            {
                _selectedReverbPreset = idx;
                _reverbFx.Preset = (ReverbProcessor.ReverbPreset)idx;
                UpdateReverbPresetStyles();
                if (!_isInitializing) SaveSettings();
            };
            _reverbPresetRow.AddView(btn);
        }
        reverbHScroll.AddView(_reverbPresetRow);
        content.AddView(reverbHScroll);

        // Decay: 0.1 ~ 5.0 s (×100: 10~500), default 180 (1.8s)
        content.AddView(MakeParamSlider("衰减时间 (Decay)", "s", 10, 500, 180, dp, out _reverbDecaySeek, out _reverbDecayVal));
        _reverbDecaySeek.SetOnSeekBarChangeListener(new GenericSeekListener(this, (d, p) =>
        {
            float val = 0.1f + p / 1000f * 4.9f;
            d._reverbFx.DecayTime = val;
            d._reverbDecayVal!.Text = $"{val:F1} s";
            if (d._isInitializing) return; d.SaveSettings();
        }));

        // Wet/Dry: 0 ~ 100%, default 30
        content.AddView(MakeParamSlider("湿/干比 (Wet/Dry)", "%", 0, 1000, 300, dp, out _reverbWetDrySeek, out _reverbWetDryVal));
        _reverbWetDrySeek.SetOnSeekBarChangeListener(new GenericSeekListener(this, (d, p) =>
        {
            float val = p / 1000f;
            d._reverbFx.WetDry = val;
            d._reverbWetDryVal!.Text = $"{val * 100:F0}%";
            if (d._isInitializing) return; d.SaveSettings();
        }));

        // Pre-delay: 0 ~ 100 ms, default 20
        content.AddView(MakeParamSlider("预延迟 (Pre-delay)", "ms", 0, 1000, 200, dp, out _reverbPreDelaySeek, out _reverbPreDelayVal));
        _reverbPreDelaySeek.SetOnSeekBarChangeListener(new GenericSeekListener(this, (d, p) =>
        {
            float val = p / 1000f * 100f;
            d._reverbFx.PreDelayMs = val;
            d._reverbPreDelayVal!.Text = $"{val:F0} ms";
            if (d._isInitializing) return; d.SaveSettings();
        }));

        // Damping: 0 ~ 100%, default 50
        content.AddView(MakeParamSlider("阻尼 (Damping)", "%", 0, 1000, 500, dp, out _reverbDampingSeek, out _reverbDampingVal));
        _reverbDampingSeek.SetOnSeekBarChangeListener(new GenericSeekListener(this, (d, p) =>
        {
            float val = p / 1000f;
            d._reverbFx.Damping = val;
            d._reverbDampingVal!.Text = $"{val * 100:F0}%";
            if (d._isInitializing) return; d.SaveSettings();
        }));

        scroll.AddView(content);
        screen.AddView(scroll);
        return screen;
    }
    #endregion

    #region Enhancement Screen
    private LinearLayout CreateEnhancementScreen(Func<int, int> dp)
    {
        var screen = new LinearLayout(Context) { Orientation = Orientation.Vertical, LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) };
        var scroll = new ScrollView(Context) { LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) };
        var content = new LinearLayout(Context) { Orientation = Orientation.Vertical, LayoutParameters = new ScrollView.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) };
        content.SetPadding(0, 0, 0, dp(16));

        // === 立体声扩展 ===
        content.AddView(MakeSectionHeader("立体声扩展", out _widenerSwitch));
        _widenerSwitch.SetOnCheckedChangeListener(new GenericSwitchListener(this, (d, on) => { d._widener.Enabled = on; d.SaveSettings(); }));

        // Width: -100 ~ +100 (stored as 0~200, offset +100), default 100 (=0%)
        content.AddView(MakeParamSlider("宽度 (Width)", "%", 0, 2000, 1000, dp, out _widenerWidthSeek, out _widenerWidthVal));
        _widenerWidthSeek.SetOnSeekBarChangeListener(new GenericSeekListener(this, (d, p) =>
        {
            float val = p / 1000f * 200f - 100f;
            d._widener.Width = val;
            d._widenerWidthVal!.Text = $"{val:+0;-0;0}%";
            if (d._isInitializing) return; d.SaveSettings();
        }));

        content.AddView(MakeDivider(dp));

        // === 磁带饱和 ===
        content.AddView(MakeSectionHeader("磁带温暖", out _satSwitch));
        _satSwitch.SetOnCheckedChangeListener(new GenericSwitchListener(this, (d, on) => { d._saturation.Enabled = on; d.SaveSettings(); }));

        // Drive: 0 ~ 24 dB (×100: 0~2400), default 600 (6dB)
        content.AddView(MakeParamSlider("驱动 (Drive)", "dB", 0, 2400, 600, dp, out _satDriveSeek, out _satDriveVal));
        _satDriveSeek.SetOnSeekBarChangeListener(new GenericSeekListener(this, (d, p) =>
        {
            float val = p / 100f;
            d._saturation.DriveDb = val;
            d._satDriveVal!.Text = $"+{val:F1} dB";
            if (d._isInitializing) return; d.SaveSettings();
        }));

        // Warmth: 0 ~ 100%
        content.AddView(MakeParamSlider("温暖度 (Warmth)", "%", 0, 1000, 500, dp, out _satWarmthSeek, out _satWarmthVal));
        _satWarmthSeek.SetOnSeekBarChangeListener(new GenericSeekListener(this, (d, p) =>
        {
            float val = p / 1000f;
            d._saturation.Warmth = val;
            d._satWarmthVal!.Text = $"{val * 100:F0}%";
            if (d._isInitializing) return; d.SaveSettings();
        }));

        // Tone: -100 ~ +100 (offset +100 = 0~200), default 100 (=0)
        content.AddView(MakeParamSlider("音色 (Tone)", "", 0, 2000, 1000, dp, out _satToneSeek, out _satToneVal));
        _satToneSeek.SetOnSeekBarChangeListener(new GenericSeekListener(this, (d, p) =>
        {
            int val = (int)(p / 1000f * 200f - 100f);
            d._saturation.Tone = val;
            d._satToneVal!.Text = $"{val:+0;-0;0}";
            if (d._isInitializing) return; d.SaveSettings();
        }));

        content.AddView(MakeDivider(dp));

        // === 去齿音 ===
        content.AddView(MakeSectionHeader("去齿音", out _deesserSwitch));
        _deesserSwitch.SetOnCheckedChangeListener(new GenericSwitchListener(this, (d, on) => { d._deEsser.Enabled = on; d.SaveSettings(); }));

        // Frequency: 2000 ~ 12000 Hz, default 6000
        content.AddView(MakeParamSlider("频率 (Frequency)", "Hz", 0, 1000, 400, dp, out _deesserFreqSeek, out _deesserFreqVal));
        _deesserFreqSeek.SetOnSeekBarChangeListener(new GenericSeekListener(this, (d, p) =>
        {
            float val = 2000f + p / 1000f * 10000f;
            d._deEsser.Frequency = val;
            d._deesserFreqVal!.Text = $"{val:F0} Hz";
            if (d._isInitializing) return; d.SaveSettings();
        }));

        // Sensitivity: 0 ~ 100%
        content.AddView(MakeParamSlider("灵敏度 (Sensitivity)", "%", 0, 1000, 500, dp, out _deesserSensSeek, out _deesserSensVal));
        _deesserSensSeek.SetOnSeekBarChangeListener(new GenericSeekListener(this, (d, p) =>
        {
            float val = p / 10f;
            d._deEsser.Sensitivity = val;
            d._deesserSensVal!.Text = $"{val:F0}%";
            if (d._isInitializing) return; d.SaveSettings();
        }));

        // Reduction: 0 ~ -20 dB (×100: 0~2000), default 1000 (10dB)
        content.AddView(MakeParamSlider("衰减量 (Reduction)", "dB", 0, 2000, 1000, dp, out _deesserReductionSeek, out _deesserReductionVal));
        _deesserReductionSeek.SetOnSeekBarChangeListener(new GenericSeekListener(this, (d, p) =>
        {
            float val = -p / 100f;
            d._deEsser.ReductionDb = val;
            d._deesserReductionVal!.Text = $"{val:F1} dB";
            if (d._isInitializing) return; d.SaveSettings();
        }));

        content.AddView(MakeDivider(dp));

        // === 限幅器 ===
        content.AddView(MakeSectionHeader("限幅器 (Limiter)", out _limiterSwitch));
        _limiterSwitch.SetOnCheckedChangeListener(new GenericSwitchListener(this, (d, on) => { d._limiter.Enabled = on; d.SaveSettings(); }));

        // Ceiling: -6 ~ 0 dB (×100: 0~600), default 597 (-0.3dB)
        content.AddView(MakeParamSlider("天花板 (Ceiling)", "dB", 0, 600, 597, dp, out _limiterCeilingSeek, out _limiterCeilingVal));
        _limiterCeilingSeek.SetOnSeekBarChangeListener(new GenericSeekListener(this, (d, p) =>
        {
            float val = -6f + p / 100f;
            d._limiter.CeilingDb = val;
            d._limiterCeilingVal!.Text = $"{val:F1} dB";
            if (d._isInitializing) return; d.SaveSettings();
        }));

        // Release: 10 ~ 500 ms, default 50
        content.AddView(MakeParamSlider("释放 (Release)", "ms", 10, 500, 50, dp, out _limiterReleaseSeek, out _limiterReleaseVal));
        _limiterReleaseSeek.SetOnSeekBarChangeListener(new GenericSeekListener(this, (d, p) =>
        {
            float val = 10f + p / 1000f * 490f;
            d._limiter.ReleaseMs = val;
            d._limiterReleaseVal!.Text = $"{val:F0} ms";
            if (d._isInitializing) return; d.SaveSettings();
        }));

        scroll.AddView(content);
        screen.AddView(scroll);
        return screen;
    }
    #endregion

    #region UI Helpers

    private LinearLayout MakeMenuEntry(string icon, string title, string subtitle, EventHandler onClick)
    {
        int dp(int v) => (int)(v * (Context.Resources?.DisplayMetrics?.Density ?? 1f));
        var row = new LinearLayout(Context) { Orientation = Orientation.Horizontal, LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent), Clickable = true, Focusable = true };
        row.SetGravity(GravityFlags.CenterVertical);
        row.SetPadding(dp(16), dp(14), dp(16), dp(14));
        var iconTv = new TextView(Context) { Text = icon, LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) };
        iconTv.SetTextColor(Color.ParseColor("#88FFFFFF")); iconTv.TextSize = 18f; iconTv.Gravity = GravityFlags.Center; iconTv.SetPadding(dp(0), dp(0), dp(12), dp(0));
        var textContainer = new LinearLayout(Context) { Orientation = Orientation.Vertical, LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f) };
        var titleTv = new TextView(Context) { Text = title, LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) };
        titleTv.SetTextColor(Color.White); titleTv.TextSize = 15f;
        textContainer.AddView(titleTv);
        var subTv = new TextView(Context) { Text = subtitle, LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) };
        subTv.SetTextColor(Color.ParseColor("#88FFFFFF")); subTv.TextSize = 11f;
        textContainer.AddView(subTv);
        var chevron = new TextView(Context) { Text = "\u203A", LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) };
        chevron.SetTextColor(Color.ParseColor("#88FFFFFF")); chevron.TextSize = 22f; chevron.Gravity = GravityFlags.Center;
        row.AddView(iconTv); row.AddView(textContainer); row.AddView(chevron);
        row.Click += onClick;
        return row;
    }

    private LinearLayout MakeSectionHeader(string title, out Switch sw)
    {
        int dp(int v) => (int)(v * (Context.Resources?.DisplayMetrics?.Density ?? 1f));
        var row = new LinearLayout(Context) { Orientation = Orientation.Horizontal, LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) };
        row.SetGravity(GravityFlags.CenterVertical); row.SetPadding(dp(16), dp(10), dp(16), 0);
        var tv = new TextView(Context) { Text = title, LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f) };
        tv.SetTextColor(Color.White); tv.TextSize = 15f; tv.SetTypeface(null, TypefaceStyle.Bold);
        sw = new Switch(Context) { LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) };
        row.AddView(tv); row.AddView(sw);
        return row;
    }

    private View MakeDivider(Func<int, int> dp)
    {
        var d = new View(Context) { LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, dp(1)) };
        d.SetBackgroundColor(Color.ParseColor("#22FFFFFF"));
        return d;
    }

    private void AddScaleLabel(LinearLayout parent, string text, GravityFlags gravity, float weight)
    {
        var tv = new TextView(Context) { Text = text, LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, weight) };
        tv.SetTextColor(Color.ParseColor("#66FFFFFF")); tv.TextSize = 10f; tv.Gravity = gravity;
        parent.AddView(tv);
    }

    /// <summary>创建带标签 + SeekBar + 值显示的参数行</summary>
    private LinearLayout MakeParamSlider(string label, string unit, int min, int max, int defaultVal,
        Func<int, int> dp, out SeekBar seekBar, out TextView valueText)
    {
        var container = new LinearLayout(Context) { Orientation = Orientation.Vertical, LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) };
        var labelRow = new LinearLayout(Context) { Orientation = Orientation.Horizontal, LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) };
        labelRow.SetPadding(dp(16), dp(6), dp(16), 0);
        var labelTv = new TextView(Context) { Text = label, LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f) };
        labelTv.SetTextColor(Color.ParseColor("#CCFFFFFF")); labelTv.TextSize = 13f;
        valueText = new TextView(Context) { LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) };
        valueText.SetTextColor(Color.ParseColor("#88FFFFFF")); valueText.TextSize = 12f; valueText.Gravity = GravityFlags.End;
        labelRow.AddView(labelTv); labelRow.AddView(valueText);
        container.AddView(labelRow);

        seekBar = new SeekBar(Context) { LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent), Max = 1000, Progress = defaultVal };
        seekBar.SetPadding(dp(16), dp(2), dp(16), dp(6));
        container.AddView(seekBar);
        return container;
    }

    #endregion

    #region Speed & MAX Audio Sections

    private LinearLayout MakeSpeedSection(Func<int, int> dp)
    {
        var container = new LinearLayout(Context) { Orientation = Orientation.Vertical, LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) };
        var label = new TextView(Context) { Text = "播放倍速", LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) };
        label.SetTextColor(Color.White); label.TextSize = 15f; label.SetTypeface(null, TypefaceStyle.Bold);
        label.SetPadding(dp(16), dp(10), dp(16), dp(6));
        container.AddView(label);

        var speedRow = new HorizontalScrollView(Context) { LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent), HorizontalScrollBarEnabled = false };
        var speedContainer = new LinearLayout(Context) { Orientation = Orientation.Horizontal, LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) };
        speedContainer.SetPadding(dp(12), dp(4), dp(12), dp(8));

        float[] speeds = [0.25f, 0.5f, 0.75f, 0.8f, 0.9f, 0.95f, 1.0f];
        foreach (var speed in speeds)
        {
            var btn = new TextView(Context) { Text = $"x{speed:0.##}", LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) };
            btn.SetPadding(dp(10), dp(6), dp(10), dp(6)); btn.Gravity = GravityFlags.Center; btn.TextSize = 13f;
            var lp = btn.LayoutParameters as LinearLayout.LayoutParams; lp!.SetMargins(dp(3), 0, dp(3), 0); btn.LayoutParameters = lp;
            var currentSpeed = GetCurrentPlaybackSpeed();
            bool isSelected = Math.Abs(speed - currentSpeed) < 0.01f;
            UpdateSpeedButtonStyle(btn, isSelected);
            var s = speed;
            btn.Click += (sender, e) =>
            {
                SetPlaybackSpeed(s);
                for (int i = 0; i < speedContainer.ChildCount; i++)
                    if (speedContainer.GetChildAt(i) is TextView tv) UpdateSpeedButtonStyle(tv, tv.Text == $"x{s:0.##}");
            };
            speedContainer.AddView(btn);
        }
        speedRow.AddView(speedContainer); container.AddView(speedRow);
        return container;
    }

    private void UpdateSpeedButtonStyle(TextView btn, bool selected)
    {
        if (selected) { btn.SetBackgroundColor(Color.ParseColor("#2196F3")); btn.SetTextColor(Color.White); }
        else { var gd = new GradientDrawable(); gd.SetColor(Color.ParseColor("#33FFFFFF")); gd.SetCornerRadius(Dp(16)); btn.Background = gd; btn.SetTextColor(Color.ParseColor("#CCFFFFFF")); }
    }

    private float GetCurrentPlaybackSpeed()
    {
        try { var p = _playerService.GetType().GetProperty("PlaybackSpeed"); if (p != null) return (float)(p.GetValue(_playerService) ?? 1.0f); } catch { }
        return 1.0f;
    }

    private void SetPlaybackSpeed(float speed)
    {
        try { _playerService.GetType().GetMethod("SetPlaybackSpeed")?.Invoke(_playerService, [speed]); }
        catch (Exception ex) { Android.Util.Log.Warn("CatClaw.SFX", $"设置播放速度失败: {ex.Message}"); }
    }

    private LinearLayout MakeMaxAudioSection(Func<int, int> dp)
    {
        var container = new LinearLayout(Context) { Orientation = Orientation.Vertical, LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) };
        var row = new LinearLayout(Context) { Orientation = Orientation.Horizontal, LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) };
        row.SetGravity(GravityFlags.CenterVertical); row.SetPadding(dp(16), dp(10), dp(16), 0);
        var titleTv = new TextView(Context) { Text = "MAX Audio 音乐厅氛围", LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f) };
        titleTv.SetTextColor(Color.White); titleTv.TextSize = 15f; titleTv.SetTypeface(null, TypefaceStyle.Bold);
        _maxAudioSwitch = new Switch(Context) { LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) };
        _maxAudioSwitch.SetOnCheckedChangeListener(new MaxAudioSwitchListener(this));
        row.AddView(titleTv); row.AddView(_maxAudioSwitch);
        container.AddView(row);
        var desc = new TextView(Context) { Text = "沉浸式音乐厅混响效果，增强空间感与临场感", LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) };
        desc.SetTextColor(Color.ParseColor("#88FFFFFF")); desc.TextSize = 12f; desc.SetPadding(dp(16), dp(4), dp(16), dp(8));
        container.AddView(desc);
        return container;
    }

    #endregion

    #endregion // View Creation

    #region Navigation

    private void NavigateTo(string screenName)
    {
        if (!_screens.TryGetValue(screenName, out var target)) return;

        // 隐藏所有当前可见页面
        foreach (var kv in _screens) kv.Value.Visibility = ViewStates.Gone;
        target.Visibility = ViewStates.Visible;

        _navStack.Push(screenName);
        _backBtn!.Visibility = ViewStates.Visible;
        _headerTitle!.Text = _screenTitles[screenName];
    }

    private void NavigateBack()
    {
        if (_navStack.Count > 0)
        {
            var current = _navStack.Pop();
            _screens[current].Visibility = ViewStates.Gone;
        }

        // 显示栈顶或主页
        string target = _navStack.Count > 0 ? _navStack.Peek() : "main";
        _screens[target].Visibility = ViewStates.Visible;
        _headerTitle!.Text = _screenTitles[target];
        _backBtn!.Visibility = target == "main" ? ViewStates.Gone : ViewStates.Visible;
    }

    public override void OnBackPressed()
    {
        if (_navStack.Count > 0) NavigateBack();
        else base.OnBackPressed();
    }

    #endregion

    #region JNI Helpers (BassBoost / Virtualizer)

    private static Java.Lang.Class? _bassClass, _virClass, _hwReverbClass;
    private static Java.Lang.Class BassClass => _bassClass ??= Java.Lang.Class.ForName("android.media.audiofx.BassBoost");
    private static Java.Lang.Class VirClass => _virClass ??= Java.Lang.Class.ForName("android.media.audiofx.Virtualizer");
    private static Java.Lang.Class HwReverbClass => _hwReverbClass ??= Java.Lang.Class.ForName("android.media.audiofx.EnvironmentalReverb");

    private static Java.Lang.Object NewEffect(Java.Lang.Class cls, int priority, int sessionId)
    {
        var ctor = cls.GetConstructor(Java.Lang.Integer.Type, Java.Lang.Integer.Type);
        return ctor.NewInstance(Java.Lang.Integer.ValueOf(priority), Java.Lang.Integer.ValueOf(sessionId));
    }
    private static void JniCallBool(Java.Lang.Object? obj, Java.Lang.Class cls, string name, bool arg)
    { var m = cls.GetMethod(name, Java.Lang.Boolean.Type); m.Invoke(obj, Java.Lang.Boolean.ValueOf(arg)); }
    private static void JniCallShort(Java.Lang.Object? obj, Java.Lang.Class cls, string name, short arg)
    { var m = cls.GetMethod(name, Java.Lang.Short.Type); m.Invoke(obj, Java.Lang.Short.ValueOf(arg)); }

    private void BassSetEnabled(bool enabled) => JniCallBool(_bass, BassClass, "setEnabled", enabled);
    private void BassSetStrength(short strength) => JniCallShort(_bass, BassClass, "setStrength", strength);
    private void VirSetEnabled(bool enabled) => JniCallBool(_virtual, VirClass, "setEnabled", enabled);
    private void VirSetStrength(short strength) => JniCallShort(_virtual, VirClass, "setStrength", strength);
    private void HwReverbSetEnabled(bool enabled) => JniCallBool(_hwReverb, HwReverbClass, "setEnabled", enabled);

    private void ApplyConcertHallPreset()
    {
        if (_hwReverb == null) return;
        try { JniCallShort(_hwReverb, HwReverbClass, "setRoom", -600); } catch { }
        try { JniCallShort(_hwReverb, HwReverbClass, "setRoomHF", -800); } catch { }
        try { JniCallShort(_hwReverb, HwReverbClass, "setDecayTime", 3000); } catch { }
        try { JniCallShort(_hwReverb, HwReverbClass, "setDiffusion", 800); } catch { }
        try { JniCallShort(_hwReverb, HwReverbClass, "setDensity", 600); } catch { }
    }

    #endregion

    #region Initialization

    private void Init()
    {
        try
        {
            _audioSessionId = _playerService.AudioSessionId;

            // 硬件 BassBoost / Virtualizer / EnvironmentalReverb
            if (_audioSessionId > 0)
            {
                try { _bass = NewEffect(BassClass, 0, _audioSessionId); } catch { }
                try { _virtual = NewEffect(VirClass, 0, _audioSessionId); } catch { }
                try { _hwReverb = NewEffect(HwReverbClass, 0, _audioSessionId); } catch { }
            }

            CreateBandSliders();
            FillPresets();

            _isInitializing = true;
            var prefs = Context.GetSharedPreferences(PrefsName, FileCreationMode.Private);

            // === 均衡器 ===
            bool eqEnabled = prefs.GetBoolean(KeyEqEnabled, false);
            _eqSwitch!.Checked = eqEnabled;
            _eqProcessor.Enabled = eqEnabled;
            int savedPreset = prefs.GetInt(KeyPreset, -1);
            if (savedPreset >= 0 && savedPreset < PresetNames.Count)
            {
                _presetSpinner!.SetSelection(savedPreset + 1);
                if (PresetCurves.TryGetValue(PresetNames[savedPreset], out var curve)) ApplyCurve(curve);
            }
            else if (eqEnabled) { RestoreBandLevels(); }

            // === 低音增强 ===
            _bassSwitch!.Checked = prefs.GetBoolean(KeyBassEnabled, false);
            if (_bass != null) BassSetEnabled(_bassSwitch.Checked);
            _bassSeekBar!.Progress = prefs.GetInt(KeyBassStrength, 0);
            _bassValue!.Text = $"{_bassSeekBar.Progress / 10}%";
            if (_bassSwitch.Checked && _bass != null) BassSetStrength((short)_bassSeekBar.Progress);

            // === 环绕声 ===
            _virSwitch!.Checked = prefs.GetBoolean(KeyVirEnabled, false);
            if (_virtual != null) VirSetEnabled(_virSwitch.Checked);
            _virSeekBar!.Progress = prefs.GetInt(KeyVirStrength, 0);
            _virValue!.Text = $"{_virSeekBar.Progress / 10}%";
            if (_virSwitch.Checked && _virtual != null) VirSetStrength((short)_virSeekBar.Progress);

            // === 压限器 ===
            _compSwitch!.Checked = prefs.GetBoolean(KeyCompEnabled, false);
            _compressor.Enabled = _compSwitch.Checked;
            {
                int thr = prefs.GetInt(KeyCompThreshold, 667); // 667 ≈ (-20+60)/60 * 1000
                _compThresholdSeek!.Progress = thr;
                float thrDb = -60f + thr / 1000f * 60f;
                _compressor.ThresholdDb = thrDb;
                _compThresholdVal!.Text = $"{thrDb:F0} dB";

                int rat = prefs.GetInt(KeyCompRatio, 40);
                _compRatioSeek!.Progress = rat;
                float ratio = rat / 10f;
                _compressor.Ratio = ratio;
                _compRatioVal!.Text = $"{ratio:F1}:1";

                int atk = prefs.GetInt(KeyCompAttack, 91); // (10-1)/99*1000
                _compAttackSeek!.Progress = atk;
                float atkMs = 1f + atk / 1000f * 99f;
                _compressor.AttackMs = atkMs;
                _compAttackVal!.Text = $"{atkMs:F0} ms";

                int rel = prefs.GetInt(KeyCompRelease, 91); // (100-10)/990*1000
                _compReleaseSeek!.Progress = rel;
                float relMs = 10f + rel / 1000f * 990f;
                _compressor.ReleaseMs = relMs;
                _compReleaseVal!.Text = $"{relMs:F0} ms";

                int mk = prefs.GetInt(KeyCompMakeup, 0);
                _compMakeupSeek!.Progress = mk;
                float mkDb = mk / 10f;
                _compressor.MakeupDb = mkDb;
                _compMakeupVal!.Text = $"+{mkDb:F1} dB";
            }

            // === 混响 ===
            _reverbSwitch!.Checked = prefs.GetBoolean(KeyReverbEnabled, false);
            _reverbFx.Enabled = _reverbSwitch.Checked;
            {
                int preset = prefs.GetInt(KeyReverbPreset, (int)ReverbProcessor.ReverbPreset.Hall);
                _selectedReverbPreset = preset;
                _reverbFx.Preset = (ReverbProcessor.ReverbPreset)preset;
                UpdateReverbPresetStyles();

                int decay = prefs.GetInt(KeyReverbDecay, 367); // (1.8-0.1)/4.9*1000
                _reverbDecaySeek!.Progress = decay;
                float decayVal = 0.1f + decay / 1000f * 4.9f;
                _reverbFx.DecayTime = decayVal;
                _reverbDecayVal!.Text = $"{decayVal:F1} s";

                int wd = prefs.GetInt(KeyReverbWetDry, 300);
                _reverbWetDrySeek!.Progress = wd;
                float wdVal = wd / 1000f;
                _reverbFx.WetDry = wdVal;
                _reverbWetDryVal!.Text = $"{wdVal * 100:F0}%";

                int pd = prefs.GetInt(KeyReverbPreDelay, 200);
                _reverbPreDelaySeek!.Progress = pd;
                float pdVal = pd / 1000f * 100f;
                _reverbFx.PreDelayMs = pdVal;
                _reverbPreDelayVal!.Text = $"{pdVal:F0} ms";

                int damp = prefs.GetInt(KeyReverbDamping, 500);
                _reverbDampingSeek!.Progress = damp;
                float dampVal = damp / 1000f;
                _reverbFx.Damping = dampVal;
                _reverbDampingVal!.Text = $"{dampVal * 100:F0}%";
            }

            // === 立体声扩展 ===
            _widenerSwitch!.Checked = prefs.GetBoolean(KeyWidenerEnabled, false);
            _widener.Enabled = _widenerSwitch.Checked;
            {
                int w = prefs.GetInt(KeyWidenerWidth, 1000); // offset 1000 = 0%
                _widenerWidthSeek!.Progress = w;
                float wVal = w / 1000f * 200f - 100f;
                _widener.Width = wVal;
                _widenerWidthVal!.Text = $"{wVal:+0;-0;0}%";
            }

            // === 磁带饱和 ===
            _satSwitch!.Checked = prefs.GetBoolean(KeySatEnabled, false);
            _saturation.Enabled = _satSwitch.Checked;
            {
                int drv = prefs.GetInt(KeySatDrive, 600);
                _satDriveSeek!.Progress = drv;
                float drvVal = drv / 100f;
                _saturation.DriveDb = drvVal;
                _satDriveVal!.Text = $"+{drvVal:F1} dB";

                int warm = prefs.GetInt(KeySatWarmth, 500);
                _satWarmthSeek!.Progress = warm;
                float warmVal = warm / 1000f;
                _saturation.Warmth = warmVal;
                _satWarmthVal!.Text = $"{warmVal * 100:F0}%";

                int tone = prefs.GetInt(KeySatTone, 1000);
                _satToneSeek!.Progress = tone;
                int toneVal = (int)(tone / 1000f * 200f - 100f);
                _saturation.Tone = toneVal;
                _satToneVal!.Text = $"{toneVal:+0;-0;0}";
            }

            // === 去齿音 ===
            _deesserSwitch!.Checked = prefs.GetBoolean(KeyDeesserEnabled, false);
            _deEsser.Enabled = _deesserSwitch.Checked;
            {
                int freq = prefs.GetInt(KeyDeesserFreq, 400); // (6000-2000)/10000*1000
                _deesserFreqSeek!.Progress = freq;
                float freqVal = 2000f + freq / 1000f * 10000f;
                _deEsser.Frequency = freqVal;
                _deesserFreqVal!.Text = $"{freqVal:F0} Hz";

                int sens = prefs.GetInt(KeyDeesserSens, 500);
                _deesserSensSeek!.Progress = sens;
                float sensVal = sens / 10f;
                _deEsser.Sensitivity = sensVal;
                _deesserSensVal!.Text = $"{sensVal:F0}%";

                int red = prefs.GetInt(KeyDeesserReduction, 1000);
                _deesserReductionSeek!.Progress = red;
                float redVal = -red / 100f;
                _deEsser.ReductionDb = redVal;
                _deesserReductionVal!.Text = $"{redVal:F1} dB";
            }

            // === 限幅器 ===
            _limiterSwitch!.Checked = prefs.GetBoolean(KeyLimiterEnabled, false);
            _limiter.Enabled = _limiterSwitch.Checked;
            {
                int ceil = prefs.GetInt(KeyLimiterCeiling, 597);
                _limiterCeilingSeek!.Progress = ceil;
                float ceilVal = -6f + ceil / 100f;
                _limiter.CeilingDb = ceilVal;
                _limiterCeilingVal!.Text = $"{ceilVal:F1} dB";

                int rel = prefs.GetInt(KeyLimiterRelease, 82); // (50-10)/490*1000
                _limiterReleaseSeek!.Progress = rel;
                float relVal = 10f + rel / 1000f * 490f;
                _limiter.ReleaseMs = relVal;
                _limiterReleaseVal!.Text = $"{relVal:F0} ms";
            }

            // === MAX Audio ===
            bool maxAudioEnabled = prefs.GetBoolean(KeyMaxAudioEnabled, false);
            _maxAudioSwitch!.Checked = maxAudioEnabled;
            if (maxAudioEnabled && _hwReverb != null)
            {
                HwReverbSetEnabled(true);
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
            var bandLayout = new LinearLayout(Context) { Orientation = Orientation.Vertical, LayoutParameters = new LinearLayout.LayoutParams(Dp(38), ViewGroup.LayoutParams.MatchParent) };
            bandLayout.SetGravity(GravityFlags.CenterHorizontal); bandLayout.SetPadding(Dp(3), Dp(2), Dp(3), Dp(2));
            int freqHz = EqBandProcessor.Freqs[i];
            string freqLabel = freqHz >= 1000 ? $"{freqHz / 1000}k" : $"{freqHz}";
            var freqText = new TextView(Context) { Text = freqLabel, LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) };
            freqText.SetTextColor(Color.ParseColor("#AAFFFFFF")); freqText.TextSize = 9f; freqText.Gravity = GravityFlags.Center;
            var slider = new VerticalSliderView(Context) { LayoutParameters = new LinearLayout.LayoutParams(Dp(32), 0, 1f), Min = -1500, Max = 1500, Value = 0 };
            var bandIdx = i;
            slider.ValueChanged += (s, v) => OnBandSliderChanged(bandIdx, (short)(int)v);
            var dbText = new TextView(Context) { Text = "0.0", LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) };
            dbText.SetTextColor(Color.White); dbText.TextSize = 9f; dbText.Gravity = GravityFlags.Center;
            _bandSliders[i] = slider; _bandDbTexts[i] = dbText;
            bandLayout.AddView(freqText); bandLayout.AddView(slider); bandLayout.AddView(dbText);
            _slidersContainer.AddView(bandLayout);
        }
    }

    private void FillPresets()
    {
        if (_presetSpinner == null) return;
        var items = new List<string> { "自定义" };
        items.AddRange(PresetNames);
        _presetSpinner.Adapter = new ArrayAdapter<string>(Context, Android.Resource.Layout.SimpleSpinnerDropDownItem, items);
    }

    private void UpdateReverbPresetStyles()
    {
        if (_reverbPresetRow == null) return;
        for (int i = 0; i < _reverbPresetRow.ChildCount; i++)
        {
            if (_reverbPresetRow.GetChildAt(i) is TextView btn)
            {
                if (i == _selectedReverbPreset)
                {
                    btn.SetBackgroundColor(Color.ParseColor("#2196F3")); btn.SetTextColor(Color.White);
                }
                else
                {
                    var gd = new GradientDrawable(); gd.SetColor(Color.ParseColor("#33FFFFFF")); gd.SetCornerRadius(Dp(16));
                    btn.Background = gd; btn.SetTextColor(Color.ParseColor("#CCFFFFFF"));
                }
            }
        }
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
            if (_bandSliders[i] != null) _bandSliders[i]!.Value = curve[i];
            if (_bandDbTexts[i] != null) { float db = curve[i] / 100f; _bandDbTexts[i]!.Text = $"{db:+0.0;-0.0;0.0}"; }
        }
    }

    private void UpdateSlidersFromProcessor()
    {
        for (int i = 0; i < EqBandProcessor.Bands; i++)
        {
            var mb = _eqProcessor.GetBandLevelMillibels(i);
            if (_bandSliders[i] != null) _bandSliders[i]!.Value = mb;
            if (_bandDbTexts[i] != null) { float db = mb / 100f; _bandDbTexts[i]!.Text = $"{db:+0.0;-0.0;0.0}"; }
        }
    }

    #endregion

    #region Event Handlers

    private void OnPresetSelected(object? sender, AdapterView.ItemSelectedEventArgs e)
    {
        if (_isInitializing) return;
        if (e.Position == 0) return;
        var presetName = PresetNames[e.Position - 1];
        if (PresetCurves.TryGetValue(presetName, out var curve))
        {
            _eqSwitch!.Checked = true; _eqProcessor.Enabled = true;
            ApplyCurve(curve); SaveSettings();
        }
    }

    // Generic listeners for new effect controls
    private class GenericSwitchListener : Java.Lang.Object, CompoundButton.IOnCheckedChangeListener
    {
        private readonly WeakReference<SoundEffectDialog> _ref;
        private readonly Action<SoundEffectDialog, bool> _action;
        public GenericSwitchListener(SoundEffectDialog d, Action<SoundEffectDialog, bool> action) { _ref = new(d); _action = action; }
        public void OnCheckedChanged(CompoundButton? b, bool isChecked)
        {
            if (!_ref.TryGetTarget(out var d) || d._isInitializing) return;
            _action(d, isChecked);
        }
    }

    private class GenericSeekListener : Java.Lang.Object, SeekBar.IOnSeekBarChangeListener
    {
        private readonly WeakReference<SoundEffectDialog> _ref;
        private readonly Action<SoundEffectDialog, int> _action;
        public GenericSeekListener(SoundEffectDialog d, Action<SoundEffectDialog, int> action) { _ref = new(d); _action = action; }
        public void OnProgressChanged(SeekBar? sb, int p, bool fromUser)
        {
            if (!_ref.TryGetTarget(out var d)) return;
            _action(d, p);
        }
        public void OnStartTrackingTouch(SeekBar? sb) { }
        public void OnStopTrackingTouch(SeekBar? sb) { }
    }

    // Legacy listeners (EQ/Bass/Vir)
    private class EqSwitchListener : Java.Lang.Object, CompoundButton.IOnCheckedChangeListener
    {
        private readonly WeakReference<SoundEffectDialog> _ref;
        public EqSwitchListener(SoundEffectDialog d) => _ref = new(d);
        public void OnCheckedChanged(CompoundButton? b, bool enabled)
        {
            if (!_ref.TryGetTarget(out var d)) return;
            d._eqProcessor.Enabled = enabled; d.SaveSettings();
        }
    }

    private class BassSwitchListener : Java.Lang.Object, CompoundButton.IOnCheckedChangeListener
    {
        private readonly WeakReference<SoundEffectDialog> _ref;
        public BassSwitchListener(SoundEffectDialog d) => _ref = new(d);
        public void OnCheckedChanged(CompoundButton? b, bool enabled)
        {
            if (!_ref.TryGetTarget(out var d) || d._bass == null) return;
            d.BassSetEnabled(enabled); if (enabled) d.BassSetStrength((short)d._bassSeekBar!.Progress); d.SaveSettings();
        }
    }

    private class VirSwitchListener : Java.Lang.Object, CompoundButton.IOnCheckedChangeListener
    {
        private readonly WeakReference<SoundEffectDialog> _ref;
        public VirSwitchListener(SoundEffectDialog d) => _ref = new(d);
        public void OnCheckedChanged(CompoundButton? b, bool enabled)
        {
            if (!_ref.TryGetTarget(out var d) || d._virtual == null) return;
            d.VirSetEnabled(enabled); if (enabled) d.VirSetStrength((short)d._virSeekBar!.Progress); d.SaveSettings();
        }
    }

    private class MaxAudioSwitchListener : Java.Lang.Object, CompoundButton.IOnCheckedChangeListener
    {
        private readonly WeakReference<SoundEffectDialog> _ref;
        public MaxAudioSwitchListener(SoundEffectDialog d) => _ref = new(d);
        public void OnCheckedChanged(CompoundButton? b, bool enabled)
        {
            if (!_ref.TryGetTarget(out var d) || d._isInitializing) return;
            if (d._hwReverb != null) { d.HwReverbSetEnabled(enabled); if (enabled) d.ApplyConcertHallPreset(); }
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

    private static bool IsXiaomiDevice()
    {
        var m = Android.OS.Build.Manufacturer ?? "";
        return m.Equals("Xiaomi", StringComparison.OrdinalIgnoreCase);
    }

    private void LaunchMiSound()
    {
        try
        {
            var intents = new[]
            {
                new Intent("miui.intent.action.MISOUND_MAIN"),
                new Intent("miui.intent.action.SOUND_EFFECT"),
                new Intent(Android.Provider.Settings.ActionSettings).PutExtra(":android:show_fragment", "com.miui.misound"),
            };
            foreach (var intent in intents)
            {
                if (intent.ResolveActivity(Context.PackageManager!) != null)
                {
                    intent.SetFlags(ActivityFlags.NewTask); Context.StartActivity(intent); return;
                }
            }
            var launchIntent = Context.PackageManager!.GetLaunchIntentForPackage("com.miui.misound");
            if (launchIntent != null) { launchIntent.SetFlags(ActivityFlags.NewTask); Context.StartActivity(launchIntent); }
            else Toast.MakeText(Context, "未找到音质音效设置", ToastLength.Short)?.Show();
        }
        catch (Exception ex) { Android.Util.Log.Warn("CatClaw.SFX", $"打开 MiSound 失败: {ex.Message}"); Toast.MakeText(Context, "打开 MiSound 失败", ToastLength.Short)?.Show(); }
    }

    #endregion

    #region Persistence

    private void SaveSettings()
    {
        try
        {
            var prefs = Context.GetSharedPreferences(PrefsName, FileCreationMode.Private);
            var editor = prefs.Edit();

            // EQ
            editor.PutBoolean(KeyEqEnabled, _eqSwitch?.Checked ?? false);
            editor.PutInt(KeyPreset, (_presetSpinner?.SelectedItemPosition ?? 0) - 1);
            for (int i = 0; i < EqBandProcessor.Bands; i++)
                editor.PutInt(KeyBandLevelPrefix + i, _eqProcessor.GetBandLevelMillibels(i));

            // Bass / Vir
            editor.PutBoolean(KeyBassEnabled, _bassSwitch?.Checked ?? false);
            editor.PutInt(KeyBassStrength, _bassSeekBar?.Progress ?? 0);
            editor.PutBoolean(KeyVirEnabled, _virSwitch?.Checked ?? false);
            editor.PutInt(KeyVirStrength, _virSeekBar?.Progress ?? 0);

            // Compressor
            editor.PutBoolean(KeyCompEnabled, _compSwitch?.Checked ?? false);
            editor.PutInt(KeyCompThreshold, _compThresholdSeek?.Progress ?? 0);
            editor.PutInt(KeyCompRatio, _compRatioSeek?.Progress ?? 0);
            editor.PutInt(KeyCompAttack, _compAttackSeek?.Progress ?? 0);
            editor.PutInt(KeyCompRelease, _compReleaseSeek?.Progress ?? 0);
            editor.PutInt(KeyCompMakeup, _compMakeupSeek?.Progress ?? 0);

            // MAX Audio
            editor.PutBoolean(KeyMaxAudioEnabled, _maxAudioSwitch?.Checked ?? false);

            // Reverb
            editor.PutBoolean(KeyReverbEnabled, _reverbSwitch?.Checked ?? false);
            editor.PutInt(KeyReverbPreset, _selectedReverbPreset);
            editor.PutInt(KeyReverbDecay, _reverbDecaySeek?.Progress ?? 0);
            editor.PutInt(KeyReverbWetDry, _reverbWetDrySeek?.Progress ?? 0);
            editor.PutInt(KeyReverbPreDelay, _reverbPreDelaySeek?.Progress ?? 0);
            editor.PutInt(KeyReverbDamping, _reverbDampingSeek?.Progress ?? 0);

            // Widener
            editor.PutBoolean(KeyWidenerEnabled, _widenerSwitch?.Checked ?? false);
            editor.PutInt(KeyWidenerWidth, _widenerWidthSeek?.Progress ?? 0);

            // Saturation
            editor.PutBoolean(KeySatEnabled, _satSwitch?.Checked ?? false);
            editor.PutInt(KeySatDrive, _satDriveSeek?.Progress ?? 0);
            editor.PutInt(KeySatWarmth, _satWarmthSeek?.Progress ?? 0);
            editor.PutInt(KeySatTone, _satToneSeek?.Progress ?? 0);

            // De-esser
            editor.PutBoolean(KeyDeesserEnabled, _deesserSwitch?.Checked ?? false);
            editor.PutInt(KeyDeesserFreq, _deesserFreqSeek?.Progress ?? 0);
            editor.PutInt(KeyDeesserSens, _deesserSensSeek?.Progress ?? 0);
            editor.PutInt(KeyDeesserReduction, _deesserReductionSeek?.Progress ?? 0);

            // Limiter
            editor.PutBoolean(KeyLimiterEnabled, _limiterSwitch?.Checked ?? false);
            editor.PutInt(KeyLimiterCeiling, _limiterCeilingSeek?.Progress ?? 0);
            editor.PutInt(KeyLimiterRelease, _limiterReleaseSeek?.Progress ?? 0);

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
            if (saved != int.MinValue) _eqProcessor.SetBandLevelMillibels(i, (short)saved);
        }
        UpdateSlidersFromProcessor();
    }

    #endregion

    protected override void OnStop() { SaveSettings(); base.OnStop(); }

    public void Release()
    {
        try { BassClass.GetMethod("release").Invoke(_bass); } catch { }
        try { VirClass.GetMethod("release").Invoke(_virtual); } catch { }
        try { HwReverbClass.GetMethod("release").Invoke(_hwReverb); } catch { }
        _bass = _virtual = _hwReverb = null;
    }
}
