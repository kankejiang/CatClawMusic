using Android.Content;
using Android.Media.Audiofx;

namespace CatClawMusic.UI.Services;

/// <summary>
/// 音效管理器 — 基于预设的音效方案，管理 Equalizer、BassBoost、Virtualizer、PresetReverb。
/// 用户只需选择一个预设，自动配置所有底层效果。
/// </summary>
public class SoundEffectManager : IDisposable
{
    private Equalizer? _equalizer;
    private BassBoost? _bassBoost;
    private Virtualizer? _virtualizer;
    private PresetReverb? _presetReverb;
    private int _sessionId;
    private string _currentPreset = "原声";

    /// <summary>当前选中的预设名称</summary>
    public string CurrentPreset => _currentPreset;

    public class Preset
    {
        public string Name { get; init; } = "";
        public string Icon { get; init; } = "";
        public string Description { get; init; } = "";
        /// <summary>PresetReverb 预设值 (null=不使用), 0=NONE,1=SMALLROOM,2=MEDIUMROOM,3=LARGEROOM,4=MEDIUMHALL,5=LARGEHALL,6=PLATE</summary>
        public short? ReverbPreset { get; init; }
        public short VirtualizerStrength { get; init; }
        public short BassBoostStrength { get; init; }
        public short[]? EqCurve { get; init; }
    }

    public static readonly Preset[] Presets = new[]
    {
        new Preset { Name = "原声", Icon = "\U0001F3B5", Description = "关闭所有音效，还原真实声音" },
        new Preset { Name = "杜比全景声", Icon = "\U0001F310", Description = "沉浸式三维空间音效，增强临场感",
            ReverbPreset = 5, VirtualizerStrength = 1000, BassBoostStrength = 400 },
        new Preset { Name = "音乐厅", Icon = "\U0001F3DB", Description = "宽广的音乐厅混响，空间感十足",
            ReverbPreset = 5, VirtualizerStrength = 600 },
        new Preset { Name = "现场演出", Icon = "\U0001F3A4", Description = "身临其境的现场演出氛围",
            ReverbPreset = 4, VirtualizerStrength = 800, BassBoostStrength = 200 },
        new Preset { Name = "环绕立体声", Icon = "\U0001F50A", Description = "增强声场宽度与立体分离度",
            VirtualizerStrength = 1000 },
        new Preset { Name = "低音增强", Icon = "\U0001F509", Description = "强劲低音，震撼有力",
            BassBoostStrength = 1000, ReverbPreset = 1 },
        new Preset { Name = "高音增强", Icon = "\U0001F3BC", Description = "提升高音清晰度与细节表现",
            EqCurve = new short[]{0,0,0,300,600} },
        new Preset { Name = "人声增强", Icon = "\U0001F399", Description = "突出人声，歌词更清晰",
            EqCurve = new short[]{-100,200,400,100,0} },
        new Preset { Name = "电子", Icon = "\U0001F3A7", Description = "适合电子音乐的能量感",
            EqCurve = new short[]{400,0,-300,300,400}, VirtualizerStrength = 400 },
        new Preset { Name = "摇滚", Icon = "\U0001F3B8", Description = "摇滚乐的强劲冲击力",
            EqCurve = new short[]{400,-100,-200,300,400}, BassBoostStrength = 300 },
        new Preset { Name = "流行", Icon = "\U0001F3B6", Description = "流行音乐的均衡听感",
            EqCurve = new short[]{200,300,-100,200,200} },
        new Preset { Name = "爵士", Icon = "\U0001F3B7", Description = "爵士乐的温暖氛围",
            EqCurve = new short[]{200,100,200,200,300}, ReverbPreset = 1 },
        new Preset { Name = "古典", Icon = "\U0001F3BB", Description = "古典音乐的宽广动态范围",
            ReverbPreset = 4, VirtualizerStrength = 400, EqCurve = new short[]{200,100,-100,100,200} },
    };

    private const string PrefsName = "catclaw_sfx_prefs";
    private const string KeyCurrentPreset = "sfx_current_preset";

    public SoundEffectManager()
    {
        var prefs = global::Android.App.Application.Context
            .GetSharedPreferences(PrefsName, FileCreationMode.Private);
        _currentPreset = prefs.GetString(KeyCurrentPreset, "原声") ?? "原声";
    }

    public void Attach(int sessionId)
    {
        if (_sessionId == sessionId && _equalizer != null) return;
        Release();
        _sessionId = sessionId;
        if (sessionId <= 0) return;

        try
        {
            _equalizer = new Equalizer(0, sessionId);
            Android.Util.Log.Info("CatClaw.SFX", $"SoundEffectManager attached: sessionId={sessionId}, bands={_equalizer.NumberOfBands}");
        }
        catch (Exception ex)
        {
            Android.Util.Log.Warn("CatClaw.SFX", $"Equalizer create failed: {ex.Message}");
            _equalizer?.Dispose();
            _equalizer = null;
        }

        try { _bassBoost = new BassBoost(0, sessionId); }
        catch (Exception ex) { Android.Util.Log.Warn("CatClaw.SFX", $"BassBoost not supported: {ex.Message}"); }

        try { _virtualizer = new Virtualizer(0, sessionId); }
        catch (Exception ex) { Android.Util.Log.Warn("CatClaw.SFX", $"Virtualizer not supported: {ex.Message}"); }

        try { _presetReverb = new PresetReverb(0, sessionId); }
        catch (Exception ex) { Android.Util.Log.Warn("CatClaw.SFX", $"PresetReverb not supported: {ex.Message}"); }

        // 恢复上次的预设
        ApplyPresetCore(_currentPreset);
    }

    /// <summary>应用音效预设</summary>
    public void ApplyPreset(string presetName)
    {
        _currentPreset = presetName;
        ApplyPresetCore(presetName);
        SaveCurrentPreset();
    }

    private void ApplyPresetCore(string presetName)
    {
        var preset = Array.Find(Presets, p => p.Name == presetName) ?? Presets[0];

        // EQ
        if (_equalizer != null)
        {
            if (preset.EqCurve != null)
            {
                _equalizer.SetEnabled(true);
                var bandCount = Math.Min(_equalizer.NumberOfBands, preset.EqCurve.Length);
                for (short i = 0; i < bandCount; i++)
                {
                    try { _equalizer.SetBandLevel(i, preset.EqCurve[i]); } catch { }
                }
            }
            else
            {
                // 非EQ预设：重置所有频段为0
                _equalizer.SetEnabled(false);
                for (short i = 0; i < _equalizer.NumberOfBands; i++)
                {
                    try { _equalizer.SetBandLevel(i, 0); } catch { }
                }
            }
        }

        // BassBoost
        try
        {
            if (_bassBoost != null)
            {
                var bbStrength = preset.BassBoostStrength;
                if (bbStrength > 0)
                {
                    _bassBoost.SetEnabled(true);
                    _bassBoost.SetStrength(bbStrength);
                }
                else
                {
                    _bassBoost.SetEnabled(false);
                }
            }
        }
        catch (Exception ex) { Android.Util.Log.Warn("CatClaw.SFX", $"BassBoost failed: {ex.Message}"); }

        // Virtualizer
        try
        {
            if (_virtualizer != null)
            {
                var virStrength = preset.VirtualizerStrength;
                if (virStrength > 0)
                {
                    _virtualizer.SetEnabled(true);
                    _virtualizer.SetStrength(virStrength);
                }
                else
                {
                    _virtualizer.SetEnabled(false);
                }
            }
        }
        catch (Exception ex) { Android.Util.Log.Warn("CatClaw.SFX", $"Virtualizer failed: {ex.Message}"); }

        // PresetReverb
        try
        {
            if (_presetReverb != null)
            {
                if (preset.ReverbPreset.HasValue)
                {
                    _presetReverb.Preset = preset.ReverbPreset.Value;
                    _presetReverb.SetEnabled(true);
                }
                else
                {
                    _presetReverb.SetEnabled(false);
                }
            }
        }
        catch (Exception ex) { Android.Util.Log.Warn("CatClaw.SFX", $"PresetReverb failed: {ex.Message}"); }

        Android.Util.Log.Info("CatClaw.SFX", $"Applied preset: {presetName}");
    }

    public void Release()
    {
        if (_equalizer != null)
        {
            try { _equalizer.SetEnabled(false); } catch { }
            try { _equalizer.Release(); } catch { }
            try { _equalizer.Dispose(); } catch { }
            _equalizer = null;
        }
        if (_bassBoost != null)
        {
            try { _bassBoost.SetEnabled(false); } catch { }
            try { _bassBoost.Release(); } catch { }
            try { _bassBoost.Dispose(); } catch { }
            _bassBoost = null;
        }
        if (_virtualizer != null)
        {
            try { _virtualizer.SetEnabled(false); } catch { }
            try { _virtualizer.Release(); } catch { }
            try { _virtualizer.Dispose(); } catch { }
            _virtualizer = null;
        }
        if (_presetReverb != null)
        {
            try { _presetReverb.SetEnabled(false); } catch { }
            try { _presetReverb.Release(); } catch { }
            try { _presetReverb.Dispose(); } catch { }
            _presetReverb = null;
        }
        _sessionId = 0;
    }

    public void Dispose() => Release();

    private void SaveCurrentPreset()
    {
        try
        {
            global::Android.App.Application.Context
                .GetSharedPreferences(PrefsName, FileCreationMode.Private)
                .Edit()!.PutString(KeyCurrentPreset, _currentPreset)!.Apply();
        }
        catch { }
    }
}