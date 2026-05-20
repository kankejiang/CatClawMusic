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

    private void OnFftData(byte[] fft)
    {
        const int bands = 32;
        var spectrum = new float[bands];
        var n = fft.Length / 2;
        if (n < 2) return;

        float[] magnitudes = new float[n - 1];
        for (int i = 1; i < n; i++)
        {
            float real = (sbyte)fft[2 * i];
            float imag = (sbyte)fft[2 * i + 1];
            magnitudes[i - 1] = (float)Math.Sqrt(real * real + imag * imag);
        }

        for (int b = 0; b < bands; b++)
        {
            float lowFreq = b * n / bands + 1;
            float highFreq = (b + 1) * n / bands + 1;
            int lo = Math.Max(0, (int)lowFreq - 1);
            int hi = Math.Min(magnitudes.Length, (int)highFreq);
            if (lo >= hi) continue;

            float sum = 0;
            for (int i = lo; i < hi; i++)
                sum += magnitudes[i];

            float avg = sum / (hi - lo);
            spectrum[b] = Math.Min(1f, avg / 80f);
        }

        SpectrumUpdated?.Invoke(spectrum);
    }

    private class CaptureListener : Java.Lang.Object, Visualizer.IOnDataCaptureListener
    {
        private readonly VisualizerHelper _owner;
        public CaptureListener(VisualizerHelper owner) => _owner = owner;

        public void OnWaveFormDataCapture(Visualizer? visualizer, byte[]? waveform, int samplingRate) { }

        public void OnFftDataCapture(Visualizer? visualizer, byte[]? fft, int samplingRate)
        {
            if (fft != null && fft.Length > 0)
                _owner.OnFftData(fft);
        }
    }
}
