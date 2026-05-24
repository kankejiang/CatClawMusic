/**
 * @file tag_reader.cpp
 * @brief 轻量级音频标签读取器
 *
 * 支持：
 * - ID3v2.3 / ID3v2.4 标签（MP3）
 * - Vorbis Comment 标签（FLAC / OGG）
 * - 基本元数据提取：标题、艺术家、专辑、年份、音轨、时长
 * - 封面图片提取（APIC / METADATA_BLOCK_PICTURE）
 *
 * 设计原则：只读取最常用的字段，避免 TagLib 的重量级依赖。
 * 对于扫描场景，速度比完整性更重要。
 */

#include "catclaw_native.h"
#include <cstring>
#include <cstdlib>
#include <cstdio>
#include <algorithm>

#if defined(__ANDROID__)
#include <android/log.h>
#define TAG_LOG(...) __android_log_print(ANDROID_LOG_DEBUG, "CatClawNative", __VA_ARGS__)
#else
#define TAG_LOG(...) printf(__VA_ARGS__)
#endif

namespace {

/* ============================================================
 * 通用辅助函数
 * ============================================================ */

/**
 * @brief 从字节数组读取大端序 32 位整数
 */
inline uint32_t read_be32(const uint8_t* p) {
    return ((uint32_t)p[0] << 24) | ((uint32_t)p[1] << 16) |
           ((uint32_t)p[2] << 8)  | (uint32_t)p[3];
}

/**
 * @brief 从字节数组读取大端序 24 位整数
 */
inline uint32_t read_be24(const uint8_t* p) {
    return ((uint32_t)p[0] << 16) | ((uint32_t)p[1] << 8) | (uint32_t)p[2];
}

/**
 * @brief 从字节数组读取小端序 32 位整数
 */
inline uint32_t read_le32(const uint8_t* p) {
    return (uint32_t)p[0] | ((uint32_t)p[1] << 8) |
           ((uint32_t)p[2] << 16) | ((uint32_t)p[3] << 24);
}

/**
 * @brief 从字节数组读取小端序 16 位整数
 */
inline uint16_t read_le16(const uint8_t* p) {
    return (uint16_t)p[0] | ((uint16_t)p[1] << 8);
}

/**
 * @brief 安全复制字符串到固定大小缓冲区
 */
void safe_copy(char* dst, const char* src, int32_t src_len, int32_t dst_size) {
    int32_t copy_len = std::min(src_len, dst_size - 1);
    memcpy(dst, src, copy_len);
    dst[copy_len] = '\0';
}

/**
 * @brief 去除字符串首尾空白
 */
void trim_string(char* str) {
    int32_t start = 0;
    while (str[start] == ' ' || str[start] == '\t' || str[start] == '\r' || str[start] == '\n')
        start++;
    int32_t end = (int32_t)strlen(str) - 1;
    while (end >= start && (str[end] == ' ' || str[end] == '\t' || str[end] == '\r' || str[end] == '\n'))
        end--;
    int32_t len = end - start + 1;
    if (start > 0) memmove(str, str + start, len);
    str[len] = '\0';
}

/**
 * @brief 读取整个文件到内存
 *
 * @param path 文件路径
 * @param out_size 返回文件大小
 * @return 文件数据（调用方 free），失败返回 nullptr
 */
uint8_t* read_file(const char* path, int32_t* out_size) {
    FILE* f = fopen(path, "rb");
    if (!f) return nullptr;

    fseek(f, 0, SEEK_END);
    long size = ftell(f);
    fseek(f, 0, SEEK_SET);

    if (size <= 0 || size > 100 * 1024 * 1024) { /* 限制 100MB */
        fclose(f);
        return nullptr;
    }

    auto* data = (uint8_t*)malloc(size);
    if (!data) {
        fclose(f);
        return nullptr;
    }

    size_t read_bytes = fread(data, 1, size, f);
    fclose(f);

    if ((long)read_bytes != size) {
        free(data);
        return nullptr;
    }

    *out_size = (int32_t)size;
    return data;
}

/* ============================================================
 * ID3v2 标签读取
 * ============================================================ */

/**
 * @brief 解析 ID3v2 同步安全整数（SyncSafe）
 *
 * ID3v2.4 使用 7 位编码：每个字节只有低 7 位有效
 */
uint32_t read_syncsafe(const uint8_t* p) {
    return ((uint32_t)p[0] << 21) | ((uint32_t)p[1] << 14) |
           ((uint32_t)p[2] << 7)  | (uint32_t)p[3];
}

/**
 * @brief 解析 ID3v2 文本帧
 *
 * 文本帧格式：编码字节(1) + 文本数据
 * 编码：0=ISO-8859-1, 1=UTF-16 BOM, 2=UTF-16 BE, 3=UTF-8
 */
void parse_id3_text(const uint8_t* data, int32_t size, char* out, int32_t out_size) {
    if (size <= 1) { out[0] = '\0'; return; }

    uint8_t encoding = data[0];
    const uint8_t* text = data + 1;
    int32_t text_len = size - 1;

    if (encoding == 3) {
        /* UTF-8：直接复制 */
        safe_copy(out, (const char*)text, text_len, out_size);
    } else if (encoding == 1) {
        /* UTF-16 BOM：简单提取 ASCII 范围字符 */
        int32_t out_pos = 0;
        if (text_len >= 2 && text[0] == 0xFF && text[1] == 0xFE) {
            /* UTF-16 LE */
            for (int32_t i = 2; i + 1 < text_len && out_pos < out_size - 1; i += 2) {
                uint16_t ch = text[i] | (text[i+1] << 8);
                if (ch <= 0x7F) out[out_pos++] = (char)ch;
                else if (ch >= 0x4E00 && ch <= 0x9FFF) {
                    /* CJK 字符：简单替换为 '?' */
                    out[out_pos++] = '?';
                }
            }
        } else if (text_len >= 2 && text[0] == 0xFE && text[1] == 0xFF) {
            /* UTF-16 BE */
            for (int32_t i = 2; i + 1 < text_len && out_pos < out_size - 1; i += 2) {
                uint16_t ch = (text[i] << 8) | text[i+1];
                if (ch <= 0x7F) out[out_pos++] = (char)ch;
                else out[out_pos++] = '?';
            }
        }
        out[out_pos] = '\0';
    } else {
        /* ISO-8859-1 或其他：直接复制 */
        safe_copy(out, (const char*)text, text_len, out_size);
    }
    trim_string(out);
}

/**
 * @brief 读取 ID3v2 标签
 *
 * @param data  文件数据
 * @param size  文件大小
 * @param info  输出标签信息
 * @return 0 成功，-1 无 ID3v2 标签
 */
int read_id3v2(const uint8_t* data, int32_t size, CatClawTagInfo* info) {
    /* 检查 ID3v2 头部：'ID3' + 版本(2B) + 标志(1B) + 大小(4B SyncSafe) */
    if (size < 10 || data[0] != 'I' || data[1] != 'D' || data[2] != '3')
        return -1;

    uint8_t version_major = data[3];
    uint8_t version_minor = data[4];
    uint8_t flags = data[5];
    uint32_t tag_size = read_syncsafe(data + 6);

    if (tag_size > (uint32_t)size - 10) tag_size = (uint32_t)size - 10;

    /* 是否有扩展头 */
    uint32_t offset = 10;
    if (flags & 0x40) {
        /* 扩展头大小 */
        if (version_major == 4) {
            uint32_t ext_size = read_syncsafe(data + offset);
            offset += ext_size;
        } else {
            uint32_t ext_size = read_be32(data + offset);
            offset += ext_size + 4;
        }
    }

    /* 遍历帧 */
    while (offset + 10 <= 10 + tag_size) {
        /* 帧头：Frame ID(4B) + Size(4B) + Flags(2B) */
        const uint8_t* frame = data + offset;
        char frame_id[5] = { (char)frame[0], (char)frame[1], (char)frame[2], (char)frame[3], '\0' };

        uint32_t frame_size;
        if (version_major == 4) {
            frame_size = read_syncsafe(frame + 4);
        } else {
            frame_size = read_be32(frame + 4);
        }

        if (frame_size == 0 || frame_size > tag_size) break;

        const uint8_t* frame_data = frame + 10;
        int32_t frame_data_size = (int32_t)frame_size;

        /* 解析常用帧 */
        if (strcmp(frame_id, "TIT2") == 0) {
            parse_id3_text(frame_data, frame_data_size, info->title, sizeof(info->title));
        } else if (strcmp(frame_id, "TPE1") == 0) {
            parse_id3_text(frame_data, frame_data_size, info->artist, sizeof(info->artist));
        } else if (strcmp(frame_id, "TALB") == 0) {
            parse_id3_text(frame_data, frame_data_size, info->album, sizeof(info->album));
        } else if (strcmp(frame_id, "TPE2") == 0) {
            parse_id3_text(frame_data, frame_data_size, info->album_artist, sizeof(info->album_artist));
        } else if (strcmp(frame_id, "TCON") == 0) {
            parse_id3_text(frame_data, frame_data_size, info->genre, sizeof(info->genre));
        } else if (strcmp(frame_id, "TYER") == 0 || strcmp(frame_id, "TDRC") == 0) {
            char year_str[16] = {};
            parse_id3_text(frame_data, frame_data_size, year_str, sizeof(year_str));
            info->year = atoi(year_str);
        } else if (strcmp(frame_id, "TRCK") == 0) {
            char track_str[16] = {};
            parse_id3_text(frame_data, frame_data_size, track_str, sizeof(track_str));
            info->track = atoi(track_str);
        } else if (strcmp(frame_id, "TPOS") == 0) {
            char disc_str[16] = {};
            parse_id3_text(frame_data, frame_data_size, disc_str, sizeof(disc_str));
            info->disc = atoi(disc_str);
        } else if (strcmp(frame_id, "APIC") == 0) {
            /* 封面图片帧 */
            info->has_cover = true;
            info->cover_offset = (int32_t)(frame_data - data);
            info->cover_size = frame_data_size;
            /* 提取 MIME 类型 */
            const char* mime = (const char*)(frame_data + 1);
            int32_t mime_len = (int32_t)strnlen(mime, frame_data_size - 1);
            safe_copy(info->cover_mime, mime, mime_len, sizeof(info->cover_mime));
        }

        offset += 10 + frame_size;
    }

    return 0;
}

/* ============================================================
 * Vorbis Comment 标签读取（FLAC / OGG）
 * ============================================================ */

/**
 * @brief 在 FLAC 文件中查找 Vorbis Comment 块
 *
 * FLAC 文件结构：fLaC 标记 + Metadata Block(s) + 音频帧
 * 每个 Metadata Block：类型(1B, bit7=last) + 长度(3B) + 数据
 */
int read_flac_vorbis_comment(const uint8_t* data, int32_t size, CatClawTagInfo* info) {
    /* 检查 fLaC 标记 */
    if (size < 4 || data[0] != 'f' || data[1] != 'L' || data[2] != 'a' || data[3] != 'C')
        return -1;

    uint32_t offset = 4;
    bool found_vorbis = false;
    bool found_streaminfo = false;

    while (offset + 4 <= (uint32_t)size) {
        uint8_t block_type = data[offset] & 0x7F;
        bool is_last = (data[offset] & 0x80) != 0;
        uint32_t block_size = read_be24(data + offset + 1);

        if (offset + 4 + block_size > (uint32_t)size) break;

        if (block_type == 0) {
            /* STREAMINFO 块：提取时长信息 */
            if (block_size >= 18) {
                int32_t sample_rate = (data[offset+4] << 12) | (data[offset+5] << 4) | (data[offset+6] >> 4);
                uint64_t total_samples = ((uint64_t)(data[offset+6] & 0x0F) << 32) |
                    ((uint64_t)data[offset+7] << 24) | ((uint64_t)data[offset+8] << 16) |
                    ((uint64_t)data[offset+9] << 8) | (uint64_t)data[offset+10];
                info->sample_rate = sample_rate;
                info->channels = ((data[offset+6] >> 1) & 0x07) + 1;
                if (sample_rate > 0) {
                    info->duration_ms = (int32_t)(total_samples * 1000 / sample_rate);
                }
                found_streaminfo = true;
            }
        } else if (block_type == 4) {
            /* VORBIS_COMMENT 块 */
            const uint8_t* vc = data + offset + 4;
            int32_t vc_size = (int32_t)block_size;

            if (vc_size < 4) break;
            uint32_t vendor_len = read_le32(vc);
            if (4 + vendor_len > (uint32_t)vc_size) break;

            uint32_t comment_offset = 4 + vendor_len;
            if (comment_offset + 4 > (uint32_t)vc_size) break;
            uint32_t comment_count = read_le32(vc + comment_offset);
            comment_offset += 4;

            for (uint32_t i = 0; i < comment_count && comment_offset + 4 <= (uint32_t)vc_size; i++) {
                uint32_t comment_len = read_le32(vc + comment_offset);
                comment_offset += 4;
                if (comment_offset + comment_len > (uint32_t)vc_size) break;

                const char* comment = (const char*)(vc + comment_offset);
                comment_offset += comment_len;

                /* 解析 KEY=VALUE 格式 */
                char key[64] = {};
                const char* eq = (const char*)memchr(comment, '=', comment_len);
                if (!eq) continue;

                int32_t key_len = (int32_t)(eq - comment);
                safe_copy(key, comment, key_len, sizeof(key));

                const char* value = eq + 1;
                int32_t value_len = (int32_t)(comment_len - key_len - 1);

                /* 转换 key 为大写进行比较 */
                for (int32_t k = 0; key[k]; k++) key[k] = (key[k] >= 'a' && key[k] <= 'z') ? key[k] - 32 : key[k];

                if (strcmp(key, "TITLE") == 0) {
                    safe_copy(info->title, value, value_len, sizeof(info->title));
                } else if (strcmp(key, "ARTIST") == 0) {
                    safe_copy(info->artist, value, value_len, sizeof(info->artist));
                } else if (strcmp(key, "ALBUM") == 0) {
                    safe_copy(info->album, value, value_len, sizeof(info->album));
                } else if (strcmp(key, "ALBUMARTIST") == 0) {
                    safe_copy(info->album_artist, value, value_len, sizeof(info->album_artist));
                } else if (strcmp(key, "GENRE") == 0) {
                    safe_copy(info->genre, value, value_len, sizeof(info->genre));
                } else if (strcmp(key, "DATE") == 0) {
                    char year_str[16] = {};
                    safe_copy(year_str, value, value_len, sizeof(year_str));
                    info->year = atoi(year_str);
                } else if (strcmp(key, "TRACKNUMBER") == 0) {
                    char track_str[16] = {};
                    safe_copy(track_str, value, value_len, sizeof(track_str));
                    info->track = atoi(track_str);
                } else if (strcmp(key, "DISCNUMBER") == 0) {
                    char disc_str[16] = {};
                    safe_copy(disc_str, value, value_len, sizeof(disc_str));
                    info->disc = atoi(disc_str);
                }
            }
            found_vorbis = true;
        } else if (block_type == 6) {
            /* PICTURE 块：封面图片 */
            const uint8_t* pic = data + offset + 4;
            int32_t pic_size = (int32_t)block_size;
            if (pic_size >= 4) {
                info->has_cover = true;
                info->cover_offset = (int32_t)(pic - data);
                info->cover_size = pic_size;
                /* 提取 MIME 类型 */
                uint32_t mime_len = read_be32(pic);
                if (mime_len < 64 && 4 + mime_len <= (uint32_t)pic_size) {
                    safe_copy(info->cover_mime, (const char*)(pic + 4), (int32_t)mime_len, sizeof(info->cover_mime));
                }
            }
        }

        if (is_last) break;
        offset += 4 + block_size;
    }

    return found_vorbis || found_streaminfo ? 0 : -1;
}

/* ============================================================
 * MP3 帧头解析（计算时长）
 * ============================================================ */

/**
 * @brief 从 MP3 帧头计算比特率和采样率
 *
 * MP3 帧头格式：11 位同步字 + MPEG 版本 + 层 + 比特率索引 + 采样率索引
 */
bool parse_mp3_frame_header(uint32_t header, int32_t* bitrate, int32_t* sample_rate, int32_t* channels) {
    /* 检查同步字 */
    if ((header & 0xFFE00000) != 0xFFE00000) return false;

    int version = (header >> 19) & 0x03;  /* 00=MPEG2.5, 01=reserved, 10=MPEG2, 11=MPEG1 */
    int layer = (header >> 17) & 0x03;    /* 01=Layer3, 10=Layer2, 11=Layer1 */
    int bitrate_idx = (header >> 12) & 0x0F;
    int samplerate_idx = (header >> 10) & 0x03;
    int channel_mode = (header >> 6) & 0x03;

    if (version == 1 || layer == 0 || bitrate_idx == 0 || bitrate_idx == 15 || samplerate_idx == 3)
        return false;

    /* 比特率表（kbps） */
    static const int bitrate_table[2][3][16] = {
        /* MPEG1 */
        { {0,32,64,96,128,160,192,224,256,288,320,352,384,416,448,0}, /* Layer1 */
          {0,32,48,56,64,80,96,112,128,160,192,224,256,320,384,0},    /* Layer2 */
          {0,32,40,48,56,64,80,96,112,128,160,192,224,256,320,0} },   /* Layer3 */
        /* MPEG2/2.5 */
        { {0,32,48,56,64,80,96,112,128,144,160,176,192,224,256,0},
          {0,8,16,24,32,40,48,56,64,80,96,112,128,144,160,0},
          {0,8,16,24,32,40,48,56,64,80,96,112,128,144,160,0} }
    };

    /* 采样率表 */
    static const int samplerate_table[3][3] = {
        {44100, 48000, 32000}, /* MPEG1 */
        {22050, 24000, 16000}, /* MPEG2 */
        {11025, 12000, 8000}   /* MPEG2.5 */
    };

    int v = (version == 3) ? 0 : 1; /* 0=MPEG1, 1=MPEG2/2.5 */
    int l = layer - 1;

    *bitrate = bitrate_table[v][l][bitrate_idx] * 1000;
    *sample_rate = samplerate_table[version == 3 ? 0 : (version == 2 ? 1 : 2)][samplerate_idx];
    *channels = (channel_mode == 3) ? 1 : 2;

    return true;
}

/**
 * @brief 从 MP3 文件计算时长
 *
 * 方法：找到第一个有效帧头，根据比特率和文件大小估算时长
 */
void compute_mp3_duration(const uint8_t* data, int32_t size, int32_t id3_size, CatClawTagInfo* info) {
    /* 跳过 ID3v2 标签 */
    int32_t offset = id3_size;
    int32_t bitrate = 0, sample_rate = 0, channels = 0;

    /* 搜索第一个有效帧头 */
    while (offset + 4 <= size) {
        uint32_t header = read_be32(data + offset);
        if (parse_mp3_frame_header(header, &bitrate, &sample_rate, &channels)) {
            break;
        }
        offset++;
    }

    if (bitrate > 0) {
        /* 估算时长 = 音频数据大小 * 8 / 比特率 */
        int32_t audio_size = size - offset;
        /* 减去 ID3v1 标签（128 字节，文件末尾） */
        if (size >= 128 && data[size - 128] == 'T' && data[size - 127] == 'A' && data[size - 126] == 'G')
            audio_size -= 128;

        info->duration_ms = (int32_t)((int64_t)audio_size * 8 * 1000 / bitrate);
        info->bitrate_kbps = bitrate / 1000;
        info->sample_rate = sample_rate;
        info->channels = channels;
    }
}

} /* anonymous namespace */

/* ============================================================
 * 公共接口实现
 * ============================================================ */

int32_t catclaw_read_tags(const char* file_path, CatClawTagInfo* info) {
    if (!file_path || !info) return -1;

    /* 初始化输出 */
    memset(info, 0, sizeof(CatClawTagInfo));

    int32_t file_size = 0;
    uint8_t* data = read_file(file_path, &file_size);
    if (!data) return -1;

    int result = -1;

    /* 检测文件类型并读取标签 */
    if (file_size >= 3 && data[0] == 'I' && data[1] == 'D' && data[2] == '3') {
        /* MP3 + ID3v2 标签 */
        int id3_result = read_id3v2(data, file_size, info);

        /* 计算 ID3v2 标签总大小 */
        uint32_t tag_size = read_syncsafe(data + 6);
        int32_t id3_total = 10 + (int32_t)tag_size;

        /* 计算 MP3 时长 */
        compute_mp3_duration(data, file_size, id3_total, info);

        result = (id3_result == 0) ? 0 : 0; /* 即使 ID3 读取失败，时长也可能有效 */
    } else if (file_size >= 4 && data[0] == 'f' && data[1] == 'L' && data[2] == 'a' && data[3] == 'C') {
        /* FLAC 文件 */
        result = read_flac_vorbis_comment(data, file_size, info);
    } else if (file_size >= 4 && data[0] == 'O' && data[1] == 'g' && data[2] == 'g' && data[3] == 'S') {
        /* OGG 文件：简化处理，只读取基本信息 */
        result = -1; /* OGG Vorbis Comment 需要更复杂的解析，暂不支持 */
    } else {
        /* 尝试作为 MP3 解析（无 ID3v2 标签） */
        int32_t bitrate = 0, sample_rate = 0, channels = 0;
        for (int32_t i = 0; i + 4 <= file_size; i++) {
            uint32_t header = read_be32(data + i);
            if (parse_mp3_frame_header(header, &bitrate, &sample_rate, &channels)) {
                info->sample_rate = sample_rate;
                info->channels = channels;
                int32_t audio_size = file_size - i;
                if (bitrate > 0) {
                    info->duration_ms = (int32_t)((int64_t)audio_size * 8 * 1000 / bitrate);
                    info->bitrate_kbps = bitrate / 1000;
                }
                result = 0;
                break;
            }
        }
    }

    free(data);
    return result;
}

int32_t catclaw_read_cover(const char* file_path, uint8_t* out_data, int32_t* out_size) {
    if (!file_path || !out_size) return -1;

    CatClawTagInfo info = {};
    if (catclaw_read_tags(file_path, &info) != 0 || !info.has_cover)
        return -1;

    /* 重新读取文件获取封面数据 */
    int32_t file_size = 0;
    uint8_t* data = read_file(file_path, &file_size);
    if (!data) return -1;

    int32_t result = -1;

    if (file_size >= 3 && data[0] == 'I' && data[1] == 'D' && data[2] == '3') {
        /* ID3v2 APIC 帧：编码(1) + MIME\0 + 图片类型(1) + 描述\0 + 图片数据 */
        if (info.cover_offset > 0 && info.cover_offset + info.cover_size <= file_size) {
            const uint8_t* apic = data + info.cover_offset;
            int32_t apic_size = info.cover_size;

            /* 跳过编码字节 */
            int32_t pos = 1;
            /* 跳过 MIME（null-terminated） */
            while (pos < apic_size && apic[pos] != '\0') pos++;
            pos++; /* 跳过 null */
            /* 跳过图片类型 */
            if (pos < apic_size) pos++;
            /* 跳过描述（null-terminated） */
            while (pos < apic_size && apic[pos] != '\0') pos++;
            pos++; /* 跳过 null */

            int32_t image_size = apic_size - pos;
            if (image_size > 0) {
                if (!out_data) {
                    *out_size = image_size;
                    result = 1; /* 缓冲区不足 */
                } else if (image_size <= *out_size) {
                    memcpy(out_data, apic + pos, image_size);
                    *out_size = image_size;
                    result = 0;
                } else {
                    *out_size = image_size;
                    result = 1;
                }
            }
        }
    } else if (file_size >= 4 && data[0] == 'f' && data[1] == 'L') {
        /* FLAC PICTURE 块 */
        if (info.cover_offset > 0 && info.cover_offset + info.cover_size <= file_size) {
            const uint8_t* pic = data + info.cover_offset;
            int32_t pic_size = info.cover_size;

            /* PICTURE 块格式：类型(4B) + MIME长度(4B) + MIME + 描述长度(4B) + 描述 + 宽高色深(4*4B) + 数据长度(4B) + 数据 */
            int32_t pos = 4; /* 跳过类型 */
            uint32_t mime_len = read_be32(pic + pos); pos += 4;
            pos += mime_len; /* 跳过 MIME */
            uint32_t desc_len = read_be32(pic + pos); pos += 4;
            pos += desc_len; /* 跳过描述 */
            pos += 16; /* 跳过宽高色深(4*4B) */
            uint32_t image_size = read_be32(pic + pos); pos += 4;

            if (image_size > 0 && pos + image_size <= (uint32_t)pic_size) {
                if (!out_data) {
                    *out_size = (int32_t)image_size;
                    result = 1;
                } else if ((int32_t)image_size <= *out_size) {
                    memcpy(out_data, pic + pos, image_size);
                    *out_size = (int32_t)image_size;
                    result = 0;
                } else {
                    *out_size = (int32_t)image_size;
                    result = 1;
                }
            }
        }
    }

    free(data);
    return result;
}

int32_t catclaw_scan_directory(
    const char* dir_path,
    bool recursive,
    void (*callback)(const CatClawTagInfo*, void*),
    void* user_data)
{
    /* 目录扫描需要平台特定的文件系统 API，
     * Android 上使用 AAssetManager 或 POSIX opendir。
     * 当前版本暂不实现，由 C# 层负责文件发现，
     * C++ 层只负责单个文件的标签读取。 */
    return -1;
}
