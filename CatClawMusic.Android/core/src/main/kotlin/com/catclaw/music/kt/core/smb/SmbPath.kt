package com.catclaw.music.kt.core.smb

/**
 * SMB 共享内路径的规范化与层级运算。
 *
 * 放在 `:core` 而不是 ViewModel 里：这些判断用在"浏览目录"的每一层导航上，
 * 写错的表现是「上一级点不动」「根目录还显示上一级按钮」「选中的路径多一个反斜杠」，
 * 都是要靠真机逐层点才发现的。抽出来才有测试。
 *
 * ## 为什么单独一套、不复用 WebDAV 的
 *
 * WebDAV 路径是 URL 的 `/a/b`，SMB 是共享内相对的 `\a\b`。两者都是"层级路径"，
 * 但分隔符相反、且 `\` 不是 `/` 的子串 —— 用一套字符串函数去处理另一种，
 * 在 `trimEnd('/')`、`substringBeforeLast('/')` 这种地方会静默返回原串。
 */
object SmbPath {

    /** 共享根目录。SMB 的写法是单个反斜杠。 */
    const val ROOT = "\\"

    /**
     * 收成规范形式：统一反斜杠、去掉首尾多余分隔符、空串归为 [ROOT]。
     *
     * 用户可能从别处粘来 `music/专辑`、`/专辑`、`\\专辑\\`，这里都收成 `\专辑`，
     * 免得同一个目录在不同入口算成不同字符串（会影响"当前是不是根目录"的判断）。
     */
    fun normalize(path: String): String {
        val cleaned = path.replace('/', '\\').trim('\\')
        return if (cleaned.isEmpty()) ROOT else "\\$cleaned"
    }

    /**
     * 是否在共享根目录。
     *
     * 判据要把 `/`、空串都算上：从别处进来的路径可能是 `/`，而 [normalize] 之后是 `\`。
     * 只比 `== "\\"` 会漏掉前一种，表现就是根目录还亮着"上一级"。
     */
    fun isRoot(path: String): Boolean = normalize(path) == ROOT

    /**
     * 上一级目录；已经在根目录时返回 [ROOT]。
     *
     * 先 [normalize] 再切，`\a\b` → `\a`，`\a` → `\`。
     */
    fun parent(path: String): String {
        val normalized = normalize(path)
        if (normalized == ROOT) return ROOT
        val trimmed = normalized.trim('\\')
        val parent = trimmed.substringBeforeLast('\\', "")
        return if (parent.isEmpty()) ROOT else "\\$parent"
    }

    /** 当前层的显示名：`\a\b` → `b`；根目录时返回 [ROOT] */
    fun name(path: String): String {
        val normalized = normalize(path)
        return if (normalized == ROOT) ROOT else normalized.trim('\\').substringAfterLast('\\')
    }
}
