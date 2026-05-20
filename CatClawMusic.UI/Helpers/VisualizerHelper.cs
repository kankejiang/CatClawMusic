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

    private void OnFftData(byte[] fft)
    {
        const int bands = 32;
        var spectrum = new float[bands];
        var n = fft.Length / 2;
        if (n < 2) return;

        int sr = _samplingRate > 0 ? _samplingRate / 1000 : 44100;
        float binHz = sr / (float)fft.Length;

        float[] magnitudes = new float[n - 1];
        for (int i = 1; i < n; i++)
            magnitudes[i - 1] = (fft[i] & 0xFF) / 255f;

        const float minFreq = 20f;
        const float maxFreq = 20000f;
        float logMin = (float)Math.Log10(minFreq);
        float logMax = (float)Math.Log10(maxFreq);

        for (int b = 0; b < bands; b++)
        {
            float logLo = logMin + (logMax - logMin) * b / bands;
            float logHi = logMin + (logMax - logMin) * (b + 1) / bands;
            float freqLo = (float)Math.Pow(10, logLo);
            float freqHi = (float)Math.Pow(10, logHi);

            int binLo = Math.Max(1, (int)(freqLo / binHz));
            int binHi = Math.Min(n - 1, (int)(freqHi / binHz));
            if (binLo >= binHi) continue;

            float peak = 0;
            for (int i = binLo; i <= binHi; i++)
            {
                if (magnitudes[i - 1] > peak)
                    peak = magnitudes[i - 1];
            }

            float centerFreq = (float)Math.Sqrt(freqLo * freqHi);
            float aWeight = ComputeAWeight(centerFreq);

            float rawDb = peak > 0.0001f ? 20f * (float)Math.Log10(peak) : -80f;
            float weightedDb = rawDb + aWeight;
            spectrum[b] = Math.Clamp((weightedDb + 60f) / 80f, 0f, 1f);
        }

        if (++_fftCount % 300 == 1)
        {
            ALog.Debug("CatClaw", $"[CatClaw] FFT diag: sr={sr}, binHz={binHz:F1}, n={n}, fftLen={fft.Length}, sum={spectrum.Sum():F2}, max={spectrum.Max():F2}");
        }

        SpectrumUpdated?.Invoke(spectrum);
    }

    private static float ComputeAWeight(float freq)
    {
        float f2 = freq * freq;
        float f4 = f2 * f2;
        float num = 12194f * 12194f * f4;
        float den = (f2 + 20.6f * 20.6f) * (float)Math.Sqrt((f2 + 107.7f * 107.7f) * (f2 + 737.9f * 737.9f)) * (f2 + 12194f * 12194f);
        float ratio = num / den;
        float db = 20f * (float)Math.Log10(ratio) + 2f;
        return db;
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
