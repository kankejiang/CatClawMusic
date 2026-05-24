/**
 * @file stack_blur.cpp
 * @brief Stack Blur 图像模糊算法实现
 *
 * Stack Blur 是 Mario Klingemann 发明的高斯模糊近似算法，
 * 复杂度 O(width × height × radius)，比逐像素卷积快得多。
 * 效果接近高斯模糊，但计算量大幅降低。
 *
 * 替代 Android RenderScript 的 ScriptIntrinsicBlur：
 * - 无 API 版本限制（RenderScript 在 Android 12+ 已废弃）
 * - 无需创建 RenderScript 上下文
 * - 就地修改像素，零额外内存分配
 *
 * 算法原理：
 *   1. 水平方向：维护一个滑动窗口的 R/G/B 累加和
 *   2. 垂直方向：对水平模糊结果再做一次
 *   3. 每个像素的新值 = 窗口内所有像素的加权平均
 */

#include "catclaw_native.h"
#include <cstring>
#include <algorithm>

namespace {

/**
 * @brief 从 ARGB 像素中提取各通道分量
 */
inline void argb_to_channels(uint32_t pixel, uint8_t& a, uint8_t& r, uint8_t& g, uint8_t& b) {
    a = (pixel >> 24) & 0xFF;
    r = (pixel >> 16) & 0xFF;
    g = (pixel >>  8) & 0xFF;
    b =  pixel        & 0xFF;
}

/**
 * @brief 将各通道分量打包回 ARGB 像素
 */
inline uint32_t channels_to_argb(uint8_t a, uint8_t r, uint8_t g, uint8_t b) {
    return ((uint32_t)a << 24) | ((uint32_t)r << 16) | ((uint32_t)g << 8) | (uint32_t)b;
}

/**
 * @brief 对像素数组执行单方向 Stack Blur
 *
 * @param pixels    像素数组
 * @param w         图片宽度
 * @param h         图片高度
 * @param radius    模糊半径
 * @param horizontal true=水平方向，false=垂直方向
 */
void stack_blur_pass(uint32_t* pixels, int32_t w, int32_t h, int32_t radius, bool horizontal) {
    int32_t div = radius * 2 + 1;
    /* 查找表：除法替换为乘法 + 移位，避免整数除法开销 */
    /* 使用 float 除法以保持精度 */
    float inv_div = 1.0f / div;

    int32_t outer_max = horizontal ? h : w;
    int32_t inner_max = horizontal ? w : h;

    for (int32_t outer = 0; outer < outer_max; outer++) {
        /* 初始化窗口累加和 */
        int32_t sum_r = 0, sum_g = 0, sum_b = 0, sum_a = 0;

        /* 预填充窗口：中心像素左侧 radius 个像素 */
        auto get_pixel = [&](int32_t inner) -> uint32_t {
            if (horizontal)
                return pixels[outer * w + inner];
            else
                return pixels[inner * w + outer];
        };

        auto set_pixel = [&](int32_t inner, uint32_t color) {
            if (horizontal)
                pixels[outer * w + inner] = color;
            else
                pixels[inner * w + outer] = color;
        };

        /* 初始化：用左边缘像素填充窗口 */
        uint32_t edge = get_pixel(0);
        uint8_t ea, er, eg, eb;
        argb_to_channels(edge, ea, er, eg, eb);
        sum_a = ea * (radius + 1);
        sum_r = er * (radius + 1);
        sum_g = eg * (radius + 1);
        sum_b = eb * (radius + 1);

        for (int32_t i = 1; i <= radius; i++) {
            uint32_t p = get_pixel(std::min(i, inner_max - 1));
            uint8_t pa, pr, pg, pb;
            argb_to_channels(p, pa, pr, pg, pb);
            sum_a += pa; sum_r += pr; sum_g += pg; sum_b += pb;
        }

        /* 滑动窗口遍历 */
        for (int32_t inner = 0; inner < inner_max; inner++) {
            /* 输出当前像素 */
            uint8_t oa = (uint8_t)(sum_a * inv_div + 0.5f);
            uint8_t or_ = (uint8_t)(sum_r * inv_div + 0.5f);
            uint8_t og = (uint8_t)(sum_g * inv_div + 0.5f);
            uint8_t ob = (uint8_t)(sum_b * inv_div + 0.5f);
            set_pixel(inner, channels_to_argb(oa, or_, og, ob));

            /* 计算窗口右端和左端位置 */
            int32_t right = std::min(inner + radius + 1, inner_max - 1);
            int32_t left = std::max(inner - radius, 0);

            /* 加入右端像素，移除左端像素 */
            uint32_t p_right = get_pixel(right);
            uint8_t ra, rr, rg, rb;
            argb_to_channels(p_right, ra, rr, rg, rb);
            sum_a += ra; sum_r += rr; sum_g += rg; sum_b += rb;

            uint32_t p_left = get_pixel(left);
            uint8_t la, lr, lg, lb;
            argb_to_channels(p_left, la, lr, lg, lb);
            sum_a -= la; sum_r -= lr; sum_g -= lg; sum_b -= lb;
        }
    }
}

} /* anonymous namespace */

void catclaw_stack_blur_argb(
    uint32_t* pixels,
    int32_t width,
    int32_t height,
    int32_t radius)
{
    if (!pixels || width <= 0 || height <= 0 || radius <= 0)
        return;

    /* 限制半径范围 */
    if (radius > 25) radius = 25;

    /* Stack Blur 需要两次遍历：水平 + 垂直 */
    stack_blur_pass(pixels, width, height, radius, true);
    stack_blur_pass(pixels, width, height, radius, false);
}
