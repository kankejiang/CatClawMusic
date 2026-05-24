/**
 * @file audio_processor.cpp
 * @brief 实时音频 PCM 处理实现
 *
 * 将 ExoPlayer 音频管道截取的 16-bit PCM 数据转换为可视化用的频谱数据。
 * 包含：立体声→单声道提取 + 绝对值归一化 + 频带分段求和。
 *
 * 优化点：
 * - 直接操作 short* 缓冲区，避免 C# ByteBuffer.Get() 的 JNI 逐样本调用开销
 * - NEON SIMD 向量化：一次处理 4 个 float 的绝对值计算
 * - 频带计算使用累加而非逐样本除法
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

int32_t catclaw_pcm_to_mono_abs(
    const int16_t* pcm_data,
    int32_t data_len,
    int32_t channel_count,
    float* out_float)
{
    if (!pcm_data || !out_float || data_len <= 0 || channel_count <= 0)
        return 0;

    int32_t mono_count = data_len / channel_count;
    float inv_max = 1.0f / 32768.0f;

#if CATCLAW_NEON
    /* NEON SIMD 加速：一次处理 4 个样本 */
    if (channel_count == 2)
    {
        int32_t i = 0;
        float32x4_t v_inv = vdupq_n_f32(inv_max);
        for (; i + 3 < mono_count; i += 4)
        {
            /* 从交错立体声中提取左声道：pcm[0], pcm[2], pcm[4], pcm[6] */
            int16_t left[4] = {
                pcm_data[(i + 0) * 2],
                pcm_data[(i + 1) * 2],
                pcm_data[(i + 2) * 2],
                pcm_data[(i + 3) * 2]
            };
            /* short → float → 绝对值 → 归一化 */
            int16x4_t v_s16 = vld1_s16(left);
            float32x4_t v_f32 = vcvtq_f32_s32(vmovl_s16(v_s16));
            float32x4_t v_abs = vabsq_f32(v_f32);
            float32x4_t v_norm = vmulq_f32(v_abs, v_inv);
            vst1q_f32(out_float + i, v_norm);
        }
        /* 处理剩余样本 */
        for (; i < mono_count; i++)
        {
            out_float[i] = fabsf((float)pcm_data[i * 2]) * inv_max;
        }
    }
    else
#endif
    {
        /* 通用路径：支持任意通道数 */
        for (int32_t i = 0; i < mono_count; i++)
        {
            out_float[i] = fabsf((float)pcm_data[i * channel_count]) * inv_max;
        }
    }

    return mono_count;
}

void catclaw_compute_spectrum_bands(
    const float* samples,
    int32_t sample_count,
    int32_t band_count,
    float* out_bands,
    float gain)
{
    if (!samples || !out_bands || sample_count <= 0 || band_count <= 0)
        return;

    int32_t samples_per_band = sample_count / band_count;
    if (samples_per_band < 1) samples_per_band = 1;

    float inv_spb = 1.0f / samples_per_band;

#if CATCLAW_NEON
    /* NEON 加速频带求和 */
    int32_t b = 0;
    for (; b + 3 < band_count; b += 4)
    {
        float sums[4] = {0, 0, 0, 0};
        for (int32_t k = 0; k < 4; k++)
        {
            int32_t start = (b + k) * samples_per_band;
            int32_t end = std::min(start + samples_per_band, sample_count);
            float32x4_t v_sum = vdupq_n_f32(0.0f);
            int32_t s = start;
            for (; s + 3 < end; s += 4)
            {
                float32x4_t v_data = vld1q_f32(samples + s);
                v_sum = vaddq_f32(v_sum, v_data);
            }
            float partial[4];
            vst1q_f32(partial, v_sum);
            sums[k] = partial[0] + partial[1] + partial[2] + partial[3];
            for (; s < end; s++)
                sums[k] += samples[s];
        }
        float32x4_t v_sums = vld1q_f32(sums);
        float32x4_t v_norm = vmulq_f32(v_sums, vdupq_n_f32(inv_spb * gain));
        float32x4_t v_clamped = vminq_f32(v_norm, vdupq_n_f32(1.0f));
        vst1q_f32(out_bands + b, v_clamped);
    }
    for (; b < band_count; b++)
    {
        int32_t start = b * samples_per_band;
        int32_t end = std::min(start + samples_per_band, sample_count);
        float sum = 0;
        for (int32_t s = start; s < end; s++)
            sum += samples[s];
        out_bands[b] = std::min(1.0f, sum * inv_spb * gain);
    }
#else
    for (int32_t b = 0; b < band_count; b++)
    {
        int32_t start = b * samples_per_band;
        int32_t end = std::min(start + samples_per_band, sample_count);
        float sum = 0;
        for (int32_t s = start; s < end; s++)
            sum += samples[s];
        out_bands[b] = std::min(1.0f, sum * inv_spb * gain);
    }
#endif
}
