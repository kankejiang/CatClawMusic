plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.compose)
    alias(libs.plugins.ksp)
    alias(libs.plugins.hilt)
}

android {
    namespace = "com.catclaw.music.kt"
    compileSdk = 37
    compileSdkMinor = 2

    defaultConfig {
        // 与 MAUI 版（com.catclaw.music / .debug）刻意分开：三包共存，A/B 对拍不被覆盖
        applicationId = "com.catclaw.music.kt"
        minSdk = 31
        targetSdk = 37
        versionCode = 1
        versionName = "0.1.0"

        // 清单原本只写 arm64-v8a；这里额外留 x86_64 是给 x86_64 安卓模拟器的安装路径，
        // 因为包里唯一的原生库（libandroidx.graphics.path.so）一旦只剩 arm64，
        // x86_64 设备会直接 INSTALL_FAILED_NO_MATCHING_ABIS。armeabi-v7a / x86 是真没设备要，裁掉。
        ndk {
            abiFilters += setOf("arm64-v8a", "x86_64")
        }
    }

    buildTypes {
        debug {
            applicationIdSuffix = ".debug"
        }
        release {
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"),
                "proguard-rules.pro",
            )
            // 不在这里放签名：正式签名由发布管线注入，本地验证用 -Pandroid.injected.signing.* 挂 debug keystore
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    buildFeatures {
        compose = true
    }

    packaging {
        resources.excludes += "/META-INF/{AL2.0,LGPL2.1}"
    }
}

dependencies {
    implementation(project(":core"))
    implementation(project(":data"))

    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.activity.compose)
    implementation(libs.androidx.lifecycle.runtime.compose)
    implementation(libs.androidx.lifecycle.viewmodel.compose)
    implementation(libs.androidx.navigation.compose)

    implementation(platform(libs.androidx.compose.bom))
    implementation(libs.androidx.compose.ui)
    implementation(libs.androidx.compose.ui.graphics)
    implementation(libs.androidx.compose.material3)
    implementation(libs.backdrop)
    // 背景模糊现成方案：替代自研的 RenderEffect 图层方案（后者真机不生效）
    implementation(libs.haze.android)
    implementation(libs.androidx.compose.ui.tooling.preview)
    debugImplementation(libs.androidx.compose.ui.tooling)

    implementation(libs.hilt.android)
    implementation(libs.androidx.hilt.navigation.compose)
    ksp(libs.hilt.compiler)

    testImplementation(libs.junit)
}
