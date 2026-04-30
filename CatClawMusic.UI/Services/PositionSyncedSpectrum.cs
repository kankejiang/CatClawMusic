using Android.Media;

namespace CatClawMusic.UI.Services;

/// <summary>流式按需解码：每个采样位置即时解码 512 帧，无需预加载</summary>
public class PositionSyncedSpectrum : IDisposable
{
    private readonly object _lock = new();
    private MediaExtractor? _extractor;
    private MediaCodec? _codec;
    private int _sampleRate = 44100, _channels = 2;
    private CancellationTokenSource? _cts;
    private string? _filePath;

    public void Start(string filePath, Func<TimeSpan> getPosition, Action<float[], float[]> onSpectrum)
    {
        Stop();
        _filePath = filePath;
        InitCodec(filePath);
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        Task.Run(() =>
        {
            while (!ct.IsCancellationRequested)
            {
                ct.WaitHandle.WaitOne(60);
                if (ct.IsCancellationRequested) break;
                try
                {
                    var (bars, peaks) = SampleAt(getPosition());
                    onSpectrum(bars, peaks);
                }
                catch { }
            }
        });
    }

    public void Stop()
    {
        _cts?.Cancel();
        lock (_lock) { ReleaseCodec(); }
        _cts = null;
    }

    private (float[], float[]) SampleAt(TimeSpan pos)
    {
        lock (_lock)
        {
            if (_codec == null || _extractor == null) return (new float[32], new float[32]);
            long seekUs = (long)(pos.TotalMilliseconds * 1000);
            // 每次 seek 后刷新解码器
            _extractor.SeekTo(seekUs, MediaExtractorSeekTo.ClosestSync);
            _codec.Flush();

            var info = new MediaCodec.BufferInfo();
            bool inputDone = false;
            var samples = new List<float>();
            int neededSamples = 1024; // 512 点 FFT × 2

            while (samples.Count < neededSamples)
            {
                if (!inputDone)
                {
                    int inIdx = _codec.DequeueInputBuffer(5000);
                    if (inIdx >= 0)
                    {
                        var inBuf = _codec.GetInputBuffer(inIdx)!;
                        int size = _extractor.ReadSampleData(inBuf, 0);
                        if (size < 0)
                        { _codec.QueueInputBuffer(inIdx, 0, 0, 0, MediaCodecBufferFlags.EndOfStream); inputDone = true; }
                        else { _codec.QueueInputBuffer(inIdx, 0, size, _extractor.SampleTime, MediaCodecBufferFlags.None); _extractor.Advance(); }
                    }
                }
                int outIdx = _codec.DequeueOutputBuffer(info, 5000);
                if (outIdx >= 0)
                {
                    var buf = _codec.GetOutputBuffer(outIdx)!; buf.Position(info.Offset);
                    var raw = new byte[info.Size]; buf.Get(raw);
                    for (int i = 0; i < raw.Length; i += 2 * _channels)
                    {
                        float sum = 0;
                        for (int c = 0; c < _channels && i + c * 2 + 1 < raw.Length; c++)
                            sum += (short)(raw[i + c * 2] | (raw[i + c * 2 + 1] << 8));
                        samples.Add(sum / (_channels * 32768f));
                    }
                    _codec.ReleaseOutputBuffer(outIdx, false);
                    if ((info.Flags & MediaCodecBufferFlags.EndOfStream) != 0) break;
                }
            }

            if (samples.Count < 512) return (new float[32], new float[32]);
            var pcm = new byte[samples.Count * 2];
            for (int i = 0; i < samples.Count; i++)
            {
                short s = (short)Math.Clamp(samples[i] * 32767f, -32768, 32767);
                pcm[i * 2] = (byte)(s & 0xFF);
                pcm[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
            }
            return FftAnalyzer.Compute(pcm);
        }
    }

    private void InitCodec(string path)
    {
        _extractor = new MediaExtractor();
        if (path.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
            _extractor.SetDataSource(global::Android.App.Application.Context, global::Android.Net.Uri.Parse(path)!, null);
        else _extractor.SetDataSource(path);

        MediaFormat? fmt = null; int ti = -1;
        for (int i = 0; i < _extractor.TrackCount; i++)
        {
            var f = _extractor.GetTrackFormat(i);
            if (f.ContainsKey(MediaFormat.KeyMime) && f.GetString(MediaFormat.KeyMime)!.StartsWith("audio/"))
            { fmt = f; ti = i; break; }
        }
        if (fmt == null || ti < 0) { ReleaseCodec(); return; }
        _extractor.SelectTrack(ti);
        _sampleRate = GetInt(fmt, MediaFormat.KeySampleRate, 44100);
        _channels = GetInt(fmt, MediaFormat.KeyChannelCount, 2);

        _codec = MediaCodec.CreateDecoderByType(fmt.GetString(MediaFormat.KeyMime)!);
        _codec.Configure(fmt, null, null, MediaCodecConfigFlags.None);
        _codec.Start();
    }

    private void ReleaseCodec()
    {
        try { _codec?.Stop(); } catch { }
        try { _codec?.Release(); } catch { }
        try { _extractor?.Release(); } catch { }
        _codec = null; _extractor = null;
    }

    private static int GetInt(MediaFormat f, string k, int d)
    { try { return f.ContainsKey(k) ? f.GetInteger(k) : d; } catch { return d; } }

    public void Dispose() => Stop();
}
