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
/// 10段软件均衡器对话框 + 低音增强 + 环绕声。
/// 使用 EqBandProcessor（Biquad 峰值滤波器）实现 10 段软件 EQ，
/// BassBoost / Virtualizer 通过 JNI 反射调用 Android 硬件音频效果。
/// </summary>
public class EqualizerDialog : Dialog
{
    private readonly IAudioPlayerService _playerService;
    private readonly EqBandProcessor _eqProcessor;
    private int _audioSessionId;

    // JNI 硬件效果
    private Java.Lang.Object? _bass;
    private Java.Lang.Object? _virtual;

    // UI
    private Switch? _eqSwitch, _bassSwitch, _virSwitch;
    private Spinner? _presetSpinner;
    private LinearLayout? _slidersContainer;
    private SeekBar? _bassSeekBar, _virSeekBar;
    private TextView? _bassValue, _virValue;
    private bool _isInitializing = true;

    // 10段滑块引用
    private readonly VerticalSliderView?[] _bandSliders = new VerticalSliderView[10];
    private readonly TextView?[] _bandDbTexts = new TextView[10];

    private const string PrefsName = "catclaw_eq10_prefs";
    private const string KeyEqEnabled = "eq_enabled";
    private const string KeyPreset = "eq_preset";
    private const string KeyBandLevelPrefix = "eq_band_";
    private const string KeyBassEnabled = "bass_enabled";
    private const string KeyBassStrength = "bass_strength";
    private const string KeyVirEnabled = "vir_enabled";
    private const string KeyVirStrength = "vir_strength";

    /// <summary>10段预设均衡器曲线（单位：millibels，-15dB~+15dB = -1500~+1500）</summary>
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
        ["电子"] =        [  400,  300,    0,   -  200, -400, -200,  100,  300,  400,  400],
        ["小扬声器"] =    [  400,  300,  200,  100, -100, -200, -100,  100,  200,  300],
        ["现场"] =        [  -100,  200,  300,  200,  100,  100,  200,  300,  200,  100],
    };

    private static readonly List<string> PresetNames = new(PresetCurves.Keys);

    static EqualizerDialog()
    {
        // "自定义" 在最前面，"普通" 排第二
        PresetNames.Sort((a, b) =>
        {
            if (a == "普通") return -1;
            if (b == "普通") return 1;
            return string.Compare(a, b, StringComparison.Ordinal);
        });
    }

    public EqualizerDialog(Activity context, IAudioPlayerService playerService)
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

    private View CreateView()
    {
        var root = new LinearLayout(Context)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        root.SetBackgroundColor(Color.ParseColor("#E6222222"));
        int pad = Dp(12);

        // ===== 均衡器标题 =====
        root.AddView(MakeSectionHeader("均衡器 (10段)", out _eqSwitch));

        // 预设选择
        var presetRow = new LinearLayout(Context)
        {
            Orientation = Orientation.Horizontal,
            LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        presetRow.SetGravity(GravityFlags.CenterVertical);
        presetRow.SetPadding(pad, 0, pad, Dp(4));

        _presetSpinner = new Spinner(Context)
        {
            LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f)
        };
        _presetSpinner.ItemSelected += OnPresetSelected;
        presetRow.AddView(_presetSpinner);
        root.AddView(presetRow);

        // 10段垂直滑块（水平滚动）
        var scrollView = new HorizontalScrollView(Context)
        {
            LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(220))
        };
        _slidersContainer = new LinearLayout(Context)
        {
            Orientation = Orientation.Horizontal,
            LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.MatchParent)
        };
        _slidersContainer.SetGravity(GravityFlags.CenterHorizontal);
        scrollView.AddView(_slidersContainer);
        root.AddView(scrollView);

        // EQ 开关监听
        _eqSwitch.SetOnCheckedChangeListener(new EqSwitchListener(this));

        // ===== 低音增强 =====
        root.AddView(MakeSectionHeader("低音增强", out _bassSwitch));

        var bassRow = new LinearLayout(Context)
        {
            Orientation = Orientation.Horizontal,
            LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        bassRow.SetGravity(GravityFlags.CenterVertical);
        bassRow.SetPadding(pad, 0, pad, Dp(8));

        _bassSeekBar = new SeekBar(Context)
        {
            LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f),
            Max = 1000
        };
        _bassSeekBar.SetOnSeekBarChangeListener(new BassListener(this));
        _bassValue = new TextView(Context)
        {
            LayoutParameters = new LinearLayout.LayoutParams(Dp(50), ViewGroup.LayoutParams.WrapContent)
        };
        _bassValue.SetTextColor(Color.White);
        _bassValue.TextSize = 13f;
        _bassValue.Gravity = GravityFlags.Center;
        _bassValue.Text = "0%";

        bassRow.AddView(_bassSeekBar);
        bassRow.AddView(_bassValue);
        root.AddView(bassRow);
        _bassSwitch.SetOnCheckedChangeListener(new BassSwitchListener(this));

        // ===== 环绕声 =====
        root.AddView(MakeSectionHeader("环绕声", out _virSwitch));

        var virRow = new LinearLayout(Context)
        {
            Orientation = Orientation.Horizontal,
            LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        virRow.SetGravity(GravityFlags.CenterVertical);
        virRow.SetPadding(pad, 0, pad, Dp(16));

        _virSeekBar = new SeekBar(Context)
        {
            LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f),
            Max = 1000
        };
        _virSeekBar.SetOnSeekBarChangeListener(new VirListener(this));
        _virValue = new TextView(Context)
        {
            LayoutParameters = new LinearLayout.LayoutParams(Dp(50), ViewGroup.LayoutParams.WrapContent)
        };
        _virValue.SetTextColor(Color.White);
        _virValue.TextSize = 13f;
        _virValue.Gravity = GravityFlags.Center;
        _virValue.Text = "0%";

        virRow.AddView(_virSeekBar);
        virRow.AddView(_virValue);
        root.AddView(virRow);
        _virSwitch.SetOnCheckedChangeListener(new VirSwitchListener(this));

        return root;
    }

    private LinearLayout MakeSectionHeader(string title, out Switch sw)
    {
        var row = new LinearLayout(Context)
        {
            Orientation = Orientation.Horizontal,
            LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        row.SetGravity(GravityFlags.CenterVertical);
        row.SetPadding(Dp(16), Dp(10), Dp(16), 0);

        var tv = new TextView(Context)
        {
            Text = title,
            LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f)
        };
        tv.SetTextColor(Color.White);
        tv.TextSize = 15f;
        tv.SetTypeface(null, Android.Graphics.TypefaceStyle.Bold);

        sw = new Switch(Context)
        {
            LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
        };

        row.AddView(tv);
        row.AddView(sw);
        return row;
    }

    private int Dp(int dp) => (int)(dp * (Context.Resources?.DisplayMetrics?.Density ?? 1f));

    #endregion

    #region JNI Helpers (BassBoost / Virtualizer)

    private static Java.Lang.Class? _bassClass, _virClass;

    private static Java.Lang.Class BassClass => _bassClass ??= Java.Lang.Class.ForName("android.media.audiofx.BassBoost");
    private static Java.Lang.Class VirClass => _virClass ??= Java.Lang.Class.ForName("android.media.audiofx.Virtualizer");

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

    private void BassSetEnabled(bool enabled) => JniCallBool(_bass, BassClass, "setEnabled", enabled);
    private void BassSetStrength(short strength) => JniCallShort(_bass, BassClass, "setStrength", strength);
    private void VirSetEnabled(bool enabled) => JniCallBool(_virtual, VirClass, "setEnabled", enabled);
    private void VirSetStrength(short strength) => JniCallShort(_virtual, VirClass, "setStrength", strength);

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
                {
                    ApplyCurve(curve);
                }
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

            _isInitializing = false;
        }
        catch (Exception ex)
        {
            Android.Util.Log.Warn("CatClaw.EQ", $"均衡器初始化失败: {ex.Message}\n{ex.StackTrace}");
            ShowMessage("均衡器初始化失败");
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
            string freqLabel = freqHz >= 1000 ? $"{freqHz / 1000f:F1}k" : $"{freqHz}";
            var freqText = new TextView(Context)
            {
                Text = freqLabel,
                LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
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
                LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
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

        // 更新 dB 标签
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
        for (int i = 0; i < 10 && i < curve.Length; i++)
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
        for (int i = 0; i < 10; i++)
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

    // EQ 开关
    private class EqSwitchListener : Java.Lang.Object, CompoundButton.IOnCheckedChangeListener
    {
        private readonly WeakReference<EqualizerDialog> _ref;
        public EqSwitchListener(EqualizerDialog d) => _ref = new(d);
        public void OnCheckedChanged(CompoundButton? b, bool enabled)
        {
            if (!_ref.TryGetTarget(out var d)) return;
            d._eqProcessor.Enabled = enabled;
            d.SaveSettings();
        }
    }

    // BassBoost 开关
    private class BassSwitchListener : Java.Lang.Object, CompoundButton.IOnCheckedChangeListener
    {
        private readonly WeakReference<EqualizerDialog> _ref;
        public BassSwitchListener(EqualizerDialog d) => _ref = new(d);
        public void OnCheckedChanged(CompoundButton? b, bool enabled)
        {
            if (!_ref.TryGetTarget(out var d) || d._bass == null) return;
            d.BassSetEnabled(enabled);
            if (enabled) d.BassSetStrength((short)d._bassSeekBar!.Progress);
            d.SaveSettings();
        }
    }

    // Virtualizer 开关
    private class VirSwitchListener : Java.Lang.Object, CompoundButton.IOnCheckedChangeListener
    {
        private readonly WeakReference<EqualizerDialog> _ref;
        public VirSwitchListener(EqualizerDialog d) => _ref = new(d);
        public void OnCheckedChanged(CompoundButton? b, bool enabled)
        {
            if (!_ref.TryGetTarget(out var d) || d._virtual == null) return;
            d.VirSetEnabled(enabled);
            if (enabled) d.VirSetStrength((short)d._virSeekBar!.Progress);
            d.SaveSettings();
        }
    }

    // Bass 强度
    private class BassListener : Java.Lang.Object, SeekBar.IOnSeekBarChangeListener
    {
        private readonly WeakReference<EqualizerDialog> _ref;
        public BassListener(EqualizerDialog d) => _ref = new(d);
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

    // Virtualizer 强度
    private class VirListener : Java.Lang.Object, SeekBar.IOnSeekBarChangeListener
    {
        private readonly WeakReference<EqualizerDialog> _ref;
        public VirListener(EqualizerDialog d) => _ref = new(d);
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

            for (int i = 0; i < 10; i++)
                editor.PutInt(KeyBandLevelPrefix + i, _eqProcessor.GetBandLevelMillibels(i));

            editor.Apply();
        }
        catch { }
    }

    private void RestoreBandLevels()
    {
        var prefs = Context.GetSharedPreferences(PrefsName, FileCreationMode.Private);
        for (int i = 0; i < 10; i++)
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

    private void ShowMessage(string msg)
    {
        if (_slidersContainer == null) return;
        _slidersContainer.RemoveAllViews();
        var tv = new TextView(Context)
        {
            Text = msg,
            LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        tv.SetTextColor(Color.ParseColor("#AAFFFFFF"));
        tv.TextSize = 14f;
        tv.Gravity = GravityFlags.Center;
        tv.SetPadding(0, Dp(32), 0, Dp(32));
        _slidersContainer.AddView(tv);
    }

    protected override void OnStop()
    {
        SaveSettings();
        base.OnStop();
    }

    public void Release()
    {
        try { BassClass.GetMethod("release").Invoke(_bass); } catch { }
        try { VirClass.GetMethod("release").Invoke(_virtual); } catch { }
        _bass = _virtual = null;
    }
}