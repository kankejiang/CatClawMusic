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
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.catclaw.music.kt.R
import com.catclaw.music.kt.lyrico.ui.LyricsMatchTarget
import com.catclaw.music.kt.lyrico.ui.MetadataEditViewModel
import com.catclaw.music.kt.ui.theme.BackTile
import com.catclaw.music.kt.ui.theme.GlassSearchField
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette
import com.catclaw.music.kt.ui.theme.PillLabel
import com.catclaw.music.kt.ui.theme.highlightOnGlassCard

/**
 * 元数据编辑页：直接改音频文件里的文本标签，保存后同步曲库。
 *
 * 入口在歌曲详情页。远程曲目/只读文件会明确提示不可写，而不是让用户点了没反应。
 */
@Composable
fun MetadataEditScreen(
    target: LyricsMatchTarget,
    onBack: () -> Unit,
    // 详情页已有的库内属性：同步时用来保留未编辑字段
    currentGenre: String? = null,
    currentYear: Int = 0,
    currentTrack: Int = 0,
    viewModel: MetadataEditViewModel = hiltViewModel(),
) {
    val palette = LocalCatClawPalette.current
    val state by viewModel.state.collectAsStateWithLifecycle()
    val ink = Color(palette.textPrimary)
    val dim = Color(palette.textSecondary)
    val accent = Color(highlightOnGlassCard(palette))

    LaunchedEffect(target.songId) {
        viewModel.prepare(target)
        viewModel.updateCurrentAttributes(currentGenre, currentYear, currentTrack)
    }

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
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = stringResource(R.string.tag_edit_title),
                    fontSize = 20.sp,
                    fontWeight = FontWeight.Bold,
                    color = ink,
                )
                Text(text = target.title, fontSize = 12.sp, color = dim, maxLines = 1)
            }
            if (state.dirty) {
                PillLabel(
                    text = if (state.saving) {
                        stringResource(R.string.tag_edit_saving)
                    } else {
                        stringResource(R.string.tag_edit_save)
                    },
                    onClick = viewModel::save,
                    filled = true,
                    enabled = !state.saving && state.supported,
                )
            }
        }

        state.message?.let { msg ->
            Spacer(Modifier.height(8.dp))
            Text(
                text = msg,
                fontSize = 12.sp,
                color = accent,
                modifier = Modifier
                    .fillMaxWidth()
                    .clip(RoundedCornerShape(12.dp))
                    .background(ink.copy(alpha = 0.08f))
                    .padding(horizontal = 12.dp, vertical = 8.dp),
            )
            LaunchedEffect(msg) {
                kotlinx.coroutines.delay(2600)
                viewModel.consumeMessage()
            }
        }

        Spacer(Modifier.height(10.dp))
        if (!state.supported) {
            Text(
                text = stringResource(R.string.tag_edit_unsupported),
                fontSize = 13.sp,
                color = dim,
                modifier = Modifier.fillMaxWidth().padding(vertical = 24.dp),
            )
            return@Column
        }
        if (state.loading) {
            Text(text = stringResource(R.string.tag_edit_loading), fontSize = 13.sp, color = dim)
            return@Column
        }

        val edited = state.edited
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .weight(1f)
                .verticalScroll(rememberScrollState()),
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            EditField(
                label = stringResource(R.string.tag_edit_field_title),
                value = edited.title,
                onChange = { v -> viewModel.update { it.copy(title = v) } },
            )
            EditField(
                label = stringResource(R.string.tag_edit_field_artist),
                value = edited.artist,
                onChange = { v -> viewModel.update { it.copy(artist = v) } },
            )
            EditField(
                label = stringResource(R.string.tag_edit_field_album),
                value = edited.album,
                onChange = { v -> viewModel.update { it.copy(album = v) } },
            )
            EditField(
                label = stringResource(R.string.tag_edit_field_album_artist),
                value = edited.albumArtist,
                onChange = { v -> viewModel.update { it.copy(albumArtist = v) } },
            )
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                Box3({ Modifier.weight(1f) }) {
                    EditField(
                        label = stringResource(R.string.tag_edit_field_track),
                        value = edited.trackNumber,
                        onChange = { v -> viewModel.update { it.copy(trackNumber = v) } },
                    )
                }
                Box3({ Modifier.weight(1f) }) {
                    EditField(
                        label = stringResource(R.string.tag_edit_field_disc),
                        value = edited.discNumber,
                        onChange = { v -> viewModel.update { it.copy(discNumber = v) } },
                    )
                }
            }
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                Box3({ Modifier.weight(1f) }) {
                    EditField(
                        label = stringResource(R.string.tag_edit_field_genre),
                        value = edited.genre,
                        onChange = { v -> viewModel.update { it.copy(genre = v) } },
                    )
                }
                Box3({ Modifier.weight(1f) }) {
                    EditField(
                        label = stringResource(R.string.tag_edit_field_date),
                        value = edited.date,
                        onChange = { v -> viewModel.update { it.copy(date = v) } },
                    )
                }
            }
            EditField(
                label = stringResource(R.string.tag_edit_field_composer),
                value = edited.composer,
                onChange = { v -> viewModel.update { it.copy(composer = v) } },
            )
            EditField(
                label = stringResource(R.string.tag_edit_field_lyricist),
                value = edited.lyricist,
                onChange = { v -> viewModel.update { it.copy(lyricist = v) } },
            )
            EditField(
                label = stringResource(R.string.tag_edit_field_comment),
                value = edited.comment,
                onChange = { v -> viewModel.update { it.copy(comment = v) } },
            )
            Text(
                text = stringResource(R.string.tag_edit_hint),
                fontSize = 11.sp,
                color = dim,
                modifier = Modifier.padding(top = 4.dp, bottom = 12.dp),
            )
        }
    }
}

/** 局部布局小工具：让一行里的两个字段各自拿到 weight */
@Composable
private fun Box3(modifier: () -> Modifier, content: @Composable () -> Unit) {
    androidx.compose.foundation.layout.Box(modifier = modifier().fillMaxWidth()) { content() }
}

@Composable
private fun EditField(label: String, value: String, onChange: (String) -> Unit) {
    val palette = LocalCatClawPalette.current
    Column(modifier = Modifier.fillMaxWidth()) {
        Text(
            text = label,
            fontSize = 11.sp,
            color = Color(palette.textSecondary),
            modifier = Modifier.padding(start = 4.dp, bottom = 4.dp),
        )
        GlassSearchField(
            query = value,
            onChange = onChange,
            placeholder = label,
            modifier = Modifier.fillMaxWidth(),
        )
    }
}
