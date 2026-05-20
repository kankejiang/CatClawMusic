using AndroidX.Media3.Common.Audio;

namespace CatClawMusic.UI.Services;

/// <summary>
/// TeeAudioProcessor: 从 ExoPlayer 音频管道无损截取 PCM 数据用于可视化，
/// 不修改音频数据（完全透传），无需 RECORD_AUDIO 权限。
/// </summary>
public class TeeAudioProcessor : BaseAudioProcessor
{
    private readonly float[] _tempBuffer = new float[4096];
    private int _tempCount;

    public float[] LatestSpectrum { get; private set; } = Array.Empty<float>();
    public event Action<float[]>? SpectrumUpdated;

    public override void QueueInput(Java.Nio.ByteBuffer? inputBuffer)
    {
        if (inputBuffer == null) return;
        var remaining = inputBuffer.Remaining();
        var outputBuffer = ReplaceOutputBuffer(remaining);
        if (outputBuffer != null)
        {
            var pos = inputBuffer.Position();
            ProcessAudioData(inputBuffer, remaining);
            inputBuffer.Position(pos);
            outputBuffer.Put(inputBuffer);
            outputBuffer.Flip();
        }
    }

    private void ProcessAudioData(Java.Nio.ByteBuffer buffer, int size)
    {
        var channelCount = 2;
        var sampleCount = size / 2;
        var monoCount = sampleCount / channelCount;

        if (monoCount == 0) return;

        var shortBuffer = buffer.AsShortBuffer();
        shortBuffer.Position(0);

        var samples = monoCount > _tempBuffer.Length ? _tempBuffer.Length : monoCount;
        for (int i = 0; i < samples; i++)
            _tempBuffer[i] = Math.Abs((float)shortBuffer.Get(i * channelCount) / 32768f);

        _tempCount = samples;
        ComputeSpectrumBands();
    }

    private void ComputeSpectrumBands()
    {
        const int bands = 32;
        var spectrum = new float[bands];
        var samplesPerBand = Math.Max(1, _tempCount / bands);

        for (int b = 0; b < bands; b++)
        {
            float sum = 0;
            for (int s = b * samplesPerBand; s < (b + 1) * samplesPerBand && s < _tempCount; s++)
                sum += _tempBuffer[s];
            spectrum[b] = Math.Min(1f, sum / samplesPerBand * 2.5f);
        }

        LatestSpectrum = spectrum;
        SpectrumUpdated?.Invoke(spectrum);
    }

    public override bool IsActive => true;

    [System.Obsolete]
    protected override void OnFlush()
    {
        _tempCount = 0;
    }
}
