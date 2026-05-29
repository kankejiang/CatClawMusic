#pragma once

#include <stdint.h>
#include <stdbool.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef struct {
    char* content;
    char* finish_reason;
    int32_t tool_call_count;
    struct {
        char* id;
        char* name;
        char* arguments;
    }* tool_calls;
    char* error;
} CatClawLlmResponse;

typedef struct {
    const char* role;
    const char* content;
    const char* tool_call_id;
    const char* name;
    int32_t tool_call_count;
    struct {
        const char* id;
        const char* name;
        const char* arguments;
    }* tool_calls;
} CatClawChatMessage;

typedef struct {
    const char* type;
    const char* description;
    int32_t enum_count;
    const char** enum_values;
} CatClawParamProperty;

typedef struct {
    const char* name;
    const char* description;
    int32_t param_count;
    const char** param_names;
    CatClawParamProperty* param_properties;
    int32_t required_count;
    const char** required_params;
} CatClawToolDef;

char* catclaw_ai_build_chat_request(
    const char* model,
    const CatClawChatMessage* messages,
    int32_t msg_count,
    const CatClawToolDef* tools,
    int32_t tool_count,
    double temperature,
    int32_t max_tokens
);

CatClawLlmResponse* catclaw_ai_parse_chat_response(
    const char* response_json,
    int32_t json_len
);

char* catclaw_ai_extract_string_arg(
    const char* args_json,
    int32_t json_len,
    const char* key
);

int32_t catclaw_ai_extract_int_arg(
    const char* args_json,
    int32_t json_len,
    const char* key,
    int32_t default_val
);

char* catclaw_ai_build_url(const char* base_url);

void catclaw_ai_free(void* ptr);

void catclaw_ai_free_response(CatClawLlmResponse* response);

#ifdef __cplusplus
}
#endif
