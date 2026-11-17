plugins {
    alias(libs.plugins.android.library)
    alias(libs.plugins.ksp)
    alias(libs.plugins.hilt)
}

android {
    namespace = "com.catclaw.music.kt.data"
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
    // 导出 Room schema，供 SchemaParityTest 与 MAUI 真实库逐表逐列比对
    arg("room.schemaLocation", "$projectDir/schemas")
    arg("room.warnOnMigrationMissing", "true")
}

dependencies {
    api(project(":core"))

    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.media3.session)
    implementation(libs.androidx.media3.exoplayer)
    implementation(libs.kotlinx.coroutines.android)
    implementation(libs.okhttp)
    implementation(libs.smbj)
    // 列共享（NetShareEnum）只有 jcifs-ng 能做；smbj 缺这套 RPC。
    // 其余 SMB 操作（列目录 / 读取）继续用 smbj，两条链路互不干扰。
    implementation(libs.jcifs.ng)
    implementation(libs.slf4j.api)
    implementation(libs.bcprov)

    api(libs.androidx.room.runtime)
    api(libs.androidx.room.ktx)
    ksp(libs.androidx.room.compiler)

    implementation(libs.hilt.android)
    ksp(libs.hilt.compiler)

    testImplementation(libs.junit)
    testImplementation(libs.kotlinx.coroutines.test)
    testImplementation(libs.sqlite.jdbc)
}
