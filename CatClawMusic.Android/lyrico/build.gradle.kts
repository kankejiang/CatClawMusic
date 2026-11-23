// Lyrico 功能移植层：源插件运行时、标签/歌词/封面搜索、批量任务、数据持久化。
// 参考实现：https://github.com/Replica0110/Lyrico （Apache-2.0）
//
// 与 app 的分工：
//   :app      —— Compose 页面
//   :lyrico   —— 从 Lyrico 移植的业务层（本模块）
//   :data     —— CatClaw 既有 Room/网络层（本模块可依赖它取曲库数据）
//   :lyrico-audiotag —— TagLib 原生标签读写
plugins {
    alias(libs.plugins.android.library)
    alias(libs.plugins.kotlin.serialization)
    alias(libs.plugins.ksp)
    alias(libs.plugins.hilt)
}

android {
    namespace = "com.catclaw.music.kt.lyrico"
    compileSdk = 37
    compileSdkMinor = 2

    defaultConfig {
        minSdk = 31
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    testOptions {
        unitTests.isReturnDefaultValues = true
    }
}

ksp {
    // 独立数据库：不碰 catclaw.db，SchemaParityTest 保持绿
    arg("room.schemaLocation", "$projectDir/schemas")
    arg("room.warnOnMigrationMissing", "true")
}

dependencies {
    api(project(":core"))
    api(project(":data"))
    api(project(":lyrico-audiotag"))

    implementation(libs.androidx.core.ktx)
    implementation(libs.kotlinx.coroutines.android)
    implementation(libs.kotlinx.serialization.json)
    implementation(libs.okhttp)

    // Lyrico 源插件用的 JS 引擎（官方 QuickJS 的 Android 打包）。
    // ⚠ 用本地 patched AAR 而非 libs.quickjs：官方 0.9.2 的 libquickjs.so 是 4KB 页对齐，
    // 在 Android 15+ 的 16KB 页设备上会导致整包安装失败（INSTALL_FAILED_INVALID_APK）。
    // 本地 AAR 由 cashapp/quickjs-java 0.9.2 源码用 NDK 27 加 -Wl,-z,max-page-size=16384 重编，
    // 仅保留 arm64-v8a（与 :app 的 abiFilters 一致），JNI 符号与原版完全相同。
    // 通过 settings.gradle.kts 里的 flatDir("lyrico/libs") 解析（library 模块不能直接用 files(...) 引 AAR）。
    implementation("app.cash.quickjs:quickjs-android:0.9.2-aligned")

    api(libs.androidx.room.runtime)
    api(libs.androidx.room.ktx)
    ksp(libs.androidx.room.compiler)

    implementation(libs.hilt.android)
    ksp(libs.hilt.compiler)

    testImplementation(libs.junit)
    testImplementation(libs.kotlinx.coroutines.test)
}
