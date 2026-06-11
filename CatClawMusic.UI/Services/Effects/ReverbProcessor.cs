using System.Runtime.CompilerServices;

namespace CatClawMusic.UI.Services.Effects;

/// <summary>
/// Schroeder/Freeverb 风格混响处理器。
/// 4 个并联梳状滤波器 → 2 个串联全通滤波器，梳状反馈路径含阻尼低通。
/// L/R 使用略微不同的延迟实现立体声去相关。
/// </summary>
public class ReverbProcessor : IAudioEffect
{
    public int Priority => 20;
    public bool Enabled { get; set; }

    // === 预设枚举 ===
    public enum ReverbPreset
    {
        Studio, Room, Chamber, Hall, Cathedral, Plate, Spring
    }

    // === 参数 ===
    private ReverbPreset _preset = ReverbPreset.Hall;
    private float _decayTime = 1.8f;     // 0.1 ~ 5.0 s
    private float _wetDry = 0.3f;        // 0.0 ~ 1.0
    private float _preDelayMs = 20f;     // 0 ~ 100 ms
    private float _damping = 0.5f;       // 0.0 ~ 1.0

    private float _sampleRate = 44100f;

    // === 预设延迟时间（样本数 @44.1kHz）===
    // 每个预设: [comb1, comb2, comb3, comb4] + [allpass1, allpass2]
    private static readonly Dictionary<ReverbPreset, int[]> CombDelays = new()
    {
        [ReverbPreset.Studio]    = [1116, 1188, 1277, 1356],
        [ReverbPreset.Room]      = [1116, 1188, 1277, 1422],
        [ReverbPreset.Chamber]   = [1116, 1277, 1356, 1491],
        [ReverbPreset.Hall]      = [1116, 1356, 1491, 1617],
        [ReverbPreset.Cathedral] = [1116, 1491, 1617, 1800],
        [ReverbPreset.Plate]     = [601,  743,  901,  1031],
        [ReverbPreset.Spring]    = [501,  613,  743,  881],
    };

    private static readonly Dictionary<ReverbPreset, int[]> AllpassDelays = new()
    {
        [ReverbPreset.Studio]    = [225, 556],
        [ReverbPreset.Room]      = [225, 556],
        [ReverbPreset.Chamber]   = [225, 341],
        [ReverbPreset.Hall]      = [225, 556],
        [ReverbPreset.Cathedral] = [556, 908],
        [ReverbPreset.Plate]     = [127, 281],
        [ReverbPreset.Spring]    = [89,  199],
    };

    // 右声道去相关比例
    private const float StereoSpread = 1.07f;

    // === 内部结构 ===
    private const int NumCombs = 4;
    private const int NumAllpass = 2;

    // L 通道
    private float[][] _combBufL = null!;
    private int[] _combIdxL = null!;
    private float[] _combFilterL = null!;  // 阻尼低通状态
    private float[][] _apBufL = null!;
    private int[] _apIdxL = null!;

    // R 通道
    private float[][] _combBufR = null!;
    private int[] _combIdxR = null!;
    private float[] _combFilterR = null!;
    private float[][] _apBufR = null!;
    private int[] _apIdxR = null!;

    // Pre-delay
    private float[] _preDelayBufL = null!;
    private float[] _preDelayBufR = null!;
    private int _preDelayIdx;
    private int _preDelayLen;

    // 反馈系数（每梳状滤波器独立）
    private float[] _feedback = null!;

    // === 参数属性 ===

    public ReverbPreset Preset
    {
        get => _preset;
        set { _preset = value; RebuildBuffers(); }
    }

    public float DecayTime
    {
        get => _decayTime;
        set { _decayTime = Math.Clamp(value, 0.1f, 5f); RecomputeFeedback(); }
    }

    public float WetDry
    {
        get => _wetDry;
        set => _wetDry = Math.Clamp(value, 0f, 1f);
    }

    public float PreDelayMs
    {
        get => _preDelayMs;
        set { _preDelayMs = Math.Clamp(value, 0f, 100f); RebuildPreDelay(); }
    }

    public float Damping
    {
        get => _damping;
        set => _damping = Math.Clamp(value, 0f, 1f);
    }

    public ReverbProcessor()
    {
        RebuildBuffers();
    }

    public void SetSampleRate(float sampleRate)
    {
        if (MathF.Abs(sampleRate - _sampleRate) < 0.1f) return;
        _sampleRate = sampleRate;
        RebuildBuffers();
    }

    public void Reset()
    {
        ClearBuffers(_combBufL); ClearBuffers(_combBufR);
        ClearBuffers(_apBufL); ClearBuffers(_apBufR);
        if (_combFilterL != null) Array.Clear(_combFilterL);
        if (_combFilterR != null) Array.Clear(_combFilterR);
        if (_preDelayBufL != null) Array.Clear(_preDelayBufL);
        if (_preDelayBufR != null) Array.Clear(_preDelayBufR);
        if (_combIdxL != null) Array.Clear(_combIdxL);
        if (_combIdxR != null) Array.Clear(_combIdxR);
        if (_apIdxL != null) Array.Clear(_apIdxL);
        if (_apIdxR != null) Array.Clear(_apIdxR);
        _preDelayIdx = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Process(float[] samples, int frameCount)
    {
        if (!Enabled) return;

        float wet = _wetDry;
        float dry = 1f - wet;
        float damp = _damping;
        float dampInv = 1f - damp;

        for (int i = 0; i < frameCount; i++)
        {
            int idx = i * 2;
            float inputL = samples[idx];
            float inputR = samples[idx + 1];

            // Pre-delay
            float pdL = inputL, pdR = inputR;
            if (_preDelayLen > 0)
            {
                pdL = _preDelayBufL[_preDelayIdx];
                pdR = _preDelayBufR[_preDelayIdx];
                _preDelayBufL[_preDelayIdx] = inputL;
                _preDelayBufR[_preDelayIdx] = inputR;
                _preDelayIdx++;
                if (_preDelayIdx >= _preDelayLen) _preDelayIdx = 0;
            }

            // L 通道: 4 个并联梳状 → 求和
            float sumL = 0f;
            for (int c = 0; c < NumCombs; c++)
            {
                float delayed = _combBufL[c][_combIdxL[c]];
                // 阻尼低通
                _combFilterL[c] = delayed * dampInv + _combFilterL[c] * damp;
                // 写入新值
                _combBufL[c][_combIdxL[c]] = pdL + _combFilterL[c] * _feedback[c];
                _combIdxL[c]++;
                if (_combIdxL[c] >= _combBufL[c].Length) _combIdxL[c] = 0;
                sumL += delayed;
            }

            // R 通道
            float sumR = 0f;
            for (int c = 0; c < NumCombs; c++)
            {
                float delayed = _combBufR[c][_combIdxR[c]];
                _combFilterR[c] = delayed * dampInv + _combFilterR[c] * damp;
                _combBufR[c][_combIdxR[c]] = pdR + _combFilterR[c] * _feedback[c];
                _combIdxR[c]++;
                if (_combIdxR[c] >= _combBufR[c].Length) _combIdxR[c] = 0;
                sumR += delayed;
            }

            // L 通道: 2 个串联全通
            for (int a = 0; a < NumAllpass; a++)
            {
                float delayed = _apBufL[a][_apIdxL[a]];
                float v = sumL + delayed * 0.5f;
                _apBufL[a][_apIdxL[a]] = v;
                _apIdxL[a]++;
                if (_apIdxL[a] >= _apBufL[a].Length) _apIdxL[a] = 0;
                sumL = delayed - sumL * 0.5f;
            }

            // R 通道: 2 个串联全通
            for (int a = 0; a < NumAllpass; a++)
            {
                float delayed = _apBufR[a][_apIdxR[a]];
                float v = sumR + delayed * 0.5f;
                _apBufR[a][_apIdxR[a]] = v;
                _apIdxR[a]++;
                if (_apIdxR[a] >= _apBufR[a].Length) _apIdxR[a] = 0;
                sumR = delayed - sumR * 0.5f;
            }

            // Wet/Dry 混合
            samples[idx] = inputL * dry + sumL * wet * 0.25f;
            samples[idx + 1] = inputR * dry + sumR * wet * 0.25f;
        }
    }

    private void RebuildBuffers()
    {
        float srRatio = _sampleRate / 44100f;

        var combDelays = CombDelays[_preset];
        var apDelays = AllpassDelays[_preset];

        // L 通道梳状
        _combBufL = new float[NumCombs][];
        _combIdxL = new int[NumCombs];
        _combFilterL = new float[NumCombs];
        for (int c = 0; c < NumCombs; c++)
        {
            int len = Math.Max(1, (int)(combDelays[c] * srRatio));
            _combBufL[c] = new float[len];
        }

        // R 通道梳状（延迟 × StereoSpread）
        _combBufR = new float[NumCombs][];
        _combIdxR = new int[NumCombs];
        _combFilterR = new float[NumCombs];
        for (int c = 0; c < NumCombs; c++)
        {
            int len = Math.Max(1, (int)(combDelays[c] * StereoSpread * srRatio));
            _combBufR[c] = new float[len];
        }

        // L 通道全通
        _apBufL = new float[NumAllpass][];
        _apIdxL = new int[NumAllpass];
        for (int a = 0; a < NumAllpass; a++)
        {
            int len = Math.Max(1, (int)(apDelays[a] * srRatio));
            _apBufL[a] = new float[len];
        }

        // R 通道全通
        _apBufR = new float[NumAllpass][];
        _apIdxR = new int[NumAllpass];
        for (int a = 0; a < NumAllpass; a++)
        {
            int len = Math.Max(1, (int)(apDelays[a] * StereoSpread * srRatio));
            _apBufR[a] = new float[len];
        }

        _feedback = new float[NumCombs];
        RecomputeFeedback();
        RebuildPreDelay();
    }

    private void RecomputeFeedback()
    {
        if (_feedback == null) return;
        float srRatio = _sampleRate / 44100f;
        var combDelays = CombDelays[_preset];

        // Schroeder 公式: feedback = 10^(-3 * delaySamples / (decayTime * sampleRate))
        for (int c = 0; c < NumCombs; c++)
        {
            int delaySamples = Math.Max(1, (int)(combDelays[c] * srRatio));
            float fb = MathF.Pow(10f, -3f * delaySamples / (_decayTime * _sampleRate));
            _feedback[c] = MathF.Min(fb, 0.98f); // 安全钳位
        }
    }

    private void RebuildPreDelay()
    {
        int len = Math.Max(0, (int)(_preDelayMs / 1000f * _sampleRate));
        if (len != _preDelayLen)
        {
            _preDelayLen = len;
            _preDelayBufL = len > 0 ? new float[len] : Array.Empty<float>();
            _preDelayBufR = len > 0 ? new float[len] : Array.Empty<float>();
            _preDelayIdx = 0;
        }
    }

    private static void ClearBuffers(float[][] bufs)
    {
        if (bufs == null) return;
        for (int i = 0; i < bufs.Length; i++)
            if (bufs[i] != null) Array.Clear(bufs[i]);
    }
}
