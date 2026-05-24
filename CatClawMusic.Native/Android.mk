# Android.mk - ndk-build 构建脚本
# 编译 CatClawMusic 原生库（FFT + LRC 解析 + 标签读取）

LOCAL_PATH := $(call my-dir)

include $(CLEAR_VARS)

LOCAL_MODULE    := catclaw_native
LOCAL_SRC_FILES := src/fft.cpp \
                   src/lrc_parser.cpp \
                   src/tag_reader.cpp \
                   src/native_bridge.cpp \
                   src/color_extractor.cpp \
                   src/spectrum_processor.cpp \
                   src/audio_processor.cpp \
                   src/stack_blur.cpp

LOCAL_C_INCLUDES := $(LOCAL_PATH)/include

# C++17 标准
LOCAL_CPPFLAGS := -std=c++17 -O2 -fvisibility=hidden -ffunction-sections -fdata-sections

# arm64 NEON 优化
ifeq ($(TARGET_ARCH_ABI),arm64-v8a)
    LOCAL_CPPFLAGS += -march=armv8-a+fp+simd
endif

# 链接 Android log 库
LOCAL_LDLIBS := -llog

# 链接时优化
LOCAL_LDFLAGS := -Wl,--gc-sections -Wl,--strip-all

include $(BUILD_SHARED_LIBRARY)
