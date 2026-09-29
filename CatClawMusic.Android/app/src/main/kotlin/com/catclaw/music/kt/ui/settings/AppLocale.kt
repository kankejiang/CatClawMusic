package com.catclaw.music.kt.ui.settings

import android.content.Context
import com.catclaw.music.kt.core.diagnostic.AppLog

/**
 * 应用内语言切换。表里的 tag 与 res 下的语言目录一一对应 —— 由 LocaleParityTest 守住，
 * 避免这里出现「有名字没资源」的语言（选了只能露出中性中文）。
 *
 * 唯一事实来源是自己这份 SharedPreferences：真机点选 日本語 后 `AppCompatDelegate.getApplicationLocales()`
 * 读回来仍是空（本机实测，MIUI 连 `locale_manager` 服务都没有：`cmd locale_manager` → Can't find service），
 * 而一旦两边都存，用户从系统设置改语言就会被这里盖掉 —— 所以只留一条路。
 */
object AppLocale {

    const val SYSTEM = ""
    private const val TAG = "CatClawLocale"
    private const val PREFS = "app_locale"
    private const val KEY = "tag"

    /** tag → 该语言自称（母语者最认得自己的写法） */
    val SUPPORTED: List<Pair<String, String>> = listOf(
        "zh-CN" to "简体中文",
        "zh-TW" to "繁體中文",
        "en" to "English",
        "ja" to "日本語",
        "ko" to "한국어",
        "fr" to "Français",
        "de" to "Deutsch",
        "es" to "Español",
        "pt" to "Português",
        "ru" to "Русский",
        "it" to "Italiano",
        "ar" to "العربية",
        "hi" to "हिन्दी",
        "bn" to "বাংলা",
        "mr" to "मराठी",
        "ta" to "தமிழ்",
        "te" to "తెలుగు",
        "gu" to "ગુજરાતી",
        "kn" to "ಕನ್ನಡ",
        "ml" to "മലയാളം",
        "th" to "ไทย",
        "vi" to "Tiếng Việt",
        "id" to "Bahasa Indonesia",
        "ms" to "Bahasa Melayu",
        "fil" to "Filipino",
        "nl" to "Nederlands",
        "pl" to "Polski",
        "tr" to "Türkçe",
        "ro" to "Română",
        "hu" to "Magyar",
        "el" to "Ελληνικά",
        "cs" to "Čeština",
        "sk" to "Slovenčina",
        "sl" to "Slovenščina",
        "hr" to "Hrvatski",
        "sr" to "Српски",
        "bg" to "Български",
        "da" to "Dansk",
        "fi" to "Suomi",
        "nb" to "Norsk bokmål",
        "sv" to "Svenska",
        "is" to "Íslenska",
        "et" to "Eesti",
        "lv" to "Latviešu",
        "lt" to "Lietuvių",
        "uk" to "Українська",
        "he" to "עברית",
        "fa" to "فارسی",
        "ur" to "اردو",
        "sw" to "Kiswahili",
        "ca" to "Català",
    )

    /** 当前生效的语言；空串 = 跟随系统 */
    fun current(context: Context): String =
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).getString(KEY, SYSTEM) ?: SYSTEM

    fun displayName(tag: String): String =
        SUPPORTED.firstOrNull { it.first == tag }?.second ?: tag

    fun apply(context: Context, tag: String) {
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit().putString(KEY, tag).apply()
        AppLog.i(TAG, "applied tag=$tag stored=${current(context)}")
    }
}
