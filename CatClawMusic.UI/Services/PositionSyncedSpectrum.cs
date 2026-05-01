using Android.Media;

namespace CatClawMusic.UI.Services;

public class PositionSyncedSpectrum : IDisposable
{
    private MediaExtractor? _extractor;
    private MediaCodec? _codec;
    private int _sampleRate = 44100, _channels = 2;
    private CancellationTokenSource? _cts;
    private bool _released;

    public void Start(string filePath, Func<TimeSpan> getPosition, Action<float[], float[]> onSpectrum)
    {
        Stop();
        InitCodec(filePath);
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var codec = _codec;
        var extractor = _extractor;
        var sr = _sampleRate;
        var ch = _channels;

        Task.Run(() =>
        {
            var info = new MediaCodec.BufferInfo();
            var lastSeekUs = -1L;

            while (!ct.IsCancellationRequested)
            {
                ct.WaitHandle.WaitOne(50);
                if (ct.IsCancellationRequested) break;
                try
                {
                    var pos = getPosition();
                    long seekUs = (long)(pos.TotalMilliseconds * 1000);

                    if (_released || codec == null || extractor == null) continue;

                    // 仅在位置大幅跳变时 seek + flush
                    if (lastSeekUs < 0 || Math.Abs(seekUs - lastSeekUs) > 2000000)
                    {
                        extractor.SeekTo(seekUs, MediaExtractorSeekTo.ClosestSync);
                        try { codec.Flush(); } catch { }
                        lastSeekUs = seekUs;
                    }

                    // 解码
                    var samples = new List<float>();
                    bool inputDone = false;
                    while (samples.Count < 1024)
                    {
                        if (_released) break;
                        if (!inputDone)
                        {
                            int inIdx;
                            try { inIdx = codec.DequeueInputBuffer(2000); }
                            catch { break; }
                            if (inIdx >= 0)
                            {
                                var inBuf = codec.GetInputBuffer(inIdx)!;
                                int size = extractor.ReadSampleData(inBuf, 0);
                                if (size < 0)
                                {
                                    codec.QueueInputBuffer(inIdx, 0, 0, 0, MediaCodecBufferFlags.EndOfStream);
                                    inputDone = true;
                                }
                                else
                                {
                                    codec.QueueInputBuffer(inIdx, 0, size, extractor.SampleTime, MediaCodecBufferFlags.None);
                                    extractor.Advance();
                                }
                            }
                        }
                        int outIdx;
                        try { outIdx = codec.DequeueOutputBuffer(info, 2000); }
                        catch { break; }
                        if (outIdx >= 0)
                        {
                            var buf = codec.GetOutputBuffer(outIdx)!; buf.Position(info.Offset);
                            var raw = new byte[info.Size]; buf.Get(raw);
                            for (int i = 0; i < raw.Length; i += 2 * ch)
                            {
                                float sum = 0;
                                for (int c = 0; c < ch && i + c * 2 + 1 < raw.Length; c++)
                                    sum += (short)(raw[i + c * 2] | (raw[i + c * 2 + 1] << 8));
                                samples.Add(sum / (ch * 32768f));
                            }
                            codec.ReleaseOutputBuffer(outIdx, false);
                            if ((info.Flags & MediaCodecBufferFlags.EndOfStream) != 0) break;
                        }
                    }

                    if (samples.Count < 512) continue;
                    var pcm = new byte[1024];
                    for (int i = 0; i < 512; i++)
                    {
                        short s = (short)Math.Clamp(samples[i] * 32767f, -32768, 32767);
                        pcm[i * 2] = (byte)(s & 0xFF);
                        pcm[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
                    }
                    var result = FftAnalyzer.Compute(pcm, sr);
                    onSpectrum(result.bars, result.peaks);
                }
                catch (OperationCanceledException) { break; }
                catch { }
            }
        });
    }

    public void Stop()
    {
        _cts?.Cancel();
        _released = true;
        try { _codec?.Stop(); } catch { }
        try { _codec?.Release(); } catch { }
        try { _extractor?.Release(); } catch { }
        _codec = null; _extractor = null;
        _cts = null;
    }

    private void InitCodec(string path)
    {
        _released = false;
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
        if (fmt == null || ti < 0) { ReleaseNow(); return; }
        _extractor.SelectTrack(ti);
        _sampleRate = GetInt(fmt, MediaFormat.KeySampleRate, 44100);
        _channels = GetInt(fmt, MediaFormat.KeyChannelCount, 2);

        _codec = MediaCodec.CreateDecoderByType(fmt.GetString(MediaFormat.KeyMime)!);
        _codec.Configure(fmt, null, null, MediaCodecConfigFlags.None);
        _codec.Start();
    }

    private void ReleaseNow()
    {
        try { _codec?.Stop(); } catch { }
        try { _codec?.Release(); } catch { }
        try { _extractor?.Release(); } catch { }
        _codec = null; _extractor = null;
        _released = true;
    }

    private static int GetInt(MediaFormat f, string k, int d)
    { try { return f.ContainsKey(k) ? f.GetInteger(k) : d; } catch { return d; } }

    public void Dispose() => Stop();
}
