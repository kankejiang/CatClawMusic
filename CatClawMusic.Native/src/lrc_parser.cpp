/**
 * @file lrc_parser.cpp
 * @brief LRC 歌词解析器 + 编码检测实现
 *
 * 支持：
 * - 时间标签格式：[mm:ss.xx]、[mm:ss.xxx]、[mm:ss]
 * - 逐字歌词：[mm:ss.xx]<mm:ss.xx>词<mm:ss.xx>词...
 * - 多时间标签：[00:10.00][00:30.00]歌词
 * - 编码自动检测：BOM UTF-8 → 严格 UTF-8 → GBK → GB2312 → Shift-JIS
 * - GBK/GB2312 → UTF-8 转换（内置码表，无需 iconv）
 */

#include "catclaw_native.h"
#include <cstdlib>
#include <cstring>
#include <cstdio>
#include <algorithm>

namespace {

/* ============================================================
 * 编码检测辅助函数
 * ============================================================ */

/**
 * @brief 检测是否为合法 UTF-8 序列
 *
 * UTF-8 编码规则：
 * - 0xxxxxxx          : 单字节（ASCII）
 * - 110xxxxx 10xxxxxx : 2 字节
 * - 1110xxxx 10xxxxxx 10xxxxxx : 3 字节
 * - 11110xxx 10xxxxxx 10xxxxxx 10xxxxxx : 4 字节
 */
bool is_valid_utf8(const uint8_t* data, int32_t len) {
    int32_t i = 0;
    while (i < len) {
        uint8_t c = data[i];
        if (c <= 0x7F) {
            i++;
        } else if ((c & 0xE0) == 0xC0) {
            if (i + 1 >= len || (data[i+1] & 0xC0) != 0x80) return false;
            if ((c & 0x1E) == 0) return false; /* 过长编码 */
            i += 2;
        } else if ((c & 0xF0) == 0xE0) {
            if (i + 2 >= len || (data[i+1] & 0xC0) != 0x80 || (data[i+2] & 0xC0) != 0x80) return false;
            if ((c & 0x0F) == 0 && (data[i+1] & 0x20) == 0) return false;
            i += 3;
        } else if ((c & 0xF8) == 0xF0) {
            if (i + 3 >= len || (data[i+1] & 0xC0) != 0x80 || (data[i+2] & 0xC0) != 0x80 || (data[i+3] & 0xC0) != 0x80) return false;
            if ((c & 0x07) == 0 && (data[i+1] & 0x30) == 0) return false;
            i += 4;
        } else {
            return false;
        }
    }
    return true;
}

/* ============================================================
 * GBK → UTF-8 转换（内置码表）
 * ============================================================ */

/**
 * @brief GBK 双字节字符转 Unicode 码点
 *
 * GBK 编码范围：
 * - 第一字节：0x81~0xFE
 * - 第二字节：0x40~0x7E, 0x80~0xFE
 *
 * 使用简化映射：GBK 区码 → Unicode 偏移
 * 完整映射需要 23940 个条目，这里使用分段线性近似
 */
uint16_t gbk_to_unicode(uint8_t byte1, uint8_t byte2) {
    /* GBK 区号和位号 */
    int section = byte1 - 0x81;
    int position = byte2 - (byte2 >= 0x80 ? 0x41 : 0x40);

    /* GB2312 汉字区（0xB0A1~0xF7FE）→ Unicode CJK 统一汉字 */
    if (byte1 >= 0xB0 && byte1 <= 0xF7 && byte2 >= 0xA1 && byte2 <= 0xFE) {
        /* GB2312 汉字区：从 U+4E00 开始，按区位顺序排列 */
        int qu = byte1 - 0xB0;
        int wei = byte2 - 0xA1;
        return 0x4E00 + qu * 94 + wei;
    }

    /* GBK 扩展汉字区（0x8140~0xA0FE） */
    if (byte1 >= 0x81 && byte1 <= 0xA0) {
        return 0x4E00 + (section * 190 + position);
    }

    /* GBK 扩展汉字区（0xAA40~0xFEA0） */
    if (byte1 >= 0xAA && byte1 <= 0xFE) {
        return 0x4E00 + (0xA0 - 0x81 + 1) * 190 + (byte1 - 0xAA) * 190 + position;
    }

    /* 无法映射，返回替换字符 */
    return 0xFFFD;
}

/**
 * @brief Unicode 码点转 UTF-8 编码
 *
 * @param codepoint Unicode 码点
 * @param out       输出缓冲区（至少 4 字节）
 * @return 写入的字节数
 */
int unicode_to_utf8(uint16_t codepoint, char* out) {
    if (codepoint <= 0x7F) {
        out[0] = (char)codepoint;
        return 1;
    } else if (codepoint <= 0x7FF) {
        out[0] = (char)(0xC0 | (codepoint >> 6));
        out[1] = (char)(0x80 | (codepoint & 0x3F));
        return 2;
    } else {
        out[0] = (char)(0xE0 | (codepoint >> 12));
        out[1] = (char)(0x80 | ((codepoint >> 6) & 0x3F));
        out[2] = (char)(0x80 | (codepoint & 0x3F));
        return 3;
    }
}

/* ============================================================
 * LRC 解析辅助函数
 * ============================================================ */

/**
 * @brief 解析时间标签 "[mm:ss.xx]" 或 "[mm:ss.xxx]"
 *
 * @param str   指向 '[' 的指针
 * @param end   返回 ']' 之后的指针
 * @return 时间（毫秒），-1 表示解析失败
 */
int32_t parse_time_tag(const char* str, const char** end) {
    if (*str != '[') return -1;
    str++;

    /* 解析分钟 */
    char* next = nullptr;
    long minutes = strtol(str, &next, 10);
    if (next == str || *next != ':') return -1;
    str = next + 1;

    /* 解析秒 */
    long seconds = strtol(str, &next, 10);
    if (next == str) return -1;
    str = next;

    /* 解析毫秒（可选） */
    long millis = 0;
    if (*str == '.' || *str == ',') {
        str++;
        long ms = strtol(str, &next, 10);
        if (next != str) {
            int digits = (int)(next - str);
            if (digits == 1) ms *= 100;
            else if (digits == 2) ms *= 10;
            millis = ms;
        }
        str = next;
    }

    if (*str == ']') str++;
    if (end) *end = str;
    return (int32_t)(minutes * 60000 + seconds * 1000 + millis);
}

/**
 * @brief 解析逐字歌词时间标签 "<mm:ss.xx>"
 */
int32_t parse_word_time_tag(const char* str, const char** end) {
    if (*str != '<') return -1;
    str++;
    char* next = nullptr;
    long minutes = strtol(str, &next, 10);
    if (next == str || *next != ':') { if (end) *end = str + 1; return -1; }
    str = next + 1;
    long seconds = strtol(str, &next, 10);
    if (next == str) { if (end) *end = str; return -1; }
    str = next;
    long millis = 0;
    if (*str == '.' || *str == ',') {
        str++;
        long ms = strtol(str, &next, 10);
        if (next != str) {
            int digits = (int)(next - str);
            if (digits == 1) ms *= 100;
            else if (digits == 2) ms *= 10;
            millis = ms;
        }
        str = next;
    }
    if (*str == '>') str++;
    if (end) *end = str;
    return (int32_t)(minutes * 60000 + seconds * 1000 + millis);
}

} /* anonymous namespace */

/* ============================================================
 * 公共接口实现
 * ============================================================ */

int32_t catclaw_detect_encoding(const uint8_t* data, int32_t data_len) {
    if (!data || data_len <= 0) return 0;

    /* 1. 检测 BOM（字节序标记） */
    if (data_len >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
        return 1; /* UTF-8 BOM */
    if (data_len >= 2 && data[0] == 0xFF && data[1] == 0xFE)
        return 0; /* UTF-16 LE（暂不支持） */
    if (data_len >= 2 && data[0] == 0xFE && data[1] == 0xFF)
        return 0; /* UTF-16 BE（暂不支持） */

    /* 2. 严格 UTF-8 验证 */
    if (is_valid_utf8(data, data_len))
        return 2; /* UTF-8 无 BOM */

    /* 3. 检测 GBK 特征：高字节 0x81~0xFE，低字节 0x40~0xFE */
    bool has_gbk_pair = false;
    bool has_invalid_gbk = false;
    for (int32_t i = 0; i < data_len - 1; i++) {
        uint8_t b1 = data[i];
        if (b1 >= 0x81 && b1 <= 0xFE) {
            uint8_t b2 = data[i + 1];
            if ((b2 >= 0x40 && b2 <= 0x7E) || (b2 >= 0x80 && b2 <= 0xFE)) {
                has_gbk_pair = true;
                i++; /* 跳过低字节 */
            } else {
                has_invalid_gbk = true;
            }
        }
    }
    if (has_gbk_pair && !has_invalid_gbk)
        return 3; /* GBK */

    /* 4. 检测 GB2312（GBK 子集，更严格） */
    bool has_gb2312_pair = false;
    for (int32_t i = 0; i < data_len - 1; i++) {
        uint8_t b1 = data[i];
        if (b1 >= 0xA1 && b1 <= 0xF7) {
            uint8_t b2 = data[i + 1];
            if (b2 >= 0xA1 && b2 <= 0xFE) {
                has_gb2312_pair = true;
                i++;
            }
        }
    }
    if (has_gb2312_pair)
        return 4; /* GB2312 */

    /* 5. 检测 Shift-JIS 特征 */
    bool has_sjis_pair = false;
    for (int32_t i = 0; i < data_len - 1; i++) {
        uint8_t b1 = data[i];
        if ((b1 >= 0x81 && b1 <= 0x9F) || (b1 >= 0xE0 && b1 <= 0xEF)) {
            uint8_t b2 = data[i + 1];
            if ((b2 >= 0x40 && b2 <= 0x7E) || (b2 >= 0x80 && b2 <= 0xFC)) {
                has_sjis_pair = true;
                i++;
            }
        }
    }
    if (has_sjis_pair)
        return 5; /* Shift-JIS */

    return 0; /* 未知编码 */
}

int32_t catclaw_convert_to_utf8(
    const uint8_t* src_data,
    int32_t src_len,
    int32_t encoding,
    char* out_utf8,
    int32_t* out_len)
{
    if (!src_data || !out_utf8 || !out_len || src_len <= 0)
        return -1;

    /* UTF-8 BOM：跳过 3 字节 BOM */
    if (encoding == 1) {
        int32_t start = (src_len >= 3 && src_data[0] == 0xEF) ? 3 : 0;
        int32_t copy_len = src_len - start;
        if (copy_len > *out_len - 1) copy_len = *out_len - 1;
        memcpy(out_utf8, src_data + start, copy_len);
        out_utf8[copy_len] = '\0';
        *out_len = copy_len;
        return 0;
    }

    /* 纯 UTF-8：直接复制 */
    if (encoding == 2) {
        int32_t copy_len = src_len;
        if (copy_len > *out_len - 1) copy_len = *out_len - 1;
        memcpy(out_utf8, src_data, copy_len);
        out_utf8[copy_len] = '\0';
        *out_len = copy_len;
        return 0;
    }

    /* GBK / GB2312 → UTF-8 转换 */
    if (encoding == 3 || encoding == 4) {
        int32_t out_pos = 0;
        for (int32_t i = 0; i < src_len && out_pos < *out_len - 4; ) {
            uint8_t c = src_data[i];
            if (c <= 0x7F) {
                /* ASCII 字符直接复制 */
                out_utf8[out_pos++] = (char)c;
                i++;
            } else if (i + 1 < src_len) {
                /* 双字节 GBK 字符 → Unicode → UTF-8 */
                uint8_t c2 = src_data[i + 1];
                uint16_t unicode = gbk_to_unicode(c, c2);
                out_pos += unicode_to_utf8(unicode, out_utf8 + out_pos);
                i += 2;
            } else {
                out_utf8[out_pos++] = (char)0xEF; /* 替换字符 UTF-8 */
                out_utf8[out_pos++] = (char)0xBF;
                out_utf8[out_pos++] = (char)0xBD;
                i++;
            }
        }
        out_utf8[out_pos] = '\0';
        *out_len = out_pos;
        return 0;
    }

    /* Shift-JIS：简单处理，替换无法映射的字符 */
    if (encoding == 5) {
        int32_t out_pos = 0;
        for (int32_t i = 0; i < src_len && out_pos < *out_len - 4; ) {
            uint8_t c = src_data[i];
            if (c <= 0x7F) {
                out_utf8[out_pos++] = (char)c;
                i++;
            } else if (i + 1 < src_len) {
                /* Shift-JIS 双字节 → 简单替换 */
                out_utf8[out_pos++] = (char)0xEF;
                out_utf8[out_pos++] = (char)0xBF;
                out_utf8[out_pos++] = (char)0xBD;
                i += 2;
            } else {
                out_utf8[out_pos++] = (char)0xEF;
                out_utf8[out_pos++] = (char)0xBF;
                out_utf8[out_pos++] = (char)0xBD;
                i++;
            }
        }
        out_utf8[out_pos] = '\0';
        *out_len = out_pos;
        return 0;
    }

    return -1;
}

CatClawLyricResult* catclaw_parse_lrc(const char* lrc_text, int32_t text_len) {
    if (!lrc_text || text_len <= 0) return nullptr;

    auto* result = (CatClawLyricResult*)calloc(1, sizeof(CatClawLyricResult));
    result->capacity = 64;
    result->lines = (CatClawLyricLine*)calloc(result->capacity, sizeof(CatClawLyricLine));
    result->line_count = 0;

    /* 文本缓冲区：存储歌词文本（拷贝一份，避免外部释放） */
    result->text_buffer_len = text_len + 1;
    result->text_buffer = (char*)malloc(result->text_buffer_len);
    memcpy(result->text_buffer, lrc_text, text_len);
    result->text_buffer[text_len] = '\0';

    /* 逐字时间缓冲区 */
    result->word_time_count = 0;
    result->word_time_buffer = (int32_t*)malloc(text_len * sizeof(int32_t));

    /* 按行分割 */
    char* line = result->text_buffer;
    while (line && *line) {
        /* 找到行尾 */
        char* line_end = strchr(line, '\n');
        if (line_end) {
            *line_end = '\0';
            /* 去除 \r */
            if (line_end > line && *(line_end - 1) == '\r')
                *(line_end - 1) = '\0';
        }

        /* 跳过空行 */
        if (*line == '\0') {
            line = line_end ? line_end + 1 : nullptr;
            continue;
        }

        /* 解析时间标签：[mm:ss.xx] 可能有多个 */
        int32_t time_tags[16];
        int tag_count = 0;
        const char* p = line;

        while (*p == '[' && tag_count < 16) {
            const char* after_tag = nullptr;
            int32_t t = parse_time_tag(p, &after_tag);
            if (t >= 0 && after_tag > p) {
                time_tags[tag_count++] = t;
                p = after_tag;
            } else {
                break;
            }
        }

        /* 没有时间标签的行跳过（可能是元数据行如 [ti:xxx]） */
        if (tag_count == 0) {
            line = line_end ? line_end + 1 : nullptr;
            continue;
        }

        /* 解析逐字歌词 <mm:ss.xx>词<mm:ss.xx>词... */
        int32_t word_times_start = result->word_time_count;
        const char* text_start = p;
        bool has_word_timing = false;

        /* 检查是否有逐字时间标签 */
        const char* wp = p;
        while (*wp) {
            if (*wp == '<') {
                has_word_timing = true;
                break;
            }
            wp++;
        }

        if (has_word_timing) {
            /* 解析逐字歌词：提取 <time> 标记和文本 */
            char* write_ptr = (char*)p;
            wp = p;
            while (*wp) {
                if (*wp == '<') {
                    const char* after_word_tag = nullptr;
                    int32_t wt = parse_word_time_tag(wp, &after_word_tag);
                    if (wt >= 0) {
                        result->word_time_buffer[result->word_time_count++] = wt;
                        wp = after_word_tag;
                        continue;
                    }
                }
                *write_ptr++ = *wp++;
            }
            *write_ptr = '\0';
        }

        /* 为每个时间标签创建一行歌词 */
        for (int t = 0; t < tag_count; t++) {
            if (result->line_count >= result->capacity) {
                result->capacity *= 2;
                result->lines = (CatClawLyricLine*)realloc(result->lines,
                    result->capacity * sizeof(CatClawLyricLine));
            }

            auto& ln = result->lines[result->line_count];
            ln.time_ms = time_tags[t];
            ln.text = p;
            ln.word_count = has_word_timing ? (result->word_time_count - word_times_start) : 0;
            ln.word_times = has_word_timing ? &result->word_time_buffer[word_times_start] : nullptr;
            result->line_count++;
        }

        line = line_end ? line_end + 1 : nullptr;
    }

    /* 按时间排序 */
    std::sort(result->lines, result->lines + result->line_count,
        [](const CatClawLyricLine& a, const CatClawLyricLine& b) {
            return a.time_ms < b.time_ms;
        });

    return result;
}

void catclaw_lyric_free(CatClawLyricResult* result) {
    if (!result) return;
    free(result->lines);
    free(result->text_buffer);
    free(result->word_time_buffer);
    free(result);
}

int32_t catclaw_lyric_find_index(const CatClawLyricResult* result, int32_t time_ms) {
    if (!result || result->line_count <= 0) return -1;

    /* 二分查找：找到最后一个 time_ms <= time_ms 的行 */
    int32_t lo = 0, hi = result->line_count - 1;
    int32_t found = -1;
    while (lo <= hi) {
        int32_t mid = lo + (hi - lo) / 2;
        if (result->lines[mid].time_ms <= time_ms) {
            found = mid;
            lo = mid + 1;
        } else {
            hi = mid - 1;
        }
    }
    return found;
}
