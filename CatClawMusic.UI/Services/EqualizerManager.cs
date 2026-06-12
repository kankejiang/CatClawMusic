using Android.Content;
using Android.Media.Audiofx;

namespace CatClawMusic.UI.Services;

/// <summary>
/// 基于 Android 内置 Equalizer 的均衡器管理器。
/// UI 使用均布的 5 段标准频率，直接一对一映射到系统硬件 EQ 频段。
/// </summary>
public class EqualizerManager : IDisposable
{
    private Equalizer? _equalizer;
    private int _sessionId;
    private bool _enabled;
    private int _systemBandCount;

    /// <summary>UI 显示的均布 5 段标准频率 (Hz)</summary>
    public static readonly int[] StandardFreqs = { 60, 230, 910, 4000, 14000 };
    /// <summary>UI 频段数量（固定 5 段）</summary>
    public const int BandCount = 5;

    /// <summary>UI 频段增益值 (millibels)</summary>
    private readonly short[] _bandLevels = new short[BandCount];

    private const string PrefsName = "catclaw_eq_prefs";
    private const string KeyEqEnabled = "eq_enabled";
    private const string KeyBandPrefix = "eq_band_";

    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            try { _equalizer?.SetEnabled(value); }
            catch (Exception ex) { Android.Util.Log.Warn("CatClaw.EQ", $"setEnabled failed: {ex.Message}"); }
            SaveEnabled();
        }
    }

    public int SystemBandCount => _systemBandCount;

    public EqualizerManager()
    {
        var prefs = global::Android.App.Application.Context
            .GetSharedPreferences(PrefsName, FileCreationMode.Private);
        _enabled = prefs.GetBoolean(KeyEqEnabled, false);
        for (int i = 0; i < BandCount; i++)
            _bandLevels[i] = (short)prefs.GetInt(KeyBandPrefix + i, 0);
    }

    /// <summary>绑定到音频会话</summary>
    public void Attach(int sessionId)
    {
        if (_sessionId == sessionId && _equalizer != null) return;
        Release();
        _sessionId = sessionId;
        if (sessionId <= 0) return;

        try
        {
            _equalizer = new Equalizer(0, sessionId);
            _systemBandCount = _equalizer.NumberOfBands;

            var freqInfo = new System.Text.StringBuilder();
            for (int i = 0; i < _systemBandCount && i < 10; i++)
            {
                if (i > 0) freqInfo.Append(", ");
                freqInfo.Append(_equalizer.GetCenterFreq((short)i));
                freqInfo.Append("Hz");
            }

            Android.Util.Log.Info("CatClaw.EQ",
                $"Equalizer attached: sysBands={_systemBandCount} " +
                $"freqs=[{freqInfo}], " +
                $"device={Android.OS.Build.Manufacturer} {Android.OS.Build.Model}");

            if (_enabled)
                _equalizer.SetEnabled(true);
            PushAllToSystem();
        }
        catch (Exception ex)
        {
            Android.Util.Log.Warn("CatClaw.EQ", $"Equalizer attach failed: {ex.Message}");
            _equalizer?.Dispose();
            _equalizer = null;
        }
    }

    /// <summary>设置 UI 某段的增益，直接映射到对应索引的系统频段</summary>
    public void SetBandLevel(int bandIdx, short millibels)
    {
        if (bandIdx < 0 || bandIdx >= BandCount) return;
        _bandLevels[bandIdx] = millibels;
        if (_equalizer != null && bandIdx < _systemBandCount)
        {
            try { _equalizer.SetBandLevel((short)bandIdx, millibels); }
            catch (Exception ex) { Android.Util.Log.Warn("CatClaw.EQ", $"setBandLevel [{bandIdx}] failed: {ex.Message}"); }
        }
        SaveBandLevel(bandIdx, millibels);
    }

    /// <summary>获取 UI 某段的增益值</summary>
    public short GetBandLevel(int bandIdx)
        => (bandIdx >= 0 && bandIdx < BandCount) ? _bandLevels[bandIdx] : (short)0;

    /// <summary>将所有增益推送到系统 EQ</summary>
    private void PushAllToSystem()
    {
        if (_equalizer == null) return;
        for (int i = 0; i < BandCount && i < _systemBandCount; i++)
        {
            try { _equalizer.SetBandLevel((short)i, _bandLevels[i]); }
            catch (Exception ex) { Android.Util.Log.Warn("CatClaw.EQ", $"pushAll[{i}] failed: {ex.Message}"); }
        }
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
        _sessionId = 0;
    }

    public void Dispose() => Release();

    #region Persistence

    private void SaveEnabled()
    {
        try
        {
            global::Android.App.Application.Context
                .GetSharedPreferences(PrefsName, FileCreationMode.Private)
                .Edit()!.PutBoolean(KeyEqEnabled, _enabled)!.Apply();
        }
        catch { }
    }

    private void SaveBandLevel(int band, short millibels)
    {
        try
        {
            global::Android.App.Application.Context
                .GetSharedPreferences(PrefsName, FileCreationMode.Private)
                .Edit()!.PutInt(KeyBandPrefix + band, millibels)!.Apply();
        }
        catch { }
    }

    #endregion
}