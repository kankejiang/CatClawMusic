using Android.Media.Audiofx;
using ALog = Android.Util.Log;

namespace CatClawMusic.UI.Helpers;

public class VisualizerHelper : Java.Lang.Object
{
    private Visualizer? _visualizer;
    private bool _enabled;

    public event Action<float[]>? SpectrumUpdated;

    public bool IsEnabled => _enabled;

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
            _visualizer.SetCaptureSize(Visualizer.GetCaptureSizeRange()[1]);
            _visualizer.SetDataCaptureListener(
                new CaptureListener(this),
                Visualizer.MaxCaptureRate / 2,
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
    private readonly float[] _smoothed = new float[32];

    private void OnFftData(byte[] fft)
    {
        const int bands = 32;
        var spectrum = new float[bands];
        var n = fft.Length / 2;
        if (n < 2) return;

        int sr = _samplingRate > 0 ? _samplingRate / 1000 : 44100;
        float binHz = sr / (float)fft.Length;

        float[] magnitudes = new float[n - 1];
        for (int k = 1; k < n; k++)
        {
            float real = (sbyte)fft[2 * k];
            float imag = (sbyte)fft[2 * k + 1];
            magnitudes[k - 1] = (float)Math.Sqrt(real * real + imag * imag);
        }

        float[] bandFreqs = BuildBandEdges(bands, binHz, n);

        for (int b = 0; b < bands; b++)
        {
            int binLo = Math.Max(1, (int)Math.Floor(bandFreqs[b] / binHz));
            int binHi = Math.Min(n - 2, (int)Math.Ceiling(bandFreqs[b + 1] / binHz));
            if (binHi < binLo) binHi = binLo;

            float peak = 0;
            for (int i = binLo; i <= binHi && i < magnitudes.Length; i++)
            {
                if (magnitudes[i - 1] > peak)
                    peak = magnitudes[i - 1];
            }

            float normalized = Math.Clamp(peak / 120f, 0f, 1f);

            if (normalized > _smoothed[b])
                _smoothed[b] = normalized;
            else
                _smoothed[b] = _smoothed[b] * 0.6f + normalized * 0.4f;

            spectrum[b] = _smoothed[b];
        }

        if (++_fftCount % 300 == 1)
        {
            ALog.Debug("CatClaw", $"[CatClaw] FFT diag: sr={sr}, binHz={binHz:F1}, sum={spectrum.Sum():F2}, max={spectrum.Max():F2}");
        }

        SpectrumUpdated?.Invoke(spectrum);
    }

    private static float[] BuildBandEdges(int bands, float binHz, int n)
    {
        float nyquist = binHz * n;
        int linearBands = Math.Min(bands, (int)(200f / binHz));
        int logBands = bands - linearBands;

        var edges = new float[bands + 1];

        for (int b = 0; b <= linearBands; b++)
            edges[b] = binHz + b * (200f - binHz) / linearBands;

        float logMin = (float)Math.Log10(200f);
        float logMax = (float)Math.Log10(Math.Min(nyquist, 20000f));

        for (int b = 1; b <= logBands; b++)
        {
            float logF = logMin + (logMax - logMin) * b / logBands;
            edges[linearBands + b] = (float)Math.Pow(10, logF);
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
