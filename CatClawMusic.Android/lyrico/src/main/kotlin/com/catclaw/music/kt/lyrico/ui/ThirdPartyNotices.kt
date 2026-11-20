package com.catclaw.music.kt.lyrico.ui

/**
 * 第三方组件与许可声明。
 *
 * ## 为什么必须有这一页
 *
 * 移植引入的代码带着许可义务，不是「写个 NOTICE 文件就完事」：
 *  - **Apache-2.0**（Lyrico）：分发时要保留版权与许可声明，并说明修改过的文件
 *  - **MPL-1.1**（TagLib）：以独立共享库形式分发，需要可获取其源码
 *  - **BSL-1.0**（utfcpp）：保留许可声明
 *  - **MIT**（QuickJS / cashapp 打包）：保留版权声明
 *
 * 所以在应用内可见的位置列出组件、版本、许可与来源，是履行义务最直接的方式。
 *
 * ## 为什么不放完整许可全文
 *
 * 全文动辄上万字，塞进设置页会把界面撑成一本书。这里给出组件、许可名称与上游地址，
 * 并说明「完整文本随安装包内的 NOTICE 提供」——这是常见做法（多数 App 的
 * 「开源许可」页也是列组件 + 链接）。仓库里 `lyrico-audiotag/NOTICE.md` 有完整依据。
 */
data class ThirdPartyComponent(
    val name: String,
    val version: String,
    val license: String,
    val url: String,
    /** 在本项目里的用途，一句话 */
    val usage: String,
    /** 是否经过本地修改 */
    val modified: Boolean = false,
)

object ThirdPartyNotices {

    val components: List<ThirdPartyComponent> = listOf(
        ThirdPartyComponent(
            name = "Lyrico",
            version = "1.5.x",
            license = "Apache-2.0",
            url = "https://github.com/Replica0110/Lyrico",
            usage = "标签读写绑定、源插件规范与业务逻辑移植",
            modified = true,
        ),
        ThirdPartyComponent(
            name = "TagLib",
            version = "2.0.2",
            license = "MPL-1.1",
            url = "https://github.com/taglib/taglib",
            usage = "音频标签读写（以 libtaglib.so 独立分发）",
        ),
        ThirdPartyComponent(
            name = "utfcpp",
            version = "4.x",
            license = "BSL-1.0",
            url = "https://github.com/nemtrif/utfcpp",
            usage = "TagLib 构建期依赖（Unicode 转换）",
        ),
        ThirdPartyComponent(
            name = "QuickJS",
            version = "2021-03-27 及以后",
            license = "MIT",
            url = "https://bellard.org/quickjs/",
            usage = "源插件的 JavaScript 引擎",
        ),
        ThirdPartyComponent(
            name = "cashapp quickjs-java",
            version = "0.9.2",
            license = "Apache-2.0",
            url = "https://github.com/cashapp/quickjs-java",
            usage = "QuickJS 的 Android 绑定与原生库",
        ),
    )

    /** 本项目对移植代码的改动说明（Apache-2.0 §4(b) 要求） */
    const val MODIFICATION_NOTE: String =
        "Lyrico 代码已按本项目适配：NDK/CMake 版本、ABI 范围、移除 Parcelable、" +
            "构建脚本去掉二次裁剪、业务层改为 Hilt 注入与独立数据表。"

    const val NOTICE_LOCATION: String =
        "完整许可文本随安装包内的 lyrico-audiotag/NOTICE.md 提供。"
}
