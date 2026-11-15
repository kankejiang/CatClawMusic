plugins {
    alias(libs.plugins.android.library)
}

android {
    namespace = "com.catclaw.music.kt.core"
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

dependencies {
    implementation(libs.androidx.core.ktx)
    api(libs.androidx.media3.exoplayer)
    // FFmpeg 软解（Vorbis/Opus/Flac/ALAC/AAC/AC3/DTS/TrueHD…），平台解不了的格式兜底
    implementation(libs.nextlib.media3ext)
    api(libs.androidx.media3.session)
    api(libs.androidx.media3.datasource)
    api(libs.kotlinx.coroutines.android)
    api(libs.kotlinx.serialization.json)

    testImplementation(libs.junit)
    testImplementation(libs.kotlinx.coroutines.test)
}
