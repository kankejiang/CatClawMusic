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
/// 音效对话框 — 栈式多页面导航结构:
/// 主页: 均衡器入口、MAX Audio、MISOUND
/// 均衡器子页: 10 段 EQ + 低音增强 + 环绕声
/// </summary>
public class SoundEffectDialog : Dialog
{
    private readonly IAudioPlayerService _playerService;
    private readonly EqualizerManager _eqManager;
    private int _audioSessionId;

    // JNI 硬件效果
    private Java.Lang.Object? _bass;
    private Java.Lang.Object? _virtual;
    private Java.Lang.Object? _presetReverb;

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
    private readonly VerticalSliderView?[] _bandSliders = new VerticalSliderView[EqualizerManager.BandCount];
    private readonly TextView?[] _bandDbTexts = new TextView[EqualizerManager.BandCount];

    // UI — 低音增强
    private Switch? _bassSwitch;
    private SeekBar? _bassSeekBar;
    private TextView? _bassValue;

    // UI — 环绕声
    private Switch? _virSwitch;
    private SeekBar? _virSeekBar;
    private TextView? _virValue;

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
    private const string KeyMaxAudioEnabled = "maxaudio_enabled";

    /// <summary>5段预设均衡器曲线（单位：millibels）</summary>
    private static readonly IReadOnlyDictionary<string, short[]> PresetCurves = new Dictionary<string, short[]>
    {
        ["普通"] =        [    0,    0,    0,    0,    0],
        ["古典"] =        [  300,  200, -100,  200,  400],
        ["舞曲"] =        [  400,  200,    0, -100,  200],
        ["平坦"] =        [    0,    0,    0,    0,    0],
        ["民谣"] =        [  200,  200, -100,    0,  200],
        ["重金属"] =      [  400,    0, -200,  300,  400],
        ["嘻哈"] =        [  400,  100,    0,  100,  100],
        ["爵士"] =        [  200,  100,  200,  200,  300],
        ["流行"] =        [  200,  300, -100,  200,  200],
        ["摇滚"] =        [  400, -100, -200,  300,  400],
        ["节奏布鲁斯"] =  [  400,  100,    0,  200,  200],
        ["拉丁"] =        [  200, -100, -100,  200,  400],
        ["乡村"] =        [  200,  100, -100,  100,  200],
        ["原声"] =        [  200,  200,  100,  200,  300],
        ["人声增强"] =    [ -100,  200,  400,  100,    0],
        ["低音增强"] =    [  600,  300,    0,    0,    0],
        ["高音增强"] =    [    0,    0,    0,  300,  600],
        ["响度"] =        [  400,  100,    0,  200,  300],
        ["电子"] =        [  400,    0, -300,  300,  400],
        ["小扬声器"] =    [  300,  200,    0,  200,  300],
        ["现场"] =        [  100,  200,  100,  200,  100],
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
        _eqManager = MainApplication.Services.GetRequiredService<EqualizerManager>();
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

        _screens["main"] = mainScreen;
        _screens["eq"] = eqScreen;

        _screenTitles["main"] = "音效";
        _screenTitles["eq"] = "均衡器";

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

        content.AddView(MakeMenuEntry("\u266B", "均衡器", "5 Band Equalizer", (s, e) => NavigateTo("eq")));
        content.AddView(MakeDivider(dp));
        content.AddView(MakeMaxAudioSection(dp));
        if (IsXiaomiDevice())
            content.AddView(MakeMenuEntry("\u2669", "MISOUND", "Xiaomi Audio Effect", (s, e) => LaunchMiSound()));

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

        // 5-band sliders (均布)
        _slidersContainer = new LinearLayout(Context)
        {
            Orientation = Orientation.Horizontal,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, dp(210))
        };
        _slidersContainer.SetGravity(GravityFlags.CenterHorizontal);
        _slidersContainer.SetPadding(dp(8), 0, dp(8), 0);
        content.AddView(_slidersContainer);
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

    #endregion

    #region MAX Audio Section

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

    #region JNI Helpers (BassBoost / Virtualizer / PresetReverb)

    private static Java.Lang.Class? _bassClass, _virClass, _presetReverbClass;
    private static Java.Lang.Class BassClass => _bassClass ??= Java.Lang.Class.ForName("android.media.audiofx.BassBoost");
    private static Java.Lang.Class VirClass => _virClass ??= Java.Lang.Class.ForName("android.media.audiofx.Virtualizer");
    private static Java.Lang.Class PresetReverbClass => _presetReverbClass ??= Java.Lang.Class.ForName("android.media.audiofx.PresetReverb");

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
    private void HwReverbSetEnabled(bool enabled) => JniCallBool(_presetReverb, PresetReverbClass, "setEnabled", enabled);

    private void ApplyConcertHallPreset()
    {
        if (_presetReverb == null) return;
        try
        {
            // PresetReverb.SHORT_PRESETS → PRESET_LARGEHALL (index 2)
            // 短整型参数名: "preset", 值: PRESET_LARGEHALL
            var presetMethod = PresetReverbClass.GetMethod("setPreset", Java.Lang.Short.Type);
            presetMethod.Invoke(_presetReverb, Java.Lang.Short.ValueOf(2)); // PRESET_LARGEHALL
        }
        catch
        {
            Android.Util.Log.Warn("CatClaw.SFX", "PresetReverb.setPreset failed, trying JNI call");
            try { JniCallShort(_presetReverb, PresetReverbClass, "setPreset", 2); } catch { }
        }
    }

    #endregion

    #region Initialization

    private void Init()
    {
        try
        {
            _audioSessionId = _playerService.AudioSessionId;

            // 挂载系统硬件 EQ（5段，如果设备支持）
            if (_audioSessionId > 0)
                _eqManager.Attach(_audioSessionId);

            // 硬件 BassBoost / Virtualizer / PresetReverb（音乐厅混响）
            if (_audioSessionId > 0)
            {
                try { _bass = NewEffect(BassClass, 0, _audioSessionId); } catch { }
                try { _virtual = NewEffect(VirClass, 0, _audioSessionId); } catch { }
                try { _presetReverb = NewEffect(PresetReverbClass, 0, _audioSessionId); } catch (Exception ex) { Android.Util.Log.Warn("CatClaw.SFX", $"PresetReverb not supported: {ex.Message}"); }
            }

            CreateBandSliders();
            FillPresets();

            _isInitializing = true;
            var prefs = Context.GetSharedPreferences(PrefsName, FileCreationMode.Private);

            // === 均衡器 ===
            bool eqEnabled = _eqManager.Enabled;
            _eqSwitch!.Checked = eqEnabled;
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

            // === MAX Audio ===
            bool maxAudioEnabled = prefs.GetBoolean(KeyMaxAudioEnabled, false);
            _maxAudioSwitch!.Checked = maxAudioEnabled;
            if (maxAudioEnabled && _presetReverb != null)
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
        for (int i = 0; i < EqualizerManager.BandCount; i++)
        {
            var bandLayout = new LinearLayout(Context)
            {
                Orientation = Orientation.Vertical,
                LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f)
            };
            bandLayout.SetGravity(GravityFlags.CenterHorizontal);
            bandLayout.SetPadding(Dp(4), Dp(2), Dp(4), Dp(2));
            int freqHz = EqualizerManager.StandardFreqs[i];
            string freqLabel = freqHz >= 1000 ? $"{freqHz / 1000}k" : $"{freqHz}";
            var freqText = new TextView(Context) { Text = freqLabel, LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) };
            freqText.SetTextColor(Color.ParseColor("#AAFFFFFF")); freqText.TextSize = 11f; freqText.Gravity = GravityFlags.Center;
            var slider = new VerticalSliderView(Context) { LayoutParameters = new LinearLayout.LayoutParams(Dp(40), 0, 1f), Min = -1500, Max = 1500, Value = 0 };
            var bandIdx = i;
            slider.ValueChanged += (s, v) => OnBandSliderChanged(bandIdx, (short)(int)v);
            var dbText = new TextView(Context) { Text = "0.0", LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) };
            dbText.SetTextColor(Color.White); dbText.TextSize = 10f; dbText.Gravity = GravityFlags.Center;
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

    #endregion

    #region Band Slider Logic

    private void OnBandSliderChanged(int band, short millibels)
    {
        if (_isInitializing) return;
        _eqManager.SetBandLevel(band, millibels);
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
        for (int i = 0; i < EqualizerManager.BandCount && i < curve.Length; i++)
        {
            _eqManager.SetBandLevel(i, curve[i]);
            if (_bandSliders[i] != null) _bandSliders[i]!.Value = curve[i];
            if (_bandDbTexts[i] != null) { float db = curve[i] / 100f; _bandDbTexts[i]!.Text = $"{db:+0.0;-0.0;0.0}"; }
        }
    }

    private void UpdateSlidersFromProcessor()
    {
        for (int i = 0; i < EqualizerManager.BandCount; i++)
        {
            var mb = _eqManager.GetBandLevel(i);
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
            _eqSwitch!.Checked = true; _eqManager.Enabled = true;
            ApplyCurve(curve); SaveSettings();
        }
    }

    private class EqSwitchListener : Java.Lang.Object, CompoundButton.IOnCheckedChangeListener
    {
        private readonly WeakReference<SoundEffectDialog> _ref;
        public EqSwitchListener(SoundEffectDialog d) => _ref = new(d);
        public void OnCheckedChanged(CompoundButton? b, bool enabled)
        {
            if (!_ref.TryGetTarget(out var d)) return;
            d._eqManager.Enabled = enabled; d.SaveSettings();
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
            if (d._presetReverb != null) { d.HwReverbSetEnabled(enabled); if (enabled) d.ApplyConcertHallPreset(); }
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
            // 尝试多种 MiSound 跳转方式（MIUI / HyperOS 不同版本）
            var candidates = new (string pkg, string cls)[]
            {
                // HyperOS 2+ / MIUI 14+
                ("com.miui.misound", "com.miui.misound.MainActivity"),
                ("com.miui.misound", "com.miui.misound.MiSoundActivity"),
                ("com.miui.misound", "com.miui.misound.SoundEffectActivity"),
                // MIUI 旧版
                ("com.miui.misound", "com.miui.misound.ui.MiSoundActivity"),
                ("com.miui.misound", "com.miui.misound.ui.SoundEffectActivity"),
            };

            foreach (var (pkg, cls) in candidates)
            {
                try
                {
                    var intent = new Intent();
                    intent.SetComponent(new ComponentName(pkg, cls));
                    intent.SetFlags(ActivityFlags.NewTask);
                    if (intent.ResolveActivity(Context.PackageManager!) != null)
                    {
                        Context.StartActivity(intent);
                        return;
                    }
                }
                catch { }
            }

            // 尝试隐式 Intent
            var implicitIntents = new[]
            {
                new Intent("miui.intent.action.MISOUND_MAIN"),
                new Intent("miui.intent.action.SOUND_EFFECT"),
            };
            foreach (var intent in implicitIntents)
            {
                intent.SetFlags(ActivityFlags.NewTask);
                if (intent.ResolveActivity(Context.PackageManager!) != null)
                {
                    Context.StartActivity(intent);
                    return;
                }
            }

            // 最后尝试打开系统声音设置
            try
            {
                var soundSettings = new Intent(Android.Provider.Settings.ActionSoundSettings);
                soundSettings.SetFlags(ActivityFlags.NewTask);
                Context.StartActivity(soundSettings);
                Toast.MakeText(Context, "已跳转到系统声音设置", ToastLength.Short)?.Show();
            }
            catch
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

            // EQ
            editor.PutBoolean(KeyEqEnabled, _eqSwitch?.Checked ?? false);
            editor.PutInt(KeyPreset, (_presetSpinner?.SelectedItemPosition ?? 0) - 1);
            for (int i = 0; i < EqualizerManager.BandCount; i++)
                editor.PutInt(KeyBandLevelPrefix + i, _eqManager.GetBandLevel(i));

            // Bass / Vir
            editor.PutBoolean(KeyBassEnabled, _bassSwitch?.Checked ?? false);
            editor.PutInt(KeyBassStrength, _bassSeekBar?.Progress ?? 0);
            editor.PutBoolean(KeyVirEnabled, _virSwitch?.Checked ?? false);
            editor.PutInt(KeyVirStrength, _virSeekBar?.Progress ?? 0);

            // MAX Audio
            editor.PutBoolean(KeyMaxAudioEnabled, _maxAudioSwitch?.Checked ?? false);

            editor.Apply();
        }
        catch { }
    }

    private void RestoreBandLevels()
    {
        var prefs = Context.GetSharedPreferences(PrefsName, FileCreationMode.Private);
        for (int i = 0; i < EqualizerManager.BandCount; i++)
        {
            int saved = prefs.GetInt(KeyBandLevelPrefix + i, int.MinValue);
            if (saved != int.MinValue) _eqManager.SetBandLevel(i, (short)saved);
        }
        UpdateSlidersFromProcessor();
    }

    #endregion

    protected override void OnStop() { SaveSettings(); base.OnStop(); }

    public void Release()
    {
        try { BassClass.GetMethod("release").Invoke(_bass); } catch { }
        try { VirClass.GetMethod("release").Invoke(_virtual); } catch { }
        try { PresetReverbClass.GetMethod("release").Invoke(_presetReverb); } catch { }
        _bass = _virtual = _presetReverb = null;
    }
}