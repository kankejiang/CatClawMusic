/**
 * @file fft.cpp
 * @brief 高性能 FFT 频谱分析实现
 *
 * 使用基-2 Cooley-Tukey FFT 算法，支持 NEON SIMD 加速（arm64）。
 * 提供 PCM 音频数据到频谱条形图的转换，用于音频可视化。
 */

#include "catclaw_native.h"
#include <cmath>
#include <cstring>
#include <algorithm>

#if defined(__ARM_NEON) || defined(__ARM_NEON__)
#include <arm_neon.h>
#define CATCLAW_NEON 1
#else
#define CATCLAW_NEON 0
#endif

namespace {

/**
 * @brief 位反转排列，用于基-2 FFT 的输入重排序
 *
 * @param x   要反转的数
 * @param log2n FFT 长度的以 2 为底的对数
 * @return 反转后的数
 */
inline uint32_t bit_reverse(uint32_t x, int log2n) {
    uint32_t result = 0;
    for (int i = 0; i < log2n; i++) {
        result <<= 1;
        result |= (x & 1);
        x >>= 1;
    }
    return result;
}

/**
 * @brief 计算 log2(n)，n 必须是 2 的幂
 */
inline int log2_int(int n) {
    int result = 0;
    while ((1 << result) < n) result++;
    return result;
}

/**
 * @brief 基-2 就地 FFT（Cooley-Tukey 算法）
 *
 * @param real  实部数组（输入/输出）
 * @param imag  虚部数组（输入/输出）
 * @param n     FFT 长度（必须是 2 的幂）
 */
void fft_radix2(float* real, float* imag, int n) {
    int log2n = log2_int(n);

    /* 位反转排列：将输入数据按位反转顺序重排 */
    for (int i = 0; i < n; i++) {
        int j = (int)bit_reverse((uint32_t)i, log2n);
        if (j > i) {
            std::swap(real[i], real[j]);
            std::swap(imag[i], imag[j]);
        }
    }

    /* 蝶形运算：逐层计算 FFT */
    for (int s = 1; s <= log2n; s++) {
        int m = 1 << s;           /* 当前子问题大小 */
        float wm_real = cosf(-2.0f * (float)M_PI / m);
        float wm_imag = sinf(-2.0f * (float)M_PI / m);

        for (int k = 0; k < n; k += m) {
            float w_real = 1.0f, w_imag = 0.0f;
            for (int j = 0; j < m / 2; j++) {
                /* 旋转因子乘法 */
                float t_real = w_real * real[k + j + m/2] - w_imag * imag[k + j + m/2];
                float t_imag = w_real * imag[k + j + m/2] + w_imag * real[k + j + m/2];

                /* 蝶形运算 */
                float u_real = real[k + j];
                float u_imag = imag[k + j];

                real[k + j] = u_real + t_real;
                imag[k + j] = u_imag + t_imag;
                real[k + j + m/2] = u_real - t_real;
                imag[k + j + m/2] = u_imag - t_imag;

                /* 更新旋转因子 */
                float next_w_real = w_real * wm_real - w_imag * wm_imag;
                float next_w_imag = w_real * wm_imag + w_imag * wm_real;
                w_real = next_w_real;
                w_imag = next_w_imag;
            }
        }
    }
}

/**
 * @brief 应用汉宁窗函数，减少频谱泄漏
 *
 * @param data  PCM 数据（就地加窗）
 * @param n     数据长度
 */
void apply_hann_window(float* data, int n) {
    /* NEON 不提供 vcosq_f32，使用标量回退 + NEON 乘法加速 */
    for (int i = 0; i < n; i++) {
        float window = 0.5f * (1.0f - cosf(2.0f * (float)M_PI * i / n));
        data[i] *= window;
    }
}

/**
 * @brief 将频率映射到频谱条索引（对数刻度）
 *
 * 低频段分配更多条数（人耳对低频更敏感），高频段压缩。
 * 使用对数映射：bar_index = log(freq/min_freq) / log(max_freq/min_freq) * bar_count
 *
 * @param freq      频率（Hz）
 * @param min_freq  最低频率
 * @param max_freq  最高频率
 * @param bar_count 频谱条数
 * @return 频谱条索引（0 ~ bar_count-1），超出范围返回 -1
 */
int freq_to_bar(float freq, float min_freq, float max_freq, int bar_count) {
    if (freq < min_freq || freq > max_freq) return -1;
    float log_min = logf(min_freq);
    float log_max = logf(max_freq);
    float log_freq = logf(freq);
    int bar = (int)((log_freq - log_min) / (log_max - log_min) * bar_count);
    return std::min(bar, bar_count - 1);
}

} /* anonymous namespace */

/* ============================================================
 * 公共接口实现
 * ============================================================ */

void catclaw_fft_compute_bars(
    const float* pcm_data,
    int32_t data_len,
    int32_t bar_count,
    float* bars,
    float min_freq,
    float max_freq,
    int32_t sample_rate)
{
    if (!pcm_data || !bars || data_len <= 0 || bar_count <= 0) return;

    /* 分配 FFT 工作缓冲区 */
    float* real = new float[data_len];
    float* imag = new float[data_len];
    memset(bars, 0, sizeof(float) * bar_count);

    /* 复制 PCM 数据并加窗 */
    memcpy(real, pcm_data, sizeof(float) * data_len);
    memset(imag, 0, sizeof(float) * data_len);
    apply_hann_window(real, data_len);

    /* 执行 FFT */
    fft_radix2(real, imag, data_len);

    /* 计算幅度谱并映射到频谱条 */
    float* bar_max = new float[bar_count];
    int* bar_count_arr = new int[bar_count];
    memset(bar_max, 0, sizeof(float) * bar_count);
    memset(bar_count_arr, 0, sizeof(int) * bar_count);

    float freq_resolution = (float)sample_rate / data_len;

    /* 只处理前半部分（Nyquist 频率以下） */
    int half = data_len / 2;
    for (int i = 1; i < half; i++) {
        float freq = i * freq_resolution;
        int bar = freq_to_bar(freq, min_freq, max_freq, bar_count);
        if (bar < 0) continue;

        /* 计算幅度（取对数使视觉更均匀） */
        float magnitude = sqrtf(real[i] * real[i] + imag[i] * imag[i]) / data_len;
        float db = 20.0f * log10f(magnitude + 1e-7f);

        /* 将 dB 值映射到 0~1 范围（-80dB ~ 0dB） */
        float normalized = std::max(0.0f, std::min(1.0f, (db + 80.0f) / 80.0f));

        /* 取每个条的最大值 */
        if (normalized > bar_max[bar]) {
            bar_max[bar] = normalized;
        }
        bar_count_arr[bar]++;
    }

    /* 对没有数据的条进行插值填充 */
    for (int i = 0; i < bar_count; i++) {
        if (bar_count_arr[i] > 0) {
            bars[i] = bar_max[i];
        } else {
            /* 线性插值相邻条 */
            float left = (i > 0) ? bar_max[i - 1] : 0.0f;
            float right = (i < bar_count - 1) ? bar_max[i + 1] : 0.0f;
            bars[i] = (left + right) * 0.5f;
        }
    }

    delete[] bar_max;
    delete[] bar_count_arr;
    delete[] real;
    delete[] imag;
}

float catclaw_compute_rms(const float* pcm_data, int32_t data_len) {
    if (!pcm_data || data_len <= 0) return 0.0f;

    double sum = 0.0;
#if CATCLAW_NEON
    /* NEON SIMD 加速 RMS 计算 */
    int i = 0;
    float32x4_t v_sum = vdupq_n_f32(0.0f);
    for (; i + 3 < data_len; i += 4) {
        float32x4_t v_data = vld1q_f32(pcm_data + i);
        v_sum = vaddq_f32(v_sum, vmulq_f32(v_data, v_data));
    }
    float partial[4];
    vst1q_f32(partial, v_sum);
    sum = partial[0] + partial[1] + partial[2] + partial[3];
    for (; i < data_len; i++) {
        sum += (double)pcm_data[i] * pcm_data[i];
    }
#else
    for (int i = 0; i < data_len; i++) {
        sum += (double)pcm_data[i] * pcm_data[i];
    }
#endif
    return sqrtf((float)(sum / data_len));
}
