# Application.mk - ndk-build 应用配置

# 目标 ABI
APP_ABI := arm64-v8a armeabi-v7a x86_64

# 最低 Android API
APP_PLATFORM := android-24

# C++ 运行时库（静态链接，避免依赖系统版本）
APP_STL := c++_static

# 优化级别
APP_CPPFLAGS := -O2
