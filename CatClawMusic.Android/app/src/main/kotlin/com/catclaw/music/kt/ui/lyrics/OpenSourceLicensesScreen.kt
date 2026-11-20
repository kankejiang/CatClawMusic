package com.catclaw.music.kt.ui.lyrics

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.catclaw.music.kt.R
import com.catclaw.music.kt.lyrico.ui.ThirdPartyNotices
import com.catclaw.music.kt.ui.theme.BackTile
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.glassCard
import com.catclaw.music.kt.ui.theme.highlightOnGlassCard

/**
 * 开源许可声明页。
 *
 * 这是许可合规的落地点：用户能在应用内看到用了哪些第三方组件、各自的许可与来源。
 * 完整文本在安装包内的 NOTICE 文件里（见 [ThirdPartyNotices.NOTICE_LOCATION]）。
 */
@Composable
fun OpenSourceLicensesScreen(onBack: () -> Unit) {
    val palette = LocalCatClawPalette.current
    val ink = Color(palette.textPrimary)
    val dim = Color(palette.textSecondary)
    val accent = Color(highlightOnGlassCard(palette))

    Column(
        modifier = Modifier
            .fillMaxSize()
            .statusBarsPadding()
            .padding(horizontal = 16.dp),
    ) {
        Spacer(Modifier.height(12.dp))
        Row(verticalAlignment = Alignment.CenterVertically) {
            BackTile(onBack = onBack, description = stringResource(R.string.GoBack))
            Spacer(Modifier.width(12.dp))
            Text(
                text = stringResource(R.string.licenses_title),
                fontSize = 20.sp,
                fontWeight = FontWeight.Bold,
                color = ink,
            )
        }
        Spacer(Modifier.height(10.dp))

        Column(
            modifier = Modifier
                .fillMaxSize()
                .verticalScroll(rememberScrollState()),
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            Text(
                text = stringResource(R.string.licenses_intro),
                fontSize = 11.sp,
                color = dim,
            )
            ThirdPartyNotices.components.forEach { component ->
                Column(
                    modifier = Modifier
                        .fillMaxWidth()
                        .glassCard(palette = palette, corner = 14)
                        .padding(horizontal = 12.dp, vertical = 10.dp),
                ) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text(
                            text = component.name,
                            fontSize = 14.sp,
                            fontWeight = FontWeight.SemiBold,
                            color = ink,
                            modifier = Modifier.weight(1f),
                        )
                        Text(text = component.license, fontSize = 11.sp, color = accent)
                    }
                    Text(
                        text = buildString {
                            append("v")
                            append(component.version)
                            if (component.modified) {
                                append("  ·  ")
                                append(stringResource(R.string.licenses_modified))
                            }
                        },
                        fontSize = 10.sp,
                        color = dim,
                    )
                    Text(text = component.usage, fontSize = 11.sp, color = dim)
                    Text(
                        text = component.url,
                        fontSize = 10.sp,
                        color = accent,
                        maxLines = 1,
                    )
                }
            }

            // 修改说明与全文位置：Apache-2.0 §4(b) 明确要求标注改动
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .clip(RoundedCornerShape(14.dp))
                    .background(ink.copy(alpha = 0.06f))
                    .padding(horizontal = 12.dp, vertical = 10.dp),
            ) {
                Text(
                    text = stringResource(R.string.licenses_modifications),
                    fontSize = 11.sp,
                    fontWeight = FontWeight.SemiBold,
                    color = ink,
                )
                Text(
                    text = ThirdPartyNotices.MODIFICATION_NOTE,
                    fontSize = 10.sp,
                    color = dim,
                )
                Spacer(Modifier.height(6.dp))
                Text(
                    text = ThirdPartyNotices.NOTICE_LOCATION,
                    fontSize = 10.sp,
                    color = dim,
                )
            }
            Spacer(Modifier.height(20.dp))
        }
    }
}
