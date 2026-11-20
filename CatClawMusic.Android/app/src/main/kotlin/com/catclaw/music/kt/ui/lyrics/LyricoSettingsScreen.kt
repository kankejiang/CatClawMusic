package com.catclaw.music.kt.ui.lyrics

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
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.catclaw.music.kt.R
import com.catclaw.music.kt.lyrico.settings.LyricoSettings
import com.catclaw.music.kt.lyrico.ui.LyricoSettingsViewModel
import com.catclaw.music.kt.ui.theme.BackTile
import com.catclaw.music.kt.ui.theme.GlassChip
import com.catclaw.music.kt.ui.theme.GlassSearchField
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.PillLabel
import com.catclaw.music.kt.ui.theme.glassCard

/**
 * 歌词设置：字段可见性、清理规则、艺人拆分、字符映射。
 *
 * 这些配置直接影响「写入音频文件的歌词长什么样」，所以集中一处并可一键恢复默认。
 */
@Composable
fun LyricoSettingsScreen(
    onBack: () -> Unit,
    viewModel: LyricoSettingsViewModel = hiltViewModel(),
) {
    val palette = LocalCatClawPalette.current
    val settings by viewModel.state.collectAsStateWithLifecycle()
    val ink = Color(palette.textPrimary)
    val dim = Color(palette.textSecondary)

    Column(
        modifier = Modifier
            .fillMaxSize()
            .statusBarsPadding()
            // 覆盖层底部压着「迷你播放栏 + 底部导航」，不预留会被挡住
            // （真机验收时点内容实际点到了播放栏上）
            .padding(bottom = 120.dp)

            .padding(horizontal = 16.dp),
    ) {
        Spacer(Modifier.height(12.dp))
        Row(verticalAlignment = Alignment.CenterVertically) {
            BackTile(onBack = onBack, description = stringResource(R.string.GoBack))
            Spacer(Modifier.width(12.dp))
            Text(
                text = stringResource(R.string.lyrico_settings_title),
                fontSize = 20.sp,
                fontWeight = FontWeight.Bold,
                color = ink,
                modifier = Modifier.weight(1f),
            )
            PillLabel(
                text = stringResource(R.string.lyrico_settings_reset),
                onClick = viewModel::reset,
                filled = false,
            )
        }
        Spacer(Modifier.height(10.dp))

        Column(
            modifier = Modifier
                .fillMaxSize()
                .verticalScroll(rememberScrollState()),
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            // ── 清理规则 ──
            SettingCard(stringResource(R.string.lyrico_settings_cleanup)) {
                ToggleRow(
                    label = stringResource(R.string.lyrico_setting_trim_lines),
                    desc = stringResource(R.string.lyrico_setting_trim_lines_desc),
                    checked = settings.cleanup.trimLines,
                    onChange = viewModel::setTrimLines,
                )
                ToggleRow(
                    label = stringResource(R.string.lyrico_setting_remove_empty),
                    desc = stringResource(R.string.lyrico_setting_remove_empty_desc),
                    checked = settings.cleanup.removeEmptyLines,
                    onChange = viewModel::setRemoveEmptyLines,
                )
                ToggleRow(
                    label = stringResource(R.string.lyrico_setting_remove_credits),
                    desc = stringResource(R.string.lyrico_setting_remove_credits_desc),
                    checked = settings.cleanup.removeCreditLines,
                    onChange = viewModel::setRemoveCreditLines,
                )
                ToggleRow(
                    label = stringResource(R.string.lyrico_setting_remove_tags),
                    desc = stringResource(R.string.lyrico_setting_remove_tags_desc),
                    checked = settings.cleanup.removeMetadataTags,
                    onChange = viewModel::setRemoveMetadataTags,
                )
            }

            // ── 字段可见性 ──
            SettingCard(stringResource(R.string.lyrico_settings_fields)) {
                Text(
                    text = stringResource(R.string.lyrico_settings_fields_hint),
                    fontSize = 11.sp,
                    color = dim,
                )
                FieldToggle("albumArtist", stringResource(R.string.tag_edit_field_album_artist), settings, viewModel)
                FieldToggle("genre", stringResource(R.string.tag_edit_field_genre), settings, viewModel)
                FieldToggle("date", stringResource(R.string.tag_edit_field_date), settings, viewModel)
                FieldToggle("trackNumber", stringResource(R.string.tag_edit_field_track), settings, viewModel)
                FieldToggle("discNumber", stringResource(R.string.tag_edit_field_disc), settings, viewModel)
                FieldToggle("composer", stringResource(R.string.tag_edit_field_composer), settings, viewModel)
                FieldToggle("lyricist", stringResource(R.string.tag_edit_field_lyricist), settings, viewModel)
                FieldToggle("comment", stringResource(R.string.tag_edit_field_comment), settings, viewModel)
            }

            // ── 艺人拆分 ──
            SettingCard(stringResource(R.string.lyrico_settings_artist_split)) {
                ToggleRow(
                    label = stringResource(R.string.lyrico_setting_split_enable),
                    desc = stringResource(R.string.lyrico_setting_split_desc),
                    checked = settings.artistSplit.enabled,
                    onChange = viewModel::setArtistSplitEnabled,
                )
                if (settings.artistSplit.enabled) {
                    Text(
                        text = stringResource(R.string.lyrico_setting_split_separators),
                        fontSize = 11.sp,
                        color = dim,
                    )
                    Spacer(Modifier.height(4.dp))
                    GlassSearchField(
                        query = settings.artistSplit.joinSeparator,
                        onChange = viewModel::setArtistJoinSeparator,
                        placeholder = "/",
                        modifier = Modifier.fillMaxWidth(),
                        compact = true,
                    )
                }
            }

            // ── 字符映射 ──
            CharacterMappingCard(settings, viewModel)

            Spacer(Modifier.height(20.dp))
        }
    }
}

@Composable
private fun SettingCard(title: String, content: @Composable () -> Unit) {
    val palette = LocalCatClawPalette.current
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .glassCard(palette = palette, corner = 18)
            .padding(horizontal = 14.dp, vertical = 12.dp),
        verticalArrangement = Arrangement.spacedBy(6.dp),
    ) {
        Text(
            text = title,
            fontSize = 14.sp,
            fontWeight = FontWeight.Bold,
            color = Color(palette.textPrimary),
        )
        content()
    }
}

@Composable
private fun ToggleRow(
    label: String,
    desc: String,
    checked: Boolean,
    onChange: (Boolean) -> Unit,
) {
    val palette = LocalCatClawPalette.current
    Row(verticalAlignment = Alignment.CenterVertically) {
        Column(modifier = Modifier.weight(1f)) {
            Text(text = label, fontSize = 13.sp, color = Color(palette.textPrimary))
            Text(text = desc, fontSize = 10.sp, color = Color(palette.textSecondary))
        }
        GlassChip(
            label = if (checked) {
                stringResource(R.string.source_plugin_enabled)
            } else {
                stringResource(R.string.source_plugin_disabled)
            },
            selected = checked,
            onClick = { onChange(!checked) },
            compact = true,
        )
    }
}

@Composable
private fun FieldToggle(
    key: String,
    label: String,
    settings: LyricoSettings,
    viewModel: LyricoSettingsViewModel,
) {
    ToggleRow(
        label = label,
        desc = "",
        checked = settings.editFields.isVisible(key),
        onChange = { viewModel.toggleEditField(key, it) },
    )
}

@Composable
private fun CharacterMappingCard(
    settings: LyricoSettings,
    viewModel: LyricoSettingsViewModel,
) {
    val palette = LocalCatClawPalette.current
    val dim = Color(palette.textSecondary)
    var text by remember(settings.characterMapping.pairs) {
        mutableStateOf(viewModel.characterMappingText(settings))
    }
    SettingCard(stringResource(R.string.lyrico_settings_char_map)) {
        ToggleRow(
            label = stringResource(R.string.lyrico_setting_char_enable),
            desc = stringResource(R.string.lyrico_setting_char_desc),
            checked = settings.characterMapping.enabled,
            onChange = viewModel::setCharacterMappingEnabled,
        )
        if (settings.characterMapping.enabled) {
            Text(
                text = stringResource(R.string.lyrico_setting_char_format),
                fontSize = 10.sp,
                color = dim,
            )
            GlassSearchField(
                query = text,
                onChange = {
                    text = it
                    viewModel.setCharacterMappingPairs(it)
                },
                placeholder = "臺=台",
                modifier = Modifier.fillMaxWidth(),
            )
        }
    }
}
