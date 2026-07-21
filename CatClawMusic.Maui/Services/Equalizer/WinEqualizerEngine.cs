#if WINDOWS
using System.Runtime.InteropServices;
using Windows.Media.Audio;
using Windows.Media.MediaProperties;
using Windows.Media.Render;
using Windows.Storage;

namespace CatClawMusic.Maui.Services.Equalizer;

/// <summary>Windows 均衡器播放引擎：AudioGraph + 双二阶(Biquad)峰值滤波器实时处理</summary>
public class WinEqualizerEngine : IDisposable
{
    private AudioGraph? _graph;
    private AudioFileInputNode? _fileInput;
    private AudioFrameOutputNode? _frameTap;
    private AudioFrameInputNode? _processedInput;
    private AudioDeviceOutputNode? _deviceOutput;
    private string? _tempDownloadPath;

    private BiquadFilter[][]? _filters; // [channel][band]
    private double _volume = 1.0;

    /// <summary>引擎是否正在承担播放任务</summary>
    public bool IsActive { get; private set; }

    public bool IsPlaying { get; private set; }

    public event EventHandler? MediaEnded;
    public event EventHandler? MediaFailed;

    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            if (_processedInput != null)
                _processedInput.OutgoingGain = _volume;
        }
    }

    public TimeSpan Position
    {
        get { try { return _fileInput?.Position ?? TimeSpan.Zero; } catch { return TimeSpan.Zero; } }
    }

    public TimeSpan Duration
    {
        get { try { return _fileInput?.Duration ?? TimeSpan.Zero; } catch { return TimeSpan.Zero; } }
    }

    /// <summary>加载音频文件（本地路径或 HTTP URL，URL 先下载到临时文件）</summary>
    public async Task<bool> LoadAsync(Uri source)
    {
        try
        {
            CleanupGraph();
            CleanupTempFile();

            string localPath;
            if (source.IsFile || source.Scheme == "file")
            {
                localPath = source.IsFile ? source.LocalPath : source.AbsolutePath;
            }
            else if (source.Scheme is "http" or "https")
            {
                // 网络源下载到临时文件
                _tempDownloadPath = Path.Combine(Path.GetTempPath(), $"cc_eq_{Guid.NewGuid():N}{GetExt(source)}");
                using var http = new HttpClient();
                await using var stream = await http.GetStreamAsync(source);
                await using var fs = File.Create(_tempDownloadPath);
                await stream.CopyToAsync(fs);
                localPath = _tempDownloadPath;
            }
            else
            {
                return false;
            }

            if (!File.Exists(localPath)) return false;

            var file = await StorageFile.GetFileFromPathAsync(localPath);

            // 创建 AudioGraph（默认 float32 处理格式）
            var settings = new AudioGraphSettings(AudioRenderCategory.Media);
            var graphResult = await AudioGraph.CreateAsync(settings);
            if (graphResult.Status != AudioGraphCreationStatus.Success)
            {
                Log.Debug("WinEqualizerEngine", $"[EQ-Win] AudioGraph 创建失败: {graphResult.Status}");
                return false;
            }
            _graph = graphResult.Graph;

            var deviceResult = await _graph.CreateDeviceOutputNodeAsync();
            if (deviceResult.Status != AudioDeviceNodeCreationStatus.Success)
            {
                Log.Debug("WinEqualizerEngine", $"[EQ-Win] 输出设备创建失败: {deviceResult.Status}");
                CleanupGraph();
                return false;
            }
            _deviceOutput = deviceResult.DeviceOutputNode;

            var fileResult = await _graph.CreateFileInputNodeAsync(file);
            if (fileResult.Status != AudioFileNodeCreationStatus.Success)
            {
                Log.Debug("WinEqualizerEngine", $"[EQ-Win] 文件输入创建失败: {fileResult.Status}");
                CleanupGraph();
                return false;
            }
            _fileInput = fileResult.FileInputNode;
            _fileInput.FileCompleted += OnFileCompleted;

            // 帧处理链路：fileInput → frameTap(读取) → [DSP] → processedInput → deviceOutput
            _frameTap = _graph.CreateFrameOutputNode();
            _fileInput.AddOutgoingConnection(_frameTap);

            _processedInput = _graph.CreateFrameInputNode();
            _processedInput.AddOutgoingConnection(_deviceOutput);
            _processedInput.OutgoingGain = _volume;

            _graph.QuantumStarted += OnQuantumStarted;

            // 初始化/更新滤波器系数
            RebuildFilters();

            IsActive = true;
            return true;
        }
        catch (Exception ex)
        {
            Log.Debug("WinEqualizerEngine", $"[EQ-Win] 加载失败: {ex.Message}");
            CleanupGraph();
            return false;
        }
    }

    public void Play()
    {
        try { _graph?.Start(); IsPlaying = true; } catch { }
    }

    public void Pause()
    {
        try { _graph?.Stop(); IsPlaying = false; } catch { }
    }

    public void Stop()
    {
        try { _graph?.Stop(); } catch { }
        IsPlaying = false;
        CleanupGraph();
        CleanupTempFile();
        IsActive = false;
    }

    public void Seek(TimeSpan position)
    {
        try { _fileInput?.Seek(position); } catch { }
    }

    /// <summary>EQ 设置变化后重建滤波器系数（实时生效，无需重载文件）</summary>
    public void RebuildFilters()
    {
        var gains = EqualizerSettings.GetBandGains();
        var enabled = EqualizerSettings.Enabled;
        var sampleRate = 44100.0;
        try
        {
            // 尝试获取实际采样率
            var props = _fileInput?.EncodingProperties;
            if (props != null && props.SampleRate > 0) sampleRate = props.SampleRate;
        }
        catch { }

        const int channels = 2;
        var bands = EqualizerSettings.BandFrequencies.Length;
        _filters = new BiquadFilter[channels][];
        for (int ch = 0; ch < channels; ch++)
        {
            _filters[ch] = new BiquadFilter[bands];
            for (int b = 0; b < bands; b++)
            {
                var gain = enabled ? gains[b] : 0;
                _filters[ch][b] = BiquadFilter.Peaking(EqualizerSettings.BandFrequencies[b], gain, 1.4, sampleRate);
            }
        }

        // 低音增强 → 额外低频架式滤波器
        if (enabled && EqualizerSettings.BassBoost > 0)
        {
            var boostDb = EqualizerSettings.BassBoost * 12.0 / 100.0;
            for (int ch = 0; ch < channels; ch++)
            {
                var shelf = BiquadFilter.LowShelf(120, boostDb, sampleRate);
                _filters[ch] = _filters[ch].Append(shelf).ToArray();
            }
        }
    }

    // ─── 帧处理 ───

    private void OnQuantumStarted(AudioGraph sender, object args)
    {
        if (_frameTap == null || _processedInput == null) return;
        try
        {
            using var frame = _frameTap.GetFrame();
            if (frame == null) return;
            ProcessFrame(frame);
            _processedInput.AddFrame(frame);
        }
        catch { }
    }

    private unsafe void ProcessFrame(Windows.Media.AudioFrame frame)
    {
        using var buffer = frame.LockBuffer(Windows.Media.AudioBufferAccessMode.ReadWrite);
        using var reference = buffer.CreateReference();
        if (reference is not IMemoryBufferByteAccess byteAccess) return;

        byteAccess.GetBuffer(out byte* dataInBytes, out uint capacityInBytes);
        if (dataInBytes == null || capacityInBytes < 4) return;

        var filters = _filters;
        if (filters == null || filters.Length == 0) return;

        // AudioGraph 默认 float32 交错立体声，直接原地处理（零拷贝）
        var floatCount = (int)(buffer.Length / 4);
        var floats = (float*)dataInBytes;
        var channels = filters.Length;

        for (int i = 0; i + channels <= floatCount; i += channels)
        {
            for (int ch = 0; ch < channels; ch++)
            {
                float sample = floats[i + ch];
                var bandFilters = filters[ch];
                for (int b = 0; b < bandFilters.Length; b++)
                    sample = bandFilters[b].Process(sample);
                floats[i + ch] = sample;
            }
        }
    }

    /// <summary>COM 互操作接口：获取 AudioBuffer 底层字节指针（AudioGraph 帧处理标准模式）</summary>
    [ComImport]
    [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMemoryBufferByteAccess
    {
        unsafe void GetBuffer(out byte* buffer, out uint capacity);
    }

    private void OnFileCompleted(AudioFileInputNode sender, object args)
    {
        IsPlaying = false;
        MainThread.BeginInvokeOnMainThread(() => MediaEnded?.Invoke(this, EventArgs.Empty));
    }

    // ─── 清理 ───

    private void CleanupGraph()
    {
        try
        {
            if (_graph != null)
            {
                _graph.QuantumStarted -= OnQuantumStarted;
                _graph.Stop();
                _graph.Dispose();
            }
        }
        catch { }
        if (_fileInput != null)
        {
            try { _fileInput.FileCompleted -= OnFileCompleted; _fileInput.Dispose(); } catch { }
        }
        try { _frameTap?.Dispose(); } catch { }
        try { _processedInput?.Dispose(); } catch { }
        try { _deviceOutput?.Dispose(); } catch { }
        _graph = null;
        _fileInput = null;
        _frameTap = null;
        _processedInput = null;
        _deviceOutput = null;
        _filters = null;
    }

    private void CleanupTempFile()
    {
        if (_tempDownloadPath != null)
        {
            try { File.Delete(_tempDownloadPath); } catch { }
            _tempDownloadPath = null;
        }
    }

    private static string GetExt(Uri uri)
    {
        try
        {
            var ext = Path.GetExtension(uri.AbsolutePath);
            return string.IsNullOrEmpty(ext) ? ".audio" : ext;
        }
        catch { return ".audio"; }
    }

    public void Dispose()
    {
        Stop();
    }
}

/// <summary>双二阶(Biquad)滤波器 — RBJ Audio EQ Cookbook 公式</summary>
public class BiquadFilter
{
    private double _b0, _b1, _b2, _a1, _a2;
    private double _s1, _s2; // Direct Form II Transposed 状态

    private BiquadFilter() { }

    /// <summary>峰值均衡滤波器</summary>
    /// <param name="freq">中心频率 Hz</param>
    /// <param name="gainDb">增益 dB</param>
    /// <param name="q">Q 值（带宽）</param>
    /// <param name="sampleRate">采样率</param>
    public static BiquadFilter Peaking(double freq, double gainDb, double q, double sampleRate)
    {
        var f = new BiquadFilter();
        if (Math.Abs(gainDb) < 0.01)
        {
            // 直通
            f._b0 = 1; f._b1 = 0; f._b2 = 0; f._a1 = 0; f._a2 = 0;
            return f;
        }

        var a = Math.Pow(10, gainDb / 40.0);
        var w0 = 2 * Math.PI * freq / sampleRate;
        var alpha = Math.Sin(w0) / (2 * q);

        var b0 = 1 + alpha * a;
        var b1 = -2 * Math.Cos(w0);
        var b2 = 1 - alpha * a;
        var a0 = 1 + alpha / a;
        var a1 = -2 * Math.Cos(w0);
        var a2 = 1 - alpha / a;

        f._b0 = b0 / a0; f._b1 = b1 / a0; f._b2 = b2 / a0;
        f._a1 = a1 / a0; f._a2 = a2 / a0;
        return f;
    }

    /// <summary>低频架式滤波器（用于低音增强）</summary>
    public static BiquadFilter LowShelf(double freq, double gainDb, double sampleRate, double slope = 0.9)
    {
        var f = new BiquadFilter();
        if (Math.Abs(gainDb) < 0.01)
        {
            f._b0 = 1; return f;
        }

        var a = Math.Pow(10, gainDb / 40.0);
        var w0 = 2 * Math.PI * freq / sampleRate;
        var alpha = Math.Sin(w0) / 2 * Math.Sqrt((a + 1 / a) * (1 / slope - 1) + 2);
        var cosW0 = Math.Cos(w0);
        var sqrtA2Alpha = 2 * Math.Sqrt(a) * alpha;

        var b0 = a * ((a + 1) - (a - 1) * cosW0 + sqrtA2Alpha);
        var b1 = 2 * a * ((a - 1) - (a + 1) * cosW0);
        var b2 = a * ((a + 1) - (a - 1) * cosW0 - sqrtA2Alpha);
        var a0 = (a + 1) + (a - 1) * cosW0 + sqrtA2Alpha;
        var a1 = -2 * ((a - 1) + (a + 1) * cosW0);
        var a2 = (a + 1) + (a - 1) * cosW0 - sqrtA2Alpha;

        f._b0 = b0 / a0; f._b1 = b1 / a0; f._b2 = b2 / a0;
        f._a1 = a1 / a0; f._a2 = a2 / a0;
        return f;
    }

    /// <summary>处理单个采样（Direct Form II Transposed）</summary>
    public float Process(float x)
    {
        var y = _b0 * x + _s1;
        _s1 = _b1 * x - _a1 * y + _s2;
        _s2 = _b2 * x - _a2 * y;
        return (float)y;
    }

    /// <summary>重置滤波器状态（切歌时调用避免爆音）</summary>
    public void Reset() { _s1 = 0; _s2 = 0; }
}
#endif
