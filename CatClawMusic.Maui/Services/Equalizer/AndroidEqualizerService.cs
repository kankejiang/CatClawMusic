#if ANDROID
using Android.Media.Audiofx;
using ALog = Android.Util.Log;

namespace CatClawMusic.Maui.Services.Equalizer;

/// <summary>Android 原生音效引擎：Equalizer + BassBoost + LoudnessEnhancer，挂载到 ExoPlayer 音频会话</summary>
public class AndroidEqualizerService : IDisposable
{
    private global::Android.Media.Audiofx.Equalizer? _equalizer;
    private BassBoost? _bassBoost;
    private LoudnessEnhancer? _loudness;
    private Virtualizer? _virtualizer;
    private int _attachedSessionId = -1;

    /// <summary>设备均衡器频段数</summary>
    public int DeviceBandCount { get; private set; }

    /// <summary>设备各频段中心频率 (Hz)</summary>
    public int[] DeviceBandFrequencies { get; private set; } = Array.Empty<int>();

    /// <summary>设备增益范围 (dB): [min, max]</summary>
    public double[] GainRangeDb { get; private set; } = { -15, 15 };

    /// <summary>是否成功挂载</summary>
    public bool IsAttached => _equalizer != null;

    /// <summary>挂载到 ExoPlayer 音频会话（重复调用安全，会话不变则跳过）</summary>
    public void AttachToSession(int audioSessionId)
    {
        if (audioSessionId == _attachedSessionId && _equalizer != null) return;
        Release();
        _attachedSessionId = audioSessionId;

        try
        {
            _equalizer = new global::Android.Media.Audiofx.Equalizer(0, audioSessionId);
            DeviceBandCount = _equalizer.NumberOfBands;

            // 读取设备频段中心频率
            DeviceBandFrequencies = new int[DeviceBandCount];
            for (short b = 0; b < DeviceBandCount; b++)
            {
                // GetCenterFreq 返回毫赫兹
                DeviceBandFrequencies[b] = _equalizer.GetCenterFreq(b) / 1000;
            }

            // 增益范围（毫贝 → 分贝）
            var range = _equalizer.GetBandLevelRange();
            if (range is { Length: 2 })
                GainRangeDb = new[] { range[0] / 100.0, range[1] / 100.0 };

            _bassBoost = new BassBoost(0, audioSessionId);
            _loudness = new LoudnessEnhancer(audioSessionId);
            _virtualizer = new Virtualizer(0, audioSessionId);

            ALog.Debug("EqualizerService", $"[EQ] 挂载会话 {audioSessionId}，设备频段数={DeviceBandCount}，频率=[{string.Join(",", DeviceBandFrequencies)}]");
        }
        catch (Exception ex)
        {
            ALog.Warn("EqualizerService", $"[EQ] 挂载失败: {ex.Message}");
            Release();
            return;
        }

        ApplySettings();
    }

    /// <summary>将 EqualizerSettings 中的设置应用到硬件音效</summary>
    public void ApplySettings()
    {
        var enabled = EqualizerSettings.Enabled;
        var gains = EqualizerSettings.GetBandGains();

        try
        {
            if (_equalizer != null)
            {
                _equalizer.SetEnabled(enabled);
                if (enabled)
                {
                    // 将 7 段 UI 增益插值映射到设备实际频段
                    for (short b = 0; b < DeviceBandCount; b++)
                    {
                        var freq = DeviceBandFrequencies[b];
                        var db = InterpolateGain(gains, freq);
                        // 钳制到设备支持范围，转换为毫贝
                        db = Math.Clamp(db, GainRangeDb[0], GainRangeDb[1]);
                        _equalizer.SetBandLevel(b, (short)(db * 100));
                    }
                }
            }

            if (_bassBoost != null)
            {
                var bassOn = enabled && EqualizerSettings.BassBoost > 0;
                _bassBoost.SetEnabled(bassOn);
                if (bassOn)
                    _bassBoost.SetStrength((short)(EqualizerSettings.BassBoost * 10)); // 0-100 → 0-1000
            }

            if (_loudness != null)
            {
                var loudOn = enabled && EqualizerSettings.Loudness > 0;
                _loudness.SetEnabled(loudOn);
                if (loudOn)
                    _loudness.SetTargetGain(EqualizerSettings.Loudness * 10); // 0-100 → 0-1000 mB
            }

            if (_virtualizer != null)
            {
                var spatialOn = EqualizerSettings.SpatialAudioEnabled;
                try
                {
                    _virtualizer.SetEnabled(spatialOn);
                    if (spatialOn)
                        _virtualizer.SetStrength(800); // 0-1000，中等强度 3D 环绕
                }
                catch (Exception ex)
                {
                    ALog.Warn("EqualizerService", $"[EQ] 空间音频设置失败: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            ALog.Warn("EqualizerService", $"[EQ] 应用设置失败: {ex.Message}");
        }
    }

    /// <summary>在 UI 频段增益之间按对数频率插值，得到指定频率的增益</summary>
    private static double InterpolateGain(double[] gains, int freq)
    {
        var freqs = EqualizerSettings.BandFrequencies;
        if (freq <= freqs[0]) return gains[0];
        if (freq >= freqs[^1]) return gains[^1];

        for (int i = 0; i < freqs.Length - 1; i++)
        {
            if (freq >= freqs[i] && freq <= freqs[i + 1])
            {
                // 对数插值更符合听感
                var t = Math.Log((double)freq / freqs[i]) / Math.Log((double)freqs[i + 1] / freqs[i]);
                return gains[i] + t * (gains[i + 1] - gains[i]);
            }
        }
        return 0;
    }

    /// <summary>释放所有音效资源</summary>
    public void Release()
    {
        try { _equalizer?.Release(); } catch { }
        try { _bassBoost?.Release(); } catch { }
        try { _loudness?.Release(); } catch { }
        try { _virtualizer?.Release(); } catch { }
        _equalizer = null;
        _bassBoost = null;
        _loudness = null;
        _virtualizer = null;
        _attachedSessionId = -1;
    }

    public void Dispose() => Release();
}
#endif
