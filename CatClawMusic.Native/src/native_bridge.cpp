/**
 * @file native_bridge.cpp
 * @brief JNI 原生桥接层，提供库初始化和版本查询接口
 */

#include "catclaw_native.h"

#ifdef __cplusplus
extern "C" {
#endif

/**
 * @brief 获取原生库版本号
 * @return 版本号（100 = 1.0.0）
 */
int32_t catclaw_get_version() {
    return 100;
}

/**
 * @brief 初始化原生库
 *
 * 在 Android 上执行一次性初始化，如 CPU 特性检测等。
 * 目前为空实现，预留扩展。
 */
void catclaw_init() {
    /* 预留：CPU 特性检测、FFT 表预计算等 */
}

#ifdef __cplusplus
}
#endif
