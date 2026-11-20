// 从 Lyrico（Apache-2.0）移植的 TagLib JNI 绑定层。
// 原项目：https://github.com/Replica0110/Lyrico —— 模块 lyrico-audiotag。
// 本地改动：NDK/CMake 版本对齐本工程（NDK 27 / CMake 3.22.1）、ABI 与 app 一致、
//          去掉 Parcelable（这些数据类只在进程内使用）。
plugins {
    alias(libs.plugins.android.library)
}

android {
    namespace = "com.lonx.audiotag"
    compileSdk = 37
    compileSdkMinor = 2
    ndkVersion = "27.0.12077973"

    defaultConfig {
        minSdk = 31
        // 原生 JNI 只能在设备上验证，用 instrumentation 测试跑往返
        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
        consumerProguardFiles("consumer-rules.pro")
        ndk {
            // 与 :app 的 abiFilters 保持一致，避免打包时缺 so
            abiFilters += setOf("arm64-v8a", "x86_64")
        }
        externalNativeBuild {
            cmake {
                cppFlags += ""
            }
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    externalNativeBuild {
        cmake {
            path = file("src/main/cpp/CMakeLists.txt")
            version = "3.22.1"
        }
    }
}

dependencies {
    implementation(libs.androidx.core.ktx)
    implementation(libs.kotlinx.coroutines.android)

    androidTestImplementation(libs.androidx.test.runner)
    androidTestImplementation(libs.androidx.test.ext.junit)
}
