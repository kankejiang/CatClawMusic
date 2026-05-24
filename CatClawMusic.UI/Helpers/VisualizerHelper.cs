using Android.Media.Audiofx;
using ALog = Android.Util.Log;

namespace CatClawMusic.UI.Helpers;

/// <summary>Android Visualizer 频谱可视化辅助类，从音频会话捕获 FFT 数据并映射到 64 个频带</summary>
public class VisualizerHelper : Java.Lang.Object
{
    private Visualizer? _visualizer;
    private bool _enabled;

    /// <summary>频谱数据更新事件，返回 64 个频带的归一化强度值（0~1）</summary>
    public event Action<float[]>? SpectrumUpdated;

    /// <summary>是否已启用可视化</summary>
    public bool IsEnabled => _enabled;

    /// <summary>启动可视化，绑定到指定音频会话 ID</summary>
    /// <param name="audioSessionId">音频会话 ID（MediaPlayer 或 ExoPlayer 的 AudioSessionId）</param>
    public void Start(int audioSessionId)
    {
        Stop();

        if (audioSessionId == 0)
        {
            ALog.Warn("CatClaw", "[CatClaw] Visualizer: audioSessionId=0, cannot start");
            return;
        }

        try
        {
            _visualizer = new Visualizer(audioSessionId);
            _visualizer.SetCaptureSize(1024);
            _visualizer.SetDataCaptureListener(
                new CaptureListener(this),
                Visualizer.MaxCaptureRate,
                false,
                true);
            _visualizer.SetEnabled(true);
            _enabled = true;
            ALog.Debug("CatClaw", $"[CatClaw] Visualizer started, sessionId={audioSessionId}, captureSize={_visualizer.CaptureSize}");
        }
        catch (Exception ex)
        {
            ALog.Warn("CatClaw", $"[CatClaw] Visualizer start failed: {ex.Message}");
            ReleaseInternal();
        }
    }

    /// <summary>停止可视化并释放资源</summary>
    public void Stop()
    {
        if (_visualizer != null)
        {
            try
            {
                _visualizer.SetEnabled(false);
            }
            catch { }
            ReleaseInternal();
        }
        _enabled = false;
    }

    private void ReleaseInternal()
    {
        try { _visualizer?.Release(); } catch { }
        _visualizer = null;
    }

    private int _samplingRate;
    private int _fftCount;
    private const int Bands = 64;
    private readonly float[] _smoothed = new float[Bands];
    private readonly float[] _prevSmoothed = new float[Bands];

    private void OnFftData(byte[] fft)
    {
        int n = fft.Length / 2;
        if (n < 2) return;

        int sr = _samplingRate > 0 ? _samplingRate / 1000 : 44100;
        float binHz = (float)sr / fft.Length;
        float nyquist = sr / 2f;

        var mags = new float[n];
        for (int k = 1; k < n; k++)
        {
            float real = (sbyte)fft[2 * k];
            float imag = (sbyte)fft[2 * k + 1];
            mags[k] = (float)Math.Sqrt(real * real + imag * imag);
        }

        var smoothedMags = new float[n];
        for (int k = 1; k < n - 1; k++)
        {
            smoothedMags[k] = mags[k - 1] * 0.25f + mags[k] * 0.5f + mags[k + 1] * 0.25f;
        }
        smoothedMags[1] = mags[1] * 0.75f + mags[2] * 0.25f;
        smoothedMags[n - 1] = mags[n - 2] * 0.25f + mags[n - 1] * 0.75f;

        float fMin = binHz;
        float fMax = Math.Min(nyquist, 14000f);

        var bandEdges = BuildBandEdges(fMin, fMax, Bands, binHz, n);

        for (int b = 0; b < Bands; b++)
        {
            int binLo = bandEdges[b];
            int binHi = bandEdges[b + 1];
            if (binHi < binLo) binHi = binLo;

            float sumSq = 0;
            int count = 0;
            for (int i = binLo; i <= binHi && i < n; i++)
            {
                sumSq += smoothedMags[i] * smoothedMags[i];
                count++;
            }

            float rms = count > 0 ? (float)Math.Sqrt(sumSq / count) : 0;
            if (rms < 5f) rms = 0f;

            float normalized = Math.Clamp(rms / 70f, 0f, 1f);

            _prevSmoothed[b] = _smoothed[b];
            if (normalized > _smoothed[b])
                _smoothed[b] += (normalized - _smoothed[b]) * 0.6f;
            else
                _smoothed[b] += (normalized - _smoothed[b]) * 0.2f;
        }

        float overallEnergy = 0f;
        for (int b = 0; b < Bands; b++)
            overallEnergy += _smoothed[b];
        overallEnergy /= Bands;

        float energyScale = Math.Clamp(overallEnergy * 3f, 0f, 1f);
        if (energyScale < 1f)
        {
            for (int b = 0; b < Bands; b++)
                _smoothed[b] *= energyScale;
        }

        for (int b = 1; b < Bands - 1; b++)
        {
            _smoothed[b] = _smoothed[b] * 0.6f
                + (_prevSmoothed[b - 1] + _prevSmoothed[b] + _prevSmoothed[b + 1]) / 3f * 0.4f;
        }

        if (++_fftCount % 300 == 1)
        {
            ALog.Debug("CatClaw", $"[CatClaw] FFT diag: sr={sr}, binHz={binHz:F1}, bands={Bands}");
        }

        SpectrumUpdated?.Invoke((float[])_smoothed.Clone());
    }

    private static int[] BuildBandEdges(float fMin, float fMax, int bands, float binHz, int n)
    {
        var edges = new int[bands + 1];
        int linearBands = Math.Max(8, bands / 2);
        float linearMax = 500f;

        for (int b = 0; b <= bands; b++)
        {
            float freq;
            if (b <= linearBands)
            {
                freq = fMin + (linearMax - fMin) * b / linearBands;
            }
            else
            {
                float logMin = (float)Math.Log10(linearMax);
                float logMax = (float)Math.Log10(fMax);
                float t = (float)(b - linearBands) / (bands - linearBands);
                freq = (float)Math.Pow(10, logMin + t * (logMax - logMin));
            }
            edges[b] = Math.Clamp((int)Math.Round(freq / binHz), 1, n - 1);
        }

        for (int b = 0; b < bands; b++)
        {
            if (edges[b + 1] <= edges[b])
                edges[b + 1] = edges[b] + 1;
        }

        return edges;
    }

    private class CaptureListener : Java.Lang.Object, Visualizer.IOnDataCaptureListener
    {
        private readonly VisualizerHelper _owner;
        public CaptureListener(VisualizerHelper owner) => _owner = owner;

        public void OnWaveFormDataCapture(Visualizer? visualizer, byte[]? waveform, int samplingRate) { }

        public void OnFftDataCapture(Visualizer? visualizer, byte[]? fft, int samplingRate)
        {
            if (fft != null && fft.Length > 0)
            {
                _owner._samplingRate = samplingRate;
                _owner.OnFftData(fft);
            }
        }
    }
}
