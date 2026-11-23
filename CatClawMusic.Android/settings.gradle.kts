pluginManagement {
    repositories {
        google()
        mavenCentral()
        gradlePluginPortal()
    }
}

dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
    repositories {
        google()
        mavenCentral()
        // 本地 patched AAR 仓库（quickjs 16KB 页对齐版，见 lyrico/build.gradle.kts 注释）。
        // 用 maven 布局而非 flatDir/files(...)：:lyrico 是 library 模块，
        // AGP 不允许直接依赖本地 .aar 文件，flatDir 的字符串记法在 Kotlin DSL 下也不成立。
        maven { url = uri("lyrico/libs/repo") }
    }
}

rootProject.name = "CatClawMusic.Android"

include(":app")
include(":core")
include(":data")
// 从 Lyrico（Apache-2.0）移植的 TagLib JNI 标签读写层
include(":lyrico-audiotag")
// 从 Lyrico 移植的业务层（源插件运行时 / 搜索 / 批量 / 独立数据库）
include(":lyrico")
