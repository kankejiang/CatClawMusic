# AI Agent C++ 原生模块集成说明

## 需要复制的文件

将以下文件从 `CatClawMusic.UI/cpp/` 复制到 `CatClawMusic.Native/` 对应目录：

```
cpp/catclaw_ai.h      → CatClawMusic.Native/include/catclaw_ai.h
cpp/ai_json_processor.cpp → CatClawMusic.Native/src/ai_json_processor.cpp
```

## CMakeLists.txt 修改

在 `CatClawMusic.Native/CMakeLists.txt` 中添加新源文件：

```cmake
add_library(catclaw_native SHARED
    src/fft.cpp
    src/lrc_parser.cpp
    src/tag_reader.cpp
    src/native_bridge.cpp
    src/color_extractor.cpp
    src/audio_processor.cpp
    src/spectrum_processor.cpp
    src/stack_blur.cpp
    src/ai_json_processor.cpp    # ← 新增
)
```

## 功能说明

C++ 原生 AI 模块提供以下高性能接口：

| 函数 | 说明 |
|------|------|
| `catclaw_ai_build_chat_request` | 构建 LLM API 请求体 JSON（手写序列化器，避免 C# 反射） |
| `catclaw_ai_parse_chat_response` | 解析 LLM API 响应 JSON（零拷贝原地解析） |
| `catclaw_ai_extract_string_arg` | 从工具参数 JSON 提取字符串字段 |
| `catclaw_ai_extract_int_arg` | 从工具参数 JSON 提取整数字段 |
| `catclaw_ai_build_url` | 构建 API URL（自动补全路径） |
| `catclaw_ai_free` | 释放 AI 模块分配的内存 |
| `catclaw_ai_free_response` | 释放响应解析结果 |

## C# 调用方式

C# 端通过 `NativeInterop` 类的 P/Invoke 调用，自动回退机制：
- 原生库可用时 → 使用 C++ 高性能实现
- 原生库不可用时 → 回退到 C# System.Text.Json 实现
