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
