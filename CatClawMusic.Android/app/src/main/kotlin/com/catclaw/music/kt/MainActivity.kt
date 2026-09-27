package com.catclaw.music.kt

import android.content.Context
import android.content.res.Configuration
import android.os.Bundle
import android.os.LocaleList
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import com.catclaw.music.kt.ui.CatClawRoot
import com.catclaw.music.kt.ui.settings.AppLocale
import com.catclaw.music.kt.ui.theme.CatClawTheme
import dagger.hilt.android.AndroidEntryPoint

@AndroidEntryPoint
class MainActivity : ComponentActivity() {

    /**
     * 应用内语言的落地方式：自己按存的 tag 重建 base context 的 Configuration ——
     * 本 Activity 不是 AppCompatActivity（主题是框架 Theme.DeviceDefault），而 MIUI 这类定制 ROM
     * 甚至没有 locale_manager 服务，不能把生效与否押在框架那条路上。
     */
    override fun attachBaseContext(newBase: Context) {
        val tag = AppLocale.current(newBase)
        if (tag.isEmpty()) {
            super.attachBaseContext(newBase)
            return
        }
        val configuration = Configuration(newBase.resources.configuration)
        configuration.setLocales(LocaleList.forLanguageTags(tag))
        super.attachBaseContext(newBase.createConfigurationContext(configuration))
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent {
            CatClawTheme {
                CatClawRoot()
            }
        }
    }
}
