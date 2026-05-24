#pragma once

#include <stdint.h>
#include <stdbool.h>

#ifdef __cplusplus
extern "C" {
#endif

/* ============================================================
 * FFT 频谱分析接口
 * ============================================================ */

/**
 * @brief 对 PCM 音频数据执行 FFT 并计算频谱条形图
 *
 * @param pcm_data   输入 PCM 采样数据（float，-1.0~1.0）
 * @param data_len   PCM 数据长度（必须是 2 的幂）
 * @param bar_count  输出频谱条数
 * @param bars       输出频谱条数组（调用方分配，长度 bar_count）
 * @param min_freq   最低频率（Hz），通常 20
 * @param max_freq   最高频率（Hz），通常 20000
 * @param sample_rate 采样率（Hz），通常 44100
 */
void catclaw_fft_compute_bars(
    const float* pcm_data,
    int32_t data_len,
    int32_t bar_count,
    float* bars,
    float min_freq,
    float max_freq,
    int32_t sample_rate
);

/**
 * @brief 计算 PCM 数据的 RMS 响度
 *
 * @param pcm_data 输入 PCM 采样数据
 * @param data_len 数据长度
 * @return RMS 值（0.0~1.0）
 */
float catclaw_compute_rms(const float* pcm_data, int32_t data_len);

/* ============================================================
 * LRC 歌词解析接口
 * ============================================================ */

/** 单行歌词 */
typedef struct {
    int32_t time_ms;       /* 时间戳（毫秒） */
    int32_t word_count;    /* 逐字歌词词数，0 表示普通行歌词 */
    int32_t* word_times;   /* 逐字歌词各词时间（毫秒），word_count 个 */
    const char* text;      /* 歌词文本（UTF-8，指向内部缓冲区） */
} CatClawLyricLine;

/** 解析结果 */
typedef struct {
    CatClawLyricLine* lines;  /* 歌词行数组 */
    int32_t line_count;       /* 歌词行数 */
    int32_t capacity;         /* 内部分配容量 */
    char* text_buffer;        /* 内部文本缓冲区 */
    int32_t text_buffer_len;  /* 文本缓冲区长度 */
    int32_t* word_time_buffer;/* 逐字时间缓冲区 */
    int32_t word_time_count;  /* 逐字时间总数 */
} CatClawLyricResult;

/**
 * @brief 检测字节流的文本编码
 *
 * @param data     原始字节数据
 * @param data_len 数据长度
 * @return 编码类型：0=未知，1=UTF-8(BOM)，2=UTF-8，3=GBK，4=GB2312，5=Shift-JIS
 */
int32_t catclaw_detect_encoding(const uint8_t* data, int32_t data_len);

/**
 * @brief 将字节数据从指定编码转换为 UTF-8
 *
 * @param src_data   源字节数据
 * @param src_len    源数据长度
 * @param encoding   编码类型（catclaw_detect_encoding 返回值）
 * @param out_utf8   输出 UTF-8 缓冲区（调用方分配）
 * @param out_len    输出缓冲区大小（字节），返回实际写入长度
 * @return 0 成功，-1 失败
 */
int32_t catclaw_convert_to_utf8(
    const uint8_t* src_data,
    int32_t src_len,
    int32_t encoding,
    char* out_utf8,
    int32_t* out_len
);

/**
 * @brief 解析 LRC 歌词文本（UTF-8）
 *
 * @param lrc_text  LRC 歌词文本（UTF-8 编码）
 * @param text_len  文本长度
 * @return 解析结果（需要调用 catclaw_lyric_free 释放）
 */
CatClawLyricResult* catclaw_parse_lrc(const char* lrc_text, int32_t text_len);

/**
 * @brief 释放歌词解析结果
 *
 * @param result 解析结果指针
 */
void catclaw_lyric_free(CatClawLyricResult* result);

/**
 * @brief 二分查找当前歌词行索引
 *
 * @param result  解析结果
 * @param time_ms 当前播放位置（毫秒）
 * @return 当前歌词行索引，-1 表示无匹配
 */
int32_t catclaw_lyric_find_index(const CatClawLyricResult* result, int32_t time_ms);

/* ============================================================
 * 音频标签读取接口
 * ============================================================ */

/** 音频标签信息 */
typedef struct {
    char title[512];
    char artist[512];
    char album[512];
    char album_artist[512];
    char genre[128];
    char comment[1024];
    int32_t year;
    int32_t track;
    int32_t disc;
    int32_t duration_ms;
    int32_t bitrate_kbps;
    int32_t sample_rate;
    int32_t channels;
    bool has_cover;
    int32_t cover_offset;
    int32_t cover_size;
    char cover_mime[64];
} CatClawTagInfo;

/**
 * @brief 从音频文件读取标签信息
 *
 * @param file_path 文件路径（UTF-8）
 * @param info      输出标签信息
 * @return 0 成功，-1 失败
 */
int32_t catclaw_read_tags(const char* file_path, CatClawTagInfo* info);

/**
 * @brief 从音频文件提取封面图片数据
 *
 * @param file_path  文件路径（UTF-8）
 * @param out_data   输出图片数据（调用方分配）
 * @param out_size   输入缓冲区大小，返回实际数据大小
 * @return 0 成功，-1 失败，1 缓冲区不足（out_size 返回所需大小）
 */
int32_t catclaw_read_cover(
    const char* file_path,
    uint8_t* out_data,
    int32_t* out_size
);

/**
 * @brief 批量扫描目录下的音频文件标签
 *
 * @param dir_path   目录路径（UTF-8）
 * @param recursive  是否递归扫描
 * @param callback   每个文件的回调函数（tag_info, user_data）
 * @param user_data  传递给回调的用户数据
 * @return 扫描到的文件数，-1 失败
 */
int32_t catclaw_scan_directory(
    const char* dir_path,
    bool recursive,
    void (*callback)(const CatClawTagInfo* tag_info, void* user_data),
    void* user_data
);

/* ============================================================
 * 封面取色接口
 * ============================================================ */

/** 提取的主色调 */
typedef struct {
    int32_t color;    /* ARGB 颜色值 */
    float center_x;   /* 水平中心位置（0~1 归一化） */
} CatClawColorEntry;

/**
 * @brief 从 ARGB 像素数据提取主色调
 *
 * 算法：色彩量化 + 频率统计 + 饱和度加权评分 + 色相去重
 * 1. 逐像素遍历：RGB→HSV + 亮度过滤 + 色彩量化（15位key）
 * 2. 评分排序：score = 频率 × (0.5 + 饱和度)
 * 3. 色相去重：前3个色差≥30°，之后≥18°，最多6个
 * 4. 水平位置归一化
 *
 * @param pixels    ARGB 像素数据（与 Android Bitmap 格式一致）
 * @param width     图片宽度
 * @param height    图片高度
 * @param max_entries 最多提取的颜色数（通常 6）
 * @param entries   输出颜色数组（调用方分配）
 * @return 实际提取的颜色数
 */
int32_t catclaw_extract_colors(
    const uint32_t* pixels,
    int32_t width,
    int32_t height,
    int32_t max_entries,
    CatClawColorEntry* entries
);

/* ============================================================
 * 频谱数据处理接口
 * ============================================================ */

/**
 * @brief 处理 FFT 频谱数据：幅度计算 + 对数频带映射 + 时间平滑
 *
 * 将原始 FFT 实部/虚部数据转换为可视化用的频谱条数据。
 * 包含 attack/decay 时间平滑，使动画更流畅。
 *
 * @param real       FFT 实部数据
 * @param imag       FFT 虚部数据
 * @param fft_size   FFT 大小
 * @param band_edges 频带边界索引数组（由 catclaw_build_band_edges 生成）
 * @param band_count 频带数量
 * @param prev_bands 上一帧的频谱数据（用于时间平滑，首次传 NULL）
 * @param out_bands  输出频谱数据（0.0~1.0，长度 band_count）
 * @param attack     Attack 系数（0~1，越大跟踪越快，通常 0.88）
 * @param decay      Decay 系数（0~1，越大衰减越快，通常 0.28）
 */
void catclaw_process_spectrum(
    const float* real,
    const float* imag,
    int32_t fft_size,
    const int32_t* band_edges,
    int32_t band_count,
    const float* prev_bands,
    float* out_bands,
    float attack,
    float decay
);

/**
 * @brief 构建对数频带边界索引
 *
 * 根据采样率和 FFT 大小，将频率范围按对数刻度划分为若干频带。
 * 低频段分配更多频带（人耳对低频更敏感）。
 *
 * @param sample_rate 采样率
 * @param fft_size    FFT 大小
 * @param min_freq    最低频率（Hz）
 * @param max_freq    最高频率（Hz）
 * @param band_count  频带数量
 * @param band_edges  输出频带边界索引数组（长度 band_count + 1）
 */
void catclaw_build_band_edges(
    int32_t sample_rate,
    int32_t fft_size,
    float min_freq,
    float max_freq,
    int32_t band_count,
    int32_t* band_edges
);

/* ============================================================
 * 实时音频 PCM 处理接口
 * ============================================================ */

/**
 * @brief 将 16-bit PCM 数据转换为单声道绝对值浮点数组
 *
 * 从交错立体声 short 数据中提取左声道，取绝对值并归一化到 0~1。
 * 用于音频可视化，替代 C# 逐样本循环。
 *
 * @param pcm_data   16-bit PCM 采样数据（交错立体声）
 * @param data_len   PCM 数据长度（short 个数）
 * @param channel_count 通道数（通常 2）
 * @param out_float 输出浮点数组（调用方分配，长度 ≥ data_len/channel_count）
 * @return 输出样本数
 */
int32_t catclaw_pcm_to_mono_abs(
    const int16_t* pcm_data,
    int32_t data_len,
    int32_t channel_count,
    float* out_float
);

/**
 * @brief 从单声道浮点 PCM 数据计算频谱条带
 *
 * 将采样数据均匀分为 band_count 个频带，每个频带取平均值并归一化。
 * 用于实时音频可视化，替代 C# 双层循环。
 *
 * @param samples       单声道浮点采样数据（0~1 绝对值）
 * @param sample_count  采样数
 * @param band_count    频带数量（通常 32）
 * @param out_bands     输出频谱数据（0~1，调用方分配）
 * @param gain          增益系数（通常 2.5）
 */
void catclaw_compute_spectrum_bands(
    const float* samples,
    int32_t sample_count,
    int32_t band_count,
    float* out_bands,
    float gain
);

/* ============================================================
 * 图像模糊接口
 * ============================================================ */

/**
 * @brief 对 ARGB 像素数据执行 Stack Blur 模糊
 *
 * Stack Blur 是 O(r) 复杂度的高斯模糊近似算法，
 * 比 RenderScript 的 ScriptIntrinsicBlur 更快且无 API 版本限制。
 * 就地修改像素数据。
 *
 * @param pixels    ARGB 像素数据（就地修改）
 * @param width     图片宽度
 * @param height    图片高度
 * @param radius    模糊半径（1~25）
 */
void catclaw_stack_blur_argb(
    uint32_t* pixels,
    int32_t width,
    int32_t height,
    int32_t radius
);

#ifdef __cplusplus
}
#endif
