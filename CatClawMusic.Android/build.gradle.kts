// 根工程：只声明插件，不产出构件。
//
// 平台定位（2026-10 起）：本工程是猫爪音乐在 Android 上的**正式实现**，
// MAUI 版（CatClawMusic.Maui）的 net11.0-android 目标已弃用，仅作参考实现保留。
// 两端差异与移植口径见仓库根 docs/android-port-progress.md / android-port-decisions.md / android-port-page-parity.md。
plugins {
    alias(libs.plugins.android.application) apply false
    alias(libs.plugins.android.library) apply false
    alias(libs.plugins.kotlin.compose) apply false
    alias(libs.plugins.ksp) apply false
    alias(libs.plugins.hilt) apply false
}
