package com.catclaw.music.kt.ui.screen

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.catclaw.music.kt.R
import com.catclaw.music.kt.ui.playlist.PlaylistViewModel
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette

/** 歌单 tab：列表 ↔ 详情两层，建/删歌单、把当前曲加进来、整单播放 */
@Composable
fun PlaylistScreen(viewModel: PlaylistViewModel = hiltViewModel()) {
    val palette = LocalCatClawPalette.current
    val playlists by viewModel.playlists.collectAsStateWithLifecycle()
    val items by viewModel.items.collectAsStateWithLifecycle()
    val openId by viewModel.openId.collectAsStateWithLifecycle()
    var draft by rememberSaveable { mutableStateOf("") }

    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)

    Column(modifier = Modifier.fillMaxSize().padding(horizontal = 20.dp)) {
        Spacer(Modifier.height(16.dp))
        val open = openId?.let { id -> playlists.firstOrNull { it.id == id } }
        Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            Text(
                text = open?.name ?: stringResource(R.string.MyPlaylists),
                style = MaterialTheme.typography.titleLarge,
                color = primary,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.weight(1f),
            )
            if (open != null) {
                Text(
                    text = stringResource(R.string.GoBack),
                    style = MaterialTheme.typography.bodyMedium,
                    color = secondary,
                    modifier = Modifier.clickable { viewModel.close() }.padding(8.dp),
                )
            }
        }
        if (open == null) {
            Text(
                text = stringResource(R.string.PlaylistSubtitle),
                style = MaterialTheme.typography.bodySmall,
                color = secondary,
            )
        }
        Spacer(Modifier.height(12.dp))

        if (open == null) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                OutlinedTextField(
                    value = draft,
                    onValueChange = { draft = it },
                    placeholder = { Text(stringResource(R.string.playlist_name_hint), color = secondary) },
                    singleLine = true,
                    modifier = Modifier.weight(1f),
                )
                Spacer(Modifier.width(10.dp))
                Button(onClick = { viewModel.create(draft); draft = "" }) {
                    Text(stringResource(R.string.playlist_new))
                }
            }
            Spacer(Modifier.height(10.dp))
            if (playlists.isEmpty()) {
                Text(stringResource(R.string.playlist_none), color = secondary)
            }
            LazyColumn(
                modifier = Modifier.fillMaxSize(),
                contentPadding = androidx.compose.foundation.layout.PaddingValues(bottom = 80.dp),
            ) {
                items(playlists, key = { it.id }) { playlist ->
                    Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .clickable { viewModel.open(playlist.id) }
                            .padding(vertical = 12.dp),
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        Text(
                            text = playlist.name,
                            style = MaterialTheme.typography.bodyLarge,
                            color = primary,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                            modifier = Modifier.weight(1f),
                        )
                        Text(
                            text = stringResource(R.string.playlist_count, playlist.songCount),
                            style = MaterialTheme.typography.labelSmall,
                            color = secondary,
                        )
                        if (!playlist.isSystem) {
                            TextButton(onClick = { viewModel.delete(playlist.id) }) {
                                Text(stringResource(R.string.Delete), color = secondary)
                            }
                        }
                    }
                }
            }
        } else {
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Button(onClick = { viewModel.playAll() }) {
                    Text(stringResource(R.string.playlist_play_all))
                }
                TextButton(onClick = { viewModel.addCurrentSong() }) {
                    Text(stringResource(R.string.playlist_add_current), color = secondary)
                }
            }
            Spacer(Modifier.height(8.dp))
            if (items.isEmpty()) {
                Text(stringResource(R.string.playlist_empty), color = secondary)
            }
            LazyColumn(
                modifier = Modifier.fillMaxSize(),
                contentPadding = androidx.compose.foundation.layout.PaddingValues(bottom = 80.dp),
            ) {
                items(items, key = { it.id }) { song ->
                    Column(
                        modifier = Modifier
                            .fillMaxWidth()
                            .clickable { viewModel.playAt(items.indexOf(song)) }
                            .padding(vertical = 10.dp),
                    ) {
                        Text(
                            text = song.title,
                            style = MaterialTheme.typography.bodyMedium,
                            color = primary,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                        )
                        Text(
                            text = song.artist.orEmpty(),
                            style = MaterialTheme.typography.labelSmall,
                            color = secondary,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                        )
                    }
                }
            }
        }
    }
}
