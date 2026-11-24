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
    //
    // 要求 so 与 Java 类同源：Java 侧是 app.cash.quickjs.QuickJs，
    // 所以 libquickjs.so 必须导出 Java_app_cash_quickjs_* 符号。
    // 之前用过一份「16KB 页对齐」的 patched AAR，里面的 so 实际来自 zipline
    // （只导出 Java_app_cash_zipline_*），导致每次插件调用都报
    //   No implementation found for long app.cash.quickjs.QuickJs.createContext()
    // 表现为「7 个歌词源只出 LRCLIB 一个」。官方 0.9.2 的 so 符号正确。
    //
    // 已知限制：官方 so 是 4KB 页对齐，16KB 页设备（部分 Android 15+）无法安装，
    // 上架前需要用 NDK 重编成 16384 对齐。
    implementation(libs.quickjs)

    api(libs.androidx.room.runtime)
    api(libs.androidx.room.ktx)
    ksp(libs.androidx.room.compiler)

    implementation(libs.hilt.android)
    ksp(libs.hilt.compiler)

    testImplementation(libs.junit)
    testImplementation(libs.kotlinx.coroutines.test)
}
