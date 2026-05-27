LOCAL_PATH := $(call my-dir)/..

include $(CLEAR_VARS)
LOCAL_MODULE := catclaw_native
LOCAL_SRC_FILES := \
    src/fft.cpp \
    src/lrc_parser.cpp \
    src/tag_reader.cpp \
    src/native_bridge.cpp \
    src/color_extractor.cpp \
    src/audio_processor.cpp \
    src/spectrum_processor.cpp \
    src/stack_blur.cpp

LOCAL_C_INCLUDES := $(LOCAL_PATH)/include
LOCAL_CPPFLAGS := -std=c++17 -O2 -fvisibility=hidden -ffunction-sections -fdata-sections
LOCAL_LDLIBS := -llog

ifeq ($(TARGET_ARCH_ABI),arm64-v8a)
    LOCAL_CPPFLAGS += -march=armv8-a+fp+simd
endif

include $(BUILD_SHARED_LIBRARY)
