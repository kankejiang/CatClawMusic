/**
 * @file ai_json_processor.cpp
 * @brief AI Agent JSON 高性能处理 - C++ 原生实现
 *
 * 提供 LLM API 请求构建、响应解析、工具参数提取的 C++ 原生实现。
 * 使用手写 JSON 解析器，避免 C# 反射和大量字符串分配开销。
 *
 * 编译后合并到 libcatclaw_native.so，通过 P/Invoke 调用。
 */

#include "catclaw_ai.h"
#include <cstdlib>
#include <cstring>
#include <cstdio>

namespace {

/* ============================================================
 * 简易 JSON 写入器（无动态内存，直接拼接到增长缓冲区）
 * ============================================================ */

struct JsonWriter {
    char* buf;
    int32_t len;
    int32_t cap;

    void init(int32_t initial_cap = 4096) {
        cap = initial_cap;
        buf = (char*)malloc(cap);
        len = 0;
        buf[0] = '\0';
    }

    void ensure(int32_t need) {
        if (len + need + 1 > cap) {
            while (len + need + 1 > cap) cap *= 2;
            buf = (char*)realloc(buf, cap);
        }
    }

    void put(char c) { ensure(1); buf[len++] = c; }

    void put(const char* s, int32_t slen = -1) {
        if (slen < 0) slen = (int32_t)strlen(s);
        ensure(slen);
        memcpy(buf + len, s, slen);
        len += slen;
    }

    void escape_and_put(const char* s) {
        if (!s) { put("null", 4); return; }
        put('"');
        for (const char* p = s; *p; p++) {
            switch (*p) {
                case '"':  put('\\'); put('"'); break;
                case '\\': put('\\'); put('\\'); break;
                case '\n': put('\\'); put('n'); break;
                case '\r': put('\\'); put('r'); break;
                case '\t': put('\\'); put('t'); break;
                default:
                    if ((unsigned char)*p < 0x20) {
                        char hex[8];
                        snprintf(hex, sizeof(hex), "\\u%04x", (unsigned char)*p);
                        put(hex);
                    } else {
                        put(*p);
                    }
                    break;
            }
        }
        put('"');
    }

    char* finish() {
        ensure(1);
        buf[len] = '\0';
        return buf;
    }
};

/* ============================================================
 * 简易 JSON 读取器（零分配原地解析）
 * ============================================================ */

struct JsonReader {
    const char* json;
    int32_t len;
    int32_t pos;

    void init(const char* j, int32_t l) { json = j; len = l; pos = 0; }

    void skip_ws() {
        while (pos < len && (json[pos] == ' ' || json[pos] == '\n' || json[pos] == '\r' || json[pos] == '\t'))
            pos++;
    }

    char peek() { skip_ws(); return pos < len ? json[pos] : '\0'; }

    char next() { skip_ws(); return pos < len ? json[pos++] : '\0'; }

    bool match(const char* s) {
        int32_t slen = (int32_t)strlen(s);
        if (pos + slen > len) return false;
        if (memcmp(json + pos, s, slen) != 0) return false;
        pos += slen;
        return true;
    }

    /* 读取 JSON 字符串值，返回 malloc 分配的 UTF-8 字符串 */
    char* read_string() {
        skip_ws();
        if (pos >= len || json[pos] != '"') return nullptr;
        pos++; /* skip opening quote */

        int32_t start = pos;
        /* 先扫描确定长度 */
        while (pos < len && json[pos] != '"') {
            if (json[pos] == '\\') pos++; /* skip escaped char */
            pos++;
        }

        /* 解码字符串（处理转义） */
        char* result = (char*)malloc(pos - start + 1);
        int32_t out = 0;
        int32_t p = start;
        while (p < pos) {
            if (json[p] == '\\' && p + 1 < pos) {
                p++;
                switch (json[p]) {
                    case '"':  result[out++] = '"'; break;
                    case '\\': result[out++] = '\\'; break;
                    case 'n':  result[out++] = '\n'; break;
                    case 'r':  result[out++] = '\r'; break;
                    case 't':  result[out++] = '\t'; break;
                    case 'u':  {
                        /* 简化：直接保留原样（UTF-8 场景下 \uXXXX 少见） */
                        result[out++] = '\\';
                        result[out++] = 'u';
                        break;
                    }
                    default: result[out++] = json[p]; break;
                }
            } else {
                result[out++] = json[p];
            }
            p++;
        }
        result[out] = '\0';

        if (pos < len) pos++; /* skip closing quote */
        return result;
    }

    /* 读取原始 JSON 值（字符串/数字/对象/数组），返回 malloc 分配的字符串 */
    char* read_raw_value() {
        skip_ws();
        if (pos >= len) return nullptr;

        int32_t start = pos;
        if (json[pos] == '"') {
            pos++;
            while (pos < len && json[pos] != '"') {
                if (json[pos] == '\\') pos++;
                pos++;
            }
            if (pos < len) pos++;
        } else if (json[pos] == '{' || json[pos] == '[') {
            char open = json[pos];
            char close = (open == '{') ? '}' : ']';
            int depth = 1;
            pos++;
            while (pos < len && depth > 0) {
                if (json[pos] == '"') {
                    pos++;
                    while (pos < len && json[pos] != '"') {
                        if (json[pos] == '\\') pos++;
                        pos++;
                    }
                    if (pos < len) pos++;
                    continue;
                }
                if (json[pos] == open) depth++;
                else if (json[pos] == close) depth--;
                pos++;
            }
        } else {
            /* number, bool, null */
            while (pos < len && json[pos] != ',' && json[pos] != '}' && json[pos] != ']' &&
                   json[pos] != ' ' && json[pos] != '\n' && json[pos] != '\r' && json[pos] != '\t')
                pos++;
        }

        int32_t vlen = pos - start;
        char* result = (char*)malloc(vlen + 1);
        memcpy(result, json + start, vlen);
        result[vlen] = '\0';
        return result;
    }

    /* 在当前对象中查找指定 key */
    bool find_key(const char* key) {
        skip_ws();
        if (pos >= len || json[pos] != '{') return false;
        pos++; /* skip { */

        while (pos < len) {
            skip_ws();
            if (json[pos] == '}') { pos++; return false; }
            if (json[pos] == ',') pos++;

            char* k = read_string();
            skip_ws();
            if (pos < len && json[pos] == ':') pos++;

            if (k && strcmp(k, key) == 0) {
                free(k);
                return true;
            }
            free(k);

            /* skip value */
            char* val = read_raw_value();
            free(val);
        }
        return false;
    }
};

} /* anonymous namespace */

/* ============================================================
 * 公共 API 实现
 * ============================================================ */

char* catclaw_ai_build_chat_request(
    const char* model,
    const CatClawChatMessage* messages,
    int32_t msg_count,
    const CatClawToolDef* tools,
    int32_t tool_count,
    double temperature,
    int32_t max_tokens)
{
    if (!model || !messages || msg_count <= 0) return nullptr;

    JsonWriter w;
    w.init(8192);

    w.put('{');
    w.put("\"model\":"); w.escape_and_put(model);
    w.put(",\"temperature\":");
    char temp_buf[32];
    snprintf(temp_buf, sizeof(temp_buf), "%.1f", temperature);
    w.put(temp_buf);
    w.put(",\"max_tokens\":");
    char mt_buf[16];
    snprintf(mt_buf, sizeof(mt_buf), "%d", max_tokens);
    w.put(mt_buf);

    /* messages */
    w.put(",\"messages\":[");
    for (int32_t i = 0; i < msg_count; i++) {
        if (i > 0) w.put(',');
        const auto& m = messages[i];
        w.put('{');
        w.put("\"role\":"); w.escape_and_put(m.role);

        if (m.role && strcmp(m.role, "assistant") == 0 && m.tool_calls && m.tool_call_count > 0) {
            w.put(",\"content\":null");
            w.put(",\"tool_calls\":[");
            for (int32_t j = 0; j < m.tool_call_count; j++) {
                if (j > 0) w.put(',');
                const auto& tc = m.tool_calls[j];
                w.put('{');
                w.put("\"id\":"); w.escape_and_put(tc.id);
                w.put(",\"type\":\"function\"");
                w.put(",\"function\":{\"name\":"); w.escape_and_put(tc.name);
                w.put(",\"arguments\":"); w.escape_and_put(tc.arguments);
                w.put("}}");
            }
            w.put(']');
        } else if (m.role && strcmp(m.role, "tool") == 0) {
            w.put(",\"content\":"); w.escape_and_put(m.content);
            if (m.tool_call_id) { w.put(",\"tool_call_id\":"); w.escape_and_put(m.tool_call_id); }
        } else {
            w.put(",\"content\":"); w.escape_and_put(m.content);
        }

        w.put('}');
    }
    w.put(']');

    /* tools */
    if (tools && tool_count > 0) {
        w.put(",\"tools\":[");
        for (int32_t i = 0; i < tool_count; i++) {
            if (i > 0) w.put(',');
            const auto& t = tools[i];
            w.put("{\"type\":\"function\",\"function\":{\"name\":"); w.escape_and_put(t.name);
            w.put(",\"description\":"); w.escape_and_put(t.description);
            w.put(",\"parameters\":{\"type\":\"object\",\"properties\":{");

            for (int32_t j = 0; j < t.param_count; j++) {
                if (j > 0) w.put(',');
                w.escape_and_put(t.param_names[j]);
                w.put(":{\"type\":"); w.escape_and_put(t.param_properties[j].type);
                w.put(",\"description\":"); w.escape_and_put(t.param_properties[j].description);
                if (t.param_properties[j].enum_count > 0 && t.param_properties[j].enum_values) {
                    w.put(",\"enum\":[");
                    for (int32_t k = 0; k < t.param_properties[j].enum_count; k++) {
                        if (k > 0) w.put(',');
                        w.escape_and_put(t.param_properties[j].enum_values[k]);
                    }
                    w.put(']');
                }
                w.put('}');
            }

            w.put("},\"required\":[");
            for (int32_t j = 0; j < t.required_count; j++) {
                if (j > 0) w.put(',');
                w.escape_and_put(t.required_params[j]);
            }
            w.put("]}}}");
        }
        w.put(']');
    }

    w.put('}');
    return w.finish();
}

CatClawLlmResponse* catclaw_ai_parse_chat_response(
    const char* response_json,
    int32_t json_len)
{
    if (!response_json || json_len <= 0) return nullptr;

    auto* result = (CatClawLlmResponse*)calloc(1, sizeof(CatClawLlmResponse));

    JsonReader r;
    r.init(response_json, json_len);

    /* 检查 error */
    if (r.find_key("error")) {
        JsonReader er = r;
        if (er.find_key("message")) {
            result->error = er.read_string();
        }
        return result;
    }

    /* 找 choices[0].message */
    if (!r.find_key("choices")) return result;

    skip_ws_wrapper(&r);
    if (r.peek() != '[') return result;
    r.next(); /* skip [ */

    /* 找 finish_reason */
    JsonReader fr_reader = r;

    /* 解析 message 对象 */
    skip_ws_wrapper(&r);
    if (r.peek() == '{') {
        r.next(); /* skip { */

        while (r.pos < r.len) {
            r.skip_ws();
            if (r.peek() == '}') break;
            if (r.peek() == ',') r.next();

            char* key = r.read_string();
            r.skip_ws();
            if (r.pos < r.len && r.json[r.pos] == ':') r.pos++;

            if (key && strcmp(key, "message") == 0) {
                free(key);
                /* 解析 message 对象 */
                skip_ws_wrapper(&r);
                if (r.peek() != '{') break;
                r.next();

                while (r.pos < r.len) {
                    r.skip_ws();
                    if (r.peek() == '}') break;
                    if (r.peek() == ',') r.next();

                    char* mk = r.read_string();
                    r.skip_ws();
                    if (r.pos < r.len && r.json[r.pos] == ':') r.pos++;

                    if (mk && strcmp(mk, "content") == 0) {
                        result->content = r.read_string();
                    } else if (mk && strcmp(mk, "tool_calls") == 0) {
                        /* 解析 tool_calls 数组 */
                        skip_ws_wrapper(&r);
                        if (r.peek() != '[') { free(mk); continue; }
                        r.next();

                        int32_t tc_cap = 8;
                        result->tool_calls = (decltype(result->tool_calls))malloc(
                            tc_cap * sizeof(decltype(*result->tool_calls)));
                        result->tool_call_count = 0;

                        while (r.pos < r.len) {
                            r.skip_ws();
                            if (r.peek() == ']') break;
                            if (r.peek() == ',') r.next();

                            if (r.peek() != '{') continue;
                            r.next();

                            if (result->tool_call_count >= tc_cap) {
                                tc_cap *= 2;
                                result->tool_calls = (decltype(result->tool_calls))realloc(
                                    result->tool_calls, tc_cap * sizeof(decltype(*result->tool_calls)));
                            }

                            auto& tc = result->tool_calls[result->tool_call_count];
                            memset(&tc, 0, sizeof(tc));

                            while (r.pos < r.len) {
                                r.skip_ws();
                                if (r.peek() == '}') { r.next(); break; }
                                if (r.peek() == ',') r.next();

                                char* tck = r.read_string();
                                r.skip_ws();
                                if (r.pos < r.len && r.json[r.pos] == ':') r.pos++;

                                if (tck && strcmp(tck, "id") == 0) {
                                    tc.id = r.read_string();
                                } else if (tck && strcmp(tck, "function") == 0) {
                                    skip_ws_wrapper(&r);
                                    if (r.peek() != '{') { free(tck); continue; }
                                    r.next();

                                    while (r.pos < r.len) {
                                        r.skip_ws();
                                        if (r.peek() == '}') { r.next(); break; }
                                        if (r.peek() == ',') r.next();

                                        char* fk = r.read_string();
                                        r.skip_ws();
                                        if (r.pos < r.len && r.json[r.pos] == ':') r.pos++;

                                        if (fk && strcmp(fk, "name") == 0) {
                                            tc.name = r.read_string();
                                        } else if (fk && strcmp(fk, "arguments") == 0) {
                                            tc.arguments = r.read_string();
                                        } else {
                                            char* skip = r.read_raw_value();
                                            free(skip);
                                        }
                                        free(fk);
                                    }
                                } else {
                                    char* skip = r.read_raw_value();
                                    free(skip);
                                }
                                free(tck);
                            }
                            result->tool_call_count++;
                        }
                    } else {
                        char* skip = r.read_raw_value();
                        free(skip);
                    }
                    free(mk);
                }
                break; /* message found and parsed */
            } else {
                char* skip = r.read_raw_value();
                free(skip);
            }
            free(key);
        }
    }

    /* 找 finish_reason */
    r = fr_reader;
    if (r.find_key("finish_reason")) {
        result->finish_reason = r.read_string();
    }

    return result;
}

/* 辅助：跳过空白 */
static void skip_ws_wrapper(JsonReader* r) { r->skip_ws(); }

char* catclaw_ai_extract_string_arg(
    const char* args_json,
    int32_t json_len,
    const char* key)
{
    if (!args_json || json_len <= 0 || !key) return nullptr;

    JsonReader r;
    r.init(args_json, json_len);

    if (r.find_key(key)) {
        return r.read_string();
    }
    return nullptr;
}

int32_t catclaw_ai_extract_int_arg(
    const char* args_json,
    int32_t json_len,
    const char* key,
    int32_t default_val)
{
    if (!args_json || json_len <= 0 || !key) return default_val;

    JsonReader r;
    r.init(args_json, json_len);

    if (r.find_key(key)) {
        r.skip_ws();
        char* raw = r.read_raw_value();
        if (raw) {
            int32_t val = atoi(raw);
            free(raw);
            return val;
        }
    }
    return default_val;
}

char* catclaw_ai_build_url(const char* base_url) {
    if (!base_url) return nullptr;

    int32_t blen = (int32_t)strlen(base_url);
    /* 去除尾部斜杠 */
    while (blen > 0 && base_url[blen-1] == '/') blen--;

    const char* suffix = "/v1/chat/completions";
    bool has_v1 = (blen >= 3 && memcmp(base_url + blen - 3, "/v1", 3) == 0);
    bool has_chat = (blen >= 19 && memcmp(base_url + blen - 19, "/chat/completions", 17) == 0);

    int32_t result_len;
    if (has_chat) {
        result_len = blen + 1;
    } else if (has_v1) {
        result_len = blen + 18; /* /chat/completions */
    } else {
        result_len = blen + 21; /* /v1/chat/completions */
    }

    char* result = (char*)malloc(result_len + 1);
    memcpy(result, base_url, blen);
    result[blen] = '\0';

    if (has_chat) {
        /* already complete */
    } else if (has_v1) {
        strcat(result, "/chat/completions");
    } else {
        strcat(result, "/v1/chat/completions");
    }

    return result;
}

void catclaw_ai_free(void* ptr) {
    free(ptr);
}

void catclaw_ai_free_response(CatClawLlmResponse* response) {
    if (!response) return;
    free(response->content);
    free(response->finish_reason);
    free(response->error);
    if (response->tool_calls) {
        for (int32_t i = 0; i < response->tool_call_count; i++) {
            free(response->tool_calls[i].id);
            free(response->tool_calls[i].name);
            free(response->tool_calls[i].arguments);
        }
        free(response->tool_calls);
    }
    free(response);
}
