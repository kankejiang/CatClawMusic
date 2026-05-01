namespace CatClawMusic.UI.Services;

/// <summary>
/// 专业音频频谱分析器：遵循 AES 标准
/// 对数频段 + A加权 + 快上慢下包络 + 峰值保持 + 帧间平滑
/// </summary>
public static class FftAnalyzer
{
    private const int FftSize = 512;
    [ThreadStatic] private static double[]? _real, _imag, _hann;
    [ThreadStatic] private static float[]? _bars, _peaks, _bgBars;
    [ThreadStatic] private static int[]? _peakTimer;
    [ThreadStatic] private static float _refLevel = 0.05f;

    // A加权系数（1/3倍频程近似，44100Hz / 512点）
    private static readonly float[] AWeight = new float[FftSize / 2];
    static FftAnalyzer()
    {
        for (int i = 0; i < FftSize / 2; i++)
        {
            double f = (double)i * 44100 / FftSize;
            double f2 = f * f;
            double r = 12200 * 12200 * f2 * f2 /
                ((f2 + 20.6 * 20.6) * Math.Sqrt((f2 + 107.7 * 107.7) * (f2 + 737.9 * 737.9)) * (f2 + 12200 * 12200));
            AWeight[i] = (float)Math.Max(0.001, Math.Min(1.0, r * 2.0 + 0.001));
        }
    }

    private static void EnsureBuffers(int barCount)
    {
        if (_real?.Length != FftSize) { _real = new double[FftSize]; _imag = new double[FftSize]; }
        if (_hann?.Length != FftSize)
        {
            _hann = new double[FftSize];
            for (int i = 0; i < FftSize; i++)
                _hann[i] = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (FftSize - 1)));
        }
        if (_bars?.Length != barCount) _bars = new float[barCount];
        if (_peaks?.Length != barCount) { _peaks = new float[barCount]; _peakTimer = new int[barCount]; _bgBars = new float[barCount]; }
    }

    /// <summary>主入口：返回 (主柱, 峰值/背景柱)</summary>
    public static (float[] bars, float[] peaks) Compute(byte[] pcmBytes, int sampleRate = 44100, int barCount = 32)
    {
        EnsureBuffers(barCount);
        var real = _real!; var imag = _imag!;
        Array.Clear(imag, 0, FftSize);

        // 1. 输入：从 PCM 窗口取 512 样本 + 汉宁窗
        int totalSamples = pcmBytes.Length / 2;
        if (totalSamples < FftSize) return (new float[barCount], new float[barCount]);
        int mid = totalSamples / 2;
        int start = mid - FftSize / 2;
        if (start < 0) start = 0;
        for (int i = 0; i < FftSize; i++)
        {
            int idx = start + i;
            if (idx >= totalSamples) idx = totalSamples - 1;
            short s = (short)(pcmBytes[idx * 2] | (pcmBytes[idx * 2 + 1] << 8));
            real[i] = s / 32768.0 * _hann![i];
        }

        // 2. FFT
        Fft(real, imag);

        // 3. 幅度计算 + A加权（dB scale 转换）
        var mag = new float[FftSize / 2];
        float maxMag = 0;
        for (int i = 0; i < FftSize / 2; i++)
        {
            double m = Math.Sqrt(real[i] * real[i] + imag[i] * imag[i]);
            mag[i] = (float)(m * AWeight[i]) * 2.0f; // A加权
            if (mag[i] > maxMag) maxMag = mag[i];
        }

        // 4. AGC 慢跟踪参考电平 → 安静段不会异常放大
        float frameEnergy = maxMag;
        _refLevel = _refLevel * 0.98f + frameEnergy * 0.02f;
        float norm = 1f / Math.Max(_refLevel * 1.5f, 0.03f);
        for (int i = 0; i < FftSize / 2; i++)
            mag[i] = Math.Clamp(mag[i] * norm, 0, 1);

        // 5. 对数频段划分：32 段从 30Hz 到 16kHz
        // 确保每个柱条映射到独立的 FFT bin 范围，跳过 DC 分量（bin 0）
        float[] rawBars = new float[barCount];
        int lastBinHigh = 0; // 追踪上一个柱条用到的最大 bin 索引
        for (int b = 0; b < barCount; b++)
        {
            double fLow = 30 * Math.Pow(16000.0 / 30.0, (double)b / barCount);
            double fHigh = 30 * Math.Pow(16000.0 / 30.0, (double)(b + 1) / barCount);
            int binLow = Math.Max(1, (int)(fLow * FftSize / sampleRate));
            int binHigh = Math.Min(FftSize / 2 - 1, (int)(fHigh * FftSize / sampleRate));

            // 确保不共用前一个柱条的 bin，避免多个柱条映射到 DC 分量
            if (binLow <= lastBinHigh) binLow = lastBinHigh + 1;
            if (binLow >= FftSize / 2) { binLow = FftSize / 2 - 1; binHigh = FftSize / 2 - 1; }
            if (binHigh < binLow) binHigh = binLow;
            lastBinHigh = binHigh;

            float energy = 0;
            for (int j = binLow; j <= binHigh; j++)
                energy += mag[j];
            rawBars[b] = Math.Min(1f, energy / (binHigh - binLow + 1) * 4f);
        }

        // 6. 包络跟随：主柱快上快下
        const float attack = 0.7f;   // 快速上升
        const float release = 0.5f;  // 快速下降

        for (int b = 0; b < barCount; b++)
        {
            float target = rawBars[b];
            if (target > _bars![b])
                _bars[b] += (target - _bars[b]) * attack;
            else
                _bars[b] += (target - _bars[b]) * release;
        }

        // 7. 峰值保持（背景柱）
        for (int b = 0; b < barCount; b++)
        {
            float barH = _bars![b];
            if (barH > _peaks![b] || _peakTimer![b] <= 0)
            { _peaks[b] = barH; _peakTimer[b] = 12; }
            else
            { _peakTimer[b]--; _peaks[b] -= 0.003f; if (_peaks[b] < barH) _peaks[b] = barH; }

            // 背景柱更慢的单独衰减（氛围层）
            if (barH > _bgBars![b]) _bgBars[b] = barH;
            else _bgBars[b] = _bgBars[b] * 0.97f + barH * 0.03f;
        }

        return (_bars!, _bgBars!);
    }

    // Cooley-Tukey FFT
    private static void Fft(double[] real, double[] imag)
    {
        int n = FftSize;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (real[i], real[j]) = (real[j], real[i]); (imag[i], imag[j]) = (imag[j], imag[i]); }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double a = -2 * Math.PI / len;
            for (int i = 0; i < n; i += len)
            {
                double ur = 1, ui = 0;
                double wr = Math.Cos(a), wi = Math.Sin(a);
                for (int j = 0; j < len / 2; j++)
                {
                    int p = i + j, q = p + len / 2;
                    double tr = ur * real[q] - ui * imag[q], ti = ur * imag[q] + ui * real[q];
                    real[q] = real[p] - tr; imag[q] = imag[p] - ti;
                    real[p] += tr; imag[p] += ti;
                    double tmp = ur * wr - ui * wi; ui = ur * wi + ui * wr; ur = tmp;
                }
            }
        }
    }
}
