/**
 * @file spectrum_processor.cpp
 * @brief 频谱数据处理实现
 *
 * 将 FFT 实部/虚部数据转换为可视化用的频谱条数据。
 * 包含幅度计算、对数频带映射、attack/decay 时间平滑。
 */

#include "catclaw_native.h"
#include <cmath>
#include <cstring>

namespace {

/**
 * @brief 计算单个频带的 RMS 能量
 */
inline float band_rms(const float* magnitudes, int32_t start, int32_t end) {
    if (start >= end) return 0.0f;
    double sum = 0.0;
    int32_t count = end - start;
    for (int32_t i = start; i < end; i++) {
        sum += (double)magnitudes[i] * magnitudes[i];
    }
    return sqrtf((float)(sum / count));
}

} /* anonymous namespace */

void catclaw_process_spectrum(
    const float* real,
    const float* imag,
    int32_t fft_size,
    const int32_t* band_edges,
    int32_t band_count,
    const float* prev_bands,
    float* out_bands,
    float attack,
    float decay)
{
    if (!real || !imag || !band_edges || !out_bands || fft_size <= 0 || band_count <= 0)
        return;

    /* 第一步：计算 FFT 幅度谱 */
    int32_t half = fft_size / 2;
    float* magnitudes = new float[half];
    for (int32_t i = 0; i < half; i++) {
        magnitudes[i] = sqrtf(real[i] * real[i] + imag[i] * imag[i]) / fft_size;
    }

    /* 第二步：计算每个频带的 RMS 能量 */
    for (int32_t b = 0; b < band_count; b++) {
        int32_t start = band_edges[b];
        int32_t end = band_edges[b + 1];
        if (start >= half) start = half - 1;
        if (end > half) end = half;
        out_bands[b] = band_rms(magnitudes, start, end);
    }

    /* 第三步：对数缩放（dB 映射到 0~1） */
    for (int32_t b = 0; b < band_count; b++) {
        float db = 20.0f * log10f(out_bands[b] + 1e-7f);
        out_bands[b] = fmaxf(0.0f, fminf(1.0f, (db + 80.0f) / 80.0f));
    }

    /* 第四步：时间平滑（attack/decay） */
    if (prev_bands) {
        for (int32_t b = 0; b < band_count; b++) {
            float target = out_bands[b];
            float prev = prev_bands[b];
            float coeff = (target > prev) ? attack : decay;
            out_bands[b] = prev + (target - prev) * coeff;
        }
    }

    delete[] magnitudes;
}

void catclaw_build_band_edges(
    int32_t sample_rate,
    int32_t fft_size,
    float min_freq,
    float max_freq,
    int32_t band_count,
    int32_t* band_edges)
{
    if (!band_edges || band_count <= 0 || fft_size <= 0 || sample_rate <= 0)
        return;

    float freq_resolution = (float)sample_rate / fft_size;
    float log_min = logf(min_freq);
    float log_max = logf(max_freq);

    for (int32_t i = 0; i <= band_count; i++) {
        float log_freq = log_min + (log_max - log_min) * i / band_count;
        float freq = expf(log_freq);
        band_edges[i] = (int32_t)(freq / freq_resolution + 0.5f);
        if (band_edges[i] < 0) band_edges[i] = 0;
        if (band_edges[i] > fft_size / 2) band_edges[i] = fft_size / 2;
    }

    /* 确保边界单调递增 */
    for (int32_t i = 1; i <= band_count; i++) {
        if (band_edges[i] <= band_edges[i - 1])
            band_edges[i] = band_edges[i - 1] + 1;
    }
}
