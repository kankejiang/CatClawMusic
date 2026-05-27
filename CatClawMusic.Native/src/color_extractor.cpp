/**
 * @file color_extractor.cpp
 * @brief 高性能封面取色算法实现
 *
 * 从封面图片的 ARGB 像素数据中提取 1-6 种主色调。
 * 算法：色彩量化 + 频率统计 + 饱和度加权评分 + 色相去重。
 *
 * 优化点：
 * - 直接操作像素缓冲区，避免 GetPixel() JNI 开销
 * - 使用固定大小数组替代 Dictionary，零 GC 压力
 * - RGB→HSV 内联计算，避免函数调用开销
 * - 量化 key 范围 0~32767，直接用数组索引替代哈希表
 */

#include "catclaw_native.h"
#include <cmath>
#include <cstring>
#include <cstdlib>
#include <algorithm>

namespace {

/* 量化级别：RGB 各通道从 256 级降为 32 级（5位） */
constexpr int QUANTIZE_LEVELS = 32;
/* 量化 key 最大值：32^3 = 32768 */
constexpr int MAX_QUANT_KEYS = 32768;
/* 明度过滤范围 */
constexpr float V_MIN = 0.05f;
constexpr float V_MAX = 0.97f;

/**
 * @brief RGB 转 HSV（内联，避免函数调用开销）
 */
inline void rgb_to_hsv(int r, int g, int b, float* h, float* s, float* v) {
    float rf = r / 255.0f, gf = g / 255.0f, bf = b / 255.0f;
    float max_c = std::max({rf, gf, bf});
    float min_c = std::min({rf, gf, bf});
    float delta = max_c - min_c;

    *v = max_c;

    if (delta < 1e-6f) {
        *h = 0.0f;
        *s = 0.0f;
        return;
    }

    *s = (max_c > 1e-6f) ? (delta / max_c) : 0.0f;

    if (max_c == rf) {
        *h = 60.0f * fmodf((gf - bf) / delta, 6.0f);
    } else if (max_c == gf) {
        *h = 60.0f * ((bf - rf) / delta + 2.0f);
    } else {
        *h = 60.0f * ((rf - gf) / delta + 4.0f);
    }

    if (*h < 0.0f) *h += 360.0f;
}

/**
 * @brief HSV 转 RGB 并打包为 ARGB 整数（A=0xFF）
 */
inline int32_t hsv_to_argb(float h, float s, float v) {
    float c = v * s;
    float x = c * (1.0f - fabsf(fmodf(h / 60.0f, 2.0f) - 1.0f));
    float m = v - c;
    float rf, gf, bf;

    if (h < 60)       { rf = c; gf = x; bf = 0; }
    else if (h < 120) { rf = x; gf = c; bf = 0; }
    else if (h < 180) { rf = 0; gf = c; bf = x; }
    else if (h < 240) { rf = 0; gf = x; bf = c; }
    else if (h < 300) { rf = x; gf = 0; bf = c; }
    else              { rf = c; gf = 0; bf = x; }

    int r = (int)((rf + m) * 255.0f + 0.5f);
    int g = (int)((gf + m) * 255.0f + 0.5f);
    int b = (int)((bf + m) * 255.0f + 0.5f);

    r = std::max(0, std::min(255, r));
    g = std::max(0, std::min(255, g));
    b = std::max(0, std::min(255, b));

    return (0xFF << 24) | (r << 16) | (g << 8) | b;
}

/**
 * @brief 计算两个色相的环面距离
 */
inline float hue_distance(float h1, float h2) {
    float d = fabsf(h1 - h2);
    return std::min(d, 360.0f - d);
}

} /* anonymous namespace */

/* ============================================================
 * 公共接口实现
 * ============================================================ */

int32_t catclaw_extract_colors(
    const uint32_t* pixels,
    int32_t width,
    int32_t height,
    int32_t max_entries,
    CatClawColorEntry* entries)
{
    if (!pixels || !entries || width <= 0 || height <= 0 || max_entries <= 0)
        return 0;

    /* 使用固定大小数组替代哈希表，零 GC 压力 */
    int32_t* freq = (int32_t*)calloc(MAX_QUANT_KEYS, sizeof(int32_t));
    int64_t* x_sum = (int64_t*)calloc(MAX_QUANT_KEYS, sizeof(int64_t));

    /* 第一步：逐像素遍历，色彩量化 + 频率统计 */
    for (int32_t y = 0; y < height; y++) {
        for (int32_t x = 0; x < width; x++) {
            uint32_t pixel = pixels[y * width + x];

            /* 提取 ARGB 分量 */
            uint8_t a = (pixel >> 24) & 0xFF;
            uint8_t r = (pixel >> 16) & 0xFF;
            uint8_t g = (pixel >>  8) & 0xFF;
            uint8_t b =  pixel        & 0xFF;

            /* 跳过半透明像素 */
            if (a < 128) continue;

            /* RGB→HSV + 亮度过滤 */
            float h, s, v;
            rgb_to_hsv(r, g, b, &h, &s, &v);
            if (v < V_MIN || v > V_MAX) continue;

            /* 色彩量化：15位 key */
            int32_t rk = r / QUANTIZE_LEVELS;
            int32_t gk = g / QUANTIZE_LEVELS;
            int32_t bk = b / QUANTIZE_LEVELS;
            int32_t key = (rk << 10) | (gk << 5) | bk;

            freq[key]++;
            x_sum[key] += x;
        }
    }

    /* 第二步：评分排序（使用简单数组排序） */
    struct ScoredEntry {
        int32_t key;
        float score;
        float avg_x;
    };

    auto* scored = (ScoredEntry*)malloc(MAX_QUANT_KEYS * sizeof(ScoredEntry));
    int32_t scored_count = 0;

    for (int32_t key = 0; key < MAX_QUANT_KEYS; key++) {
        if (freq[key] <= 0) continue;

        /* 从量化 key 反推 RGB 中心值 */
        int32_t rk = (key >> 10) & 0x1F;
        int32_t gk = (key >> 5) & 0x1F;
        int32_t bk = key & 0x1F;
        int32_t r = rk * QUANTIZE_LEVELS + QUANTIZE_LEVELS / 2;
        int32_t g = gk * QUANTIZE_LEVELS + QUANTIZE_LEVELS / 2;
        int32_t b = bk * QUANTIZE_LEVELS + QUANTIZE_LEVELS / 2;

        float h, s, v;
        rgb_to_hsv(r, g, b, &h, &s, &v);

        /* 评分 = 频率 × (0.5 + 饱和度) */
        float score = (float)freq[key] * (0.5f + s);
        float avg_x = (float)x_sum[key] / freq[key];

        scored[scored_count].key = key;
        scored[scored_count].score = score;
        scored[scored_count].avg_x = avg_x;
        scored_count++;
    }

    /* 按评分降序排序 */
    std::sort(scored, scored + scored_count,
        [](const ScoredEntry& a, const ScoredEntry& b) {
            return a.score > b.score;
        });

    /* 第三步：色相去重选取 */
    int32_t result_count = 0;
    float min_hue_dist = 30.0f;
    float selected_hues[6] = {};

    for (int32_t i = 0; i < scored_count && result_count < max_entries; i++) {
        int32_t key = scored[i].key;
        int32_t rk = (key >> 10) & 0x1F;
        int32_t gk = (key >> 5) & 0x1F;
        int32_t bk = key & 0x1F;
        int32_t r = rk * QUANTIZE_LEVELS + QUANTIZE_LEVELS / 2;
        int32_t g = gk * QUANTIZE_LEVELS + QUANTIZE_LEVELS / 2;
        int32_t b = bk * QUANTIZE_LEVELS + QUANTIZE_LEVELS / 2;

        float h, s, v;
        rgb_to_hsv(r, g, b, &h, &s, &v);

        /* 检查与已选颜色的色相差 */
        bool is_duplicate = false;
        for (int32_t j = 0; j < result_count; j++) {
            if (hue_distance(h, selected_hues[j]) < min_hue_dist) {
                is_duplicate = true;
                break;
            }
        }
        if (is_duplicate) continue;

        /* 通过去重检查 */
        selected_hues[result_count] = h;
        entries[result_count].color = hsv_to_argb(h, s, v);
        entries[result_count].center_x = scored[i].avg_x;
        entries[result_count].weight = scored[i].score;
        result_count++;

        /* 前3个颜色选完后，放宽色相差要求 */
        if (result_count >= 3) min_hue_dist = 18.0f;
    }

    /* 第四步：水平位置归一化 */
    if (result_count > 0) {
        float min_x = entries[0].center_x;
        float max_x = entries[0].center_x;
        for (int32_t i = 1; i < result_count; i++) {
            if (entries[i].center_x < min_x) min_x = entries[i].center_x;
            if (entries[i].center_x > max_x) max_x = entries[i].center_x;
        }
        float range = max_x - min_x;
        if (range > 0) {
            for (int32_t i = 0; i < result_count; i++)
                entries[i].center_x = (entries[i].center_x - min_x) / range;
        } else {
            for (int32_t i = 0; i < result_count; i++)
                entries[i].center_x = (result_count > 1) ? (float)i / (result_count - 1) : 0.5f;
        }
    }

    /* 第五步：兜底策略 — 若无结果，稀疏采样取平均色 */
    if (result_count == 0) {
        int64_t r_sum = 0, g_sum = 0, b_sum = 0;
        int32_t count = 0;
        for (int32_t y = 0; y < height; y += 10) {
            for (int32_t x = 0; x < width; x += 10) {
                uint32_t pixel = pixels[y * width + x];
                uint8_t a = (pixel >> 24) & 0xFF;
                if (a < 128) continue;
                uint8_t r = (pixel >> 16) & 0xFF;
                uint8_t g = (pixel >>  8) & 0xFF;
                uint8_t b =  pixel        & 0xFF;
                float h, s, v;
                rgb_to_hsv(r, g, b, &h, &s, &v);
                if (v < 0.1f || v > 0.95f) continue;
                r_sum += r; g_sum += g; b_sum += b;
                count++;
            }
        }
        if (count > 0) {
            int32_t r = (int32_t)(r_sum / count);
            int32_t g = (int32_t)(g_sum / count);
            int32_t b = (int32_t)(b_sum / count);
            entries[0].color = (0xFF << 24) | (r << 16) | (g << 8) | b;
            entries[0].center_x = 0.5f;
            entries[0].weight = 1.0f;
            result_count = 1;
        }
    }

    free(freq);
    free(x_sum);
    free(scored);
    return result_count;
}
