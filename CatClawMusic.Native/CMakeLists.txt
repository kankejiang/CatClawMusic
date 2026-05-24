cmake_minimum_required(VERSION 3.22)
project(catclaw_native CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_library(catclaw_native SHARED
    src/fft.cpp
    src/lrc_parser.cpp
    src/tag_reader.cpp
    src/native_bridge.cpp
)

target_include_directories(catclaw_native PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}/include
)

# 链接 Android log 库
find_library(log-lib log)
target_link_libraries(catclaw_native ${log-lib})

# 优化选项：O2 优化 + NEON 支持（arm64）
target_compile_options(catclaw_native PRIVATE
    -O2
    -fvisibility=hidden
    -ffunction-sections
    -fdata-sections
)

# arm64 特定优化
if(ANDROID_ABI STREQUAL "arm64-v8a")
    target_compile_options(catclaw_native PRIVATE -march=armv8-a+fp+simd)
endif()

# 链接时优化 + 剔除未使用符号
target_link_options(catclaw_native PRIVATE
    -Wl,--gc-sections
    -Wl,--strip-all
)
