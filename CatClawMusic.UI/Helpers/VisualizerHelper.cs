using Android.Media.Audiofx;
using ALog = Android.Util.Log;

namespace CatClawMusic.UI.Helpers;

/// <summary>Android Visualizer 频谱可视化辅助类，从音频会话捕获 FFT 数据并映射到 64 个频带</summary>
public class VisualizerHelper : Java.Lang.Object
{
    private Visualizer? _visualizer;
    private bool _enabled;
    private CaptureListener? _listener;
    private long _lastFftTicks;
    private int _currentSessionId;

    /// <summary>频谱数据更新事件，返回 64 个频带的归一化强度值（0~1）</summary>
    public event Action<float[]>? SpectrumUpdated;

    /// <summary>是否已启用可视化（C# 标志，可能滞后于原生状态）</summary>
    public bool IsEnabled => _enabled;

    /// <summary>最后一次收到 FFT 数据的时间戳（TickCount64）</summary>
    public long LastFftTicks => _lastFftTicks;

    /// <summary>当前绑定的音频会话 ID</summary>
    public int CurrentSessionId => _currentSessionId;

    /// <summary>
    /// 检查 Visualizer 是否真正存活：C# 标志为 true 且原生 Visualizer 仍为启用状态，
    /// 且最近 3 秒内收到过 FFT 数据。
    /// </summary>
    public bool IsAlive(int sessionId)
    {
        if (!_enabled || _visualizer == null) return false;
        if (sessionId != 0 && sessionId != _currentSessionId) return false;
        // 检查原生 Visualizer 是否仍处于启用状态
        try
        {
            if (!_visualizer.Enabled) return false;
        }
        catch
        {
            return false;
        }
        // 检查心跳：3 秒内是否收到过数据
        if (_lastFftTicks == 0) return true; // 刚启动还未收到数据
        var elapsed = System.Environment.TickCount64 - _lastFftTicks;
        return elapsed < 3000;
    }

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

        _currentSessionId = audioSessionId;
        _lastFftTicks = System.Environment.TickCount64;

        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                _visualizer = new Visualizer(audioSessionId);
                _visualizer.SetCaptureSize(1024);
                _listener = new CaptureListener(this);
                _visualizer.SetDataCaptureListener(
                    _listener,
                    Visualizer.MaxCaptureRate,
                    false,
                    true);
                _visualizer.SetEnabled(true);
                _enabled = true;
                ALog.Debug("CatClaw", $"[CatClaw] Visualizer started, sessionId={audioSessionId}, captureSize={_visualizer.CaptureSize}");
                return;
            }
            catch (Exception ex)
            {
                ALog.Warn("CatClaw", $"[CatClaw] Visualizer start attempt {attempt + 1} failed: {ex.Message}");
                ReleaseInternal();
                if (attempt < 2)
                    Thread.Sleep(100);
            }
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
        _listener = null;
        _lastFftTicks = 0;
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
    private float[]? _magsBuf;
    private float[]? _smoothedMagsBuf;

    private readonly float[] _outputBuf = new float[Bands];

    private void OnFftData(byte[] fft)
    {
        // 整个方法加 try/catch，防止异常冒泡到 Android 原生层导致监听器被静默停止
        try
        {
            int n = fft.Length / 2;
            if (n < 2) return;

            if (_magsBuf == null || _magsBuf.Length != n) _magsBuf = new float[n];
            if (_smoothedMagsBuf == null || _smoothedMagsBuf.Length != n) _smoothedMagsBuf = new float[n];

            int sr = _samplingRate > 0 ? _samplingRate / 1000 : 44100;
            // 防止除零：sr 最小为 1
            if (sr < 1) sr = 1;
            float binHz = (float)sr / fft.Length;
            // 防止除零：binHz 最小为一个极小正数
            if (binHz <= 0f) binHz = 0.01f;
            float nyquist = sr / 2f;

            for (int k = 1; k < n; k++)
            {
                float real = (sbyte)fft[2 * k];
                float imag = (sbyte)fft[2 * k + 1];
                _magsBuf[k] = (float)Math.Sqrt(real * real + imag * imag);
            }

            for (int k = 1; k < n - 1; k++)
            {
                _smoothedMagsBuf[k] = _magsBuf[k - 1] * 0.25f + _magsBuf[k] * 0.5f + _magsBuf[k + 1] * 0.25f;
            }
            _smoothedMagsBuf[1] = _magsBuf[1] * 0.75f + _magsBuf[2] * 0.25f;
            _smoothedMagsBuf[n - 1] = _magsBuf[n - 2] * 0.25f + _magsBuf[n - 1] * 0.75f;

            float fMin = binHz;
            float fMax = Math.Min(nyquist, 14000f);

            var bandEdges = GetBandEdges(fMin, fMax, Bands, binHz, n);

            for (int b = 0; b < Bands; b++)
            {
                int binLo = bandEdges[b];
                int binHi = bandEdges[b + 1];
                if (binHi < binLo) binHi = binLo;

                float sumSq = 0;
                int count = 0;
                for (int i = binLo; i <= binHi && i < n; i++)
                {
                    sumSq += _smoothedMagsBuf[i] * _smoothedMagsBuf[i];
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

            Array.Copy(_smoothed, _outputBuf, Bands);
            _lastFftTicks = System.Environment.TickCount64;
            SpectrumUpdated?.Invoke(_outputBuf);
        }
        catch (Exception ex)
        {
            ALog.Warn("CatClaw", $"[CatClaw] OnFftData exception (suppressed): {ex.Message}");
        }
    }

    private int[]? _bandEdges;
    private int _bandEdgesKey;

    private int[] GetBandEdges(float fMin, float fMax, int bands, float binHz, int n)
    {
        int key = (int)(binHz * 100) + n * 10000;
        if (_bandEdges != null && _bandEdgesKey == key) return _bandEdges;

        _bandEdges = new int[bands + 1];
        _bandEdgesKey = key;
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
            _bandEdges[b] = Math.Clamp((int)Math.Round(freq / binHz), 1, n - 1);
        }

        for (int b = 0; b < bands; b++)
        {
            if (_bandEdges[b + 1] <= _bandEdges[b])
                _bandEdges[b + 1] = _bandEdges[b] + 1;
        }

        return _bandEdges;
    }

    private class CaptureListener : Java.Lang.Object, Visualizer.IOnDataCaptureListener
    {
        private readonly VisualizerHelper _owner;
        public CaptureListener(VisualizerHelper owner) => _owner = owner;

        public void OnWaveFormDataCapture(Visualizer? visualizer, byte[]? waveform, int samplingRate) { }

        public void OnFftDataCapture(Visualizer? visualizer, byte[]? fft, int samplingRate)
        {
            // 加 try/catch 防止异常冒泡到 Android 原生层导致监听器被静默停止
            try
            {
                if (fft != null && fft.Length > 0)
                {
                    _owner._samplingRate = samplingRate;
                    _owner.OnFftData(fft);
                }
            }
            catch (Exception ex)
            {
                ALog.Warn("CatClaw", $"[CatClaw] CaptureListener.OnFftDataCapture exception (suppressed): {ex.Message}");
            }
        }
    }
}
