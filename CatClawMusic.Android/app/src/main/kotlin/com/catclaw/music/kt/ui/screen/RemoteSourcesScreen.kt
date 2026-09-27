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
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
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
import com.catclaw.music.kt.core.model.ConnectionProfile
import com.catclaw.music.kt.data.db.toSong
import com.catclaw.music.kt.data.download.DownloadState
import com.catclaw.music.kt.core.model.ProtocolType
import com.catclaw.music.kt.ui.remote.RemoteViewModel
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette

/** 发现 tab：远程音源配置 + 导入 + 直接播（Subsonic/Navidrome 已通，WebDAV/SMB 待移植） */
@Composable
fun RemoteSourcesScreen(onBack: () -> Unit, viewModel: RemoteViewModel = hiltViewModel()) {
    val palette = LocalCatClawPalette.current
    val profiles by viewModel.profiles.collectAsStateWithLifecycle()
    val songs by viewModel.remoteSongs.collectAsStateWithLifecycle()
    val busy by viewModel.busy.collectAsStateWithLifecycle()
    val message by viewModel.message.collectAsStateWithLifecycle()
    val downloadTasks by viewModel.downloadTasks.collectAsStateWithLifecycle()
    val cachedIds by viewModel.cachedSongIds.collectAsStateWithLifecycle()

    val primary = Color(palette.textPrimary)
    val secondary = Color(palette.textSecondary)

    var name by rememberSaveable { mutableStateOf("") }
    var host by rememberSaveable { mutableStateOf("") }
    var port by rememberSaveable { mutableStateOf("4535") }
    var user by rememberSaveable { mutableStateOf("") }
    var pass by rememberSaveable { mutableStateOf("") }
    var protocol by rememberSaveable { mutableStateOf(ProtocolType.Navidrome.name) }
    var share by rememberSaveable { mutableStateOf("") }
    var domain by rememberSaveable { mutableStateOf("") }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 20.dp),
    ) {
        Spacer(Modifier.height(16.dp))
        Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            Text(
                text = stringResource(R.string.GoBack),
                style = MaterialTheme.typography.bodyMedium,
                color = secondary,
                modifier = Modifier.clickable(onClick = onBack).padding(vertical = 8.dp, horizontal = 2.dp),
            )
            Spacer(Modifier.width(14.dp))
            Text(
                text = stringResource(R.string.RemoteMusic),
                style = MaterialTheme.typography.titleLarge,
                color = primary,
                modifier = Modifier.weight(1f),
            )
        }
        Text(
            text = stringResource(R.string.RemoteMusicSubtitle),
            style = MaterialTheme.typography.bodySmall,
            color = secondary,
        )
        Spacer(Modifier.height(14.dp))

        Text(stringResource(R.string.ConfiguredConnections), style = MaterialTheme.typography.titleSmall, color = Color(palette.primaryDark))
        Spacer(Modifier.height(6.dp))
        if (profiles.isEmpty()) {
            Text(stringResource(R.string.NoConnectionsHint), color = secondary)
        }
        profiles.forEach { profile ->
            Row(modifier = Modifier.fillMaxWidth().padding(vertical = 6.dp), verticalAlignment = Alignment.CenterVertically) {
                Column(modifier = Modifier.weight(1f)) {
                    Text(profile.name, style = MaterialTheme.typography.bodyLarge, color = primary, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    Text(
                        text = "${profile.origin} · ${profile.protocol.name}",
                        style = MaterialTheme.typography.labelSmall,
                        color = secondary,
                    )
                }
                TextButton(onClick = { viewModel.test(profile) }) {
                    Text(stringResource(R.string.Test), color = secondary)
                }
                TextButton(onClick = { viewModel.import(profile) }, enabled = !busy) {
                    Text(stringResource(R.string.remote_import), color = Color(palette.primary))
                }
                TextButton(onClick = { viewModel.delete(profile.id) }) {
                    Text(stringResource(R.string.Delete), color = secondary)
                }
            }
        }

        message?.let {
            Text(it, style = MaterialTheme.typography.labelSmall, color = Color(palette.primaryDark))
        }
        Spacer(Modifier.height(14.dp))

        Text(stringResource(R.string.remote_add_server), style = MaterialTheme.typography.titleSmall, color = Color(palette.primaryDark))
        Spacer(Modifier.height(6.dp))
        OutlinedTextField(
            value = name,
            onValueChange = { name = it },
            label = { Text(stringResource(R.string.Name)) },
            singleLine = true,
            modifier = Modifier.fillMaxWidth(),
        )
        Spacer(Modifier.height(6.dp))
        Row {
            OutlinedTextField(
                value = host,
                onValueChange = { host = it },
                label = { Text(stringResource(R.string.HostAddress)) },
                singleLine = true,
                modifier = Modifier.weight(1.6f),
            )
            Spacer(Modifier.width(8.dp))
            OutlinedTextField(
                value = port,
                onValueChange = { port = it },
                label = { Text(stringResource(R.string.Port)) },
                singleLine = true,
                modifier = Modifier.weight(1f),
            )
        }
        Spacer(Modifier.height(6.dp))
        Row {
            OutlinedTextField(
                value = user,
                onValueChange = { user = it },
                label = { Text(stringResource(R.string.Username)) },
                singleLine = true,
                modifier = Modifier.weight(1f),
            )
            Spacer(Modifier.width(8.dp))
            OutlinedTextField(
                value = pass,
                onValueChange = { pass = it },
                label = { Text(stringResource(R.string.Password)) },
                singleLine = true,
                modifier = Modifier.weight(1f),
            )
        }
        Spacer(Modifier.height(8.dp))
        Row(horizontalArrangement = Arrangement.spacedBy(18.dp)) {
            ProtocolType.entries.forEach { option ->
                Text(
                    text = option.name,
                    style = MaterialTheme.typography.labelLarge,
                    color = if (option.name == protocol) Color(palette.primary) else secondary,
                    modifier = Modifier
                        .clickable { protocol = option.name }
                        .padding(vertical = 6.dp),
                )
            }
        }
        if (protocol == ProtocolType.SMB.name) {
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                OutlinedTextField(
                    value = share,
                    onValueChange = { share = it },
                    label = { Text(stringResource(R.string.SmbShareName)) },
                    singleLine = true,
                    modifier = Modifier.weight(1f),
                )
                OutlinedTextField(
                    value = domain,
                    onValueChange = { domain = it },
                    label = { Text(stringResource(R.string.SmbDomain)) },
                    singleLine = true,
                    modifier = Modifier.weight(1f),
                )
            }
            Spacer(Modifier.height(8.dp))
        }
        Spacer(Modifier.height(8.dp))
        Button(
            onClick = {
                val chosen = ProtocolType.valueOf(protocol)
                viewModel.save(
                    ConnectionProfile(
                        name = name.ifBlank { host },
                        host = host.trim(),
                        port = port.trim().toIntOrNull() ?: if (chosen == ProtocolType.SMB) 445 else 4535,
                        userName = user.trim(),
                        password = pass,
                        protocol = chosen,
                        shareName = share.trim().ifEmpty { null },
                        domainName = domain.trim().ifEmpty { null },
                    ),
                )
                name = ""
                host = ""
                user = ""
                pass = ""
                share = ""
                domain = ""
            },
        ) {
            Text(stringResource(R.string.Save))
        }

        Spacer(Modifier.height(18.dp))
        Text(
            text = stringResource(R.string.RemoteMusic) + " · " + stringResource(R.string.playlist_count, songs.size),
            style = MaterialTheme.typography.titleSmall,
            color = Color(palette.primaryDark),
        )
        Spacer(Modifier.height(4.dp))
        songs.forEachIndexed { index, song ->
            val task = downloadTasks.firstOrNull { it.songId == song.id }
            val cached = song.id in cachedIds
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(vertical = 6.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Column(
                    modifier = Modifier
                        .weight(1f)
                        .clickable { viewModel.playAt(index) },
                ) {
                    Text(song.title, style = MaterialTheme.typography.bodyMedium, color = primary, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    Text(
                        text = when {
                            task != null && task.state == DownloadState.Running ->
                                "${task.downloaded}/${task.total}"
                            task != null && task.state == DownloadState.Failed -> stringResource(R.string.download_failed)
                            cached -> stringResource(R.string.download_done)
                            else -> listOf(song.artist, song.album).filter { it.isNotBlank() }.joinToString(" · ")
                        },
                        style = MaterialTheme.typography.labelSmall,
                        color = secondary,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                }
                if (task != null && task.state == DownloadState.Running) {
                    TextButton(onClick = { viewModel.cancelDownload(song.id) }) {
                        Text(stringResource(R.string.download_cancel), color = secondary)
                    }
                } else if (!cached) {
                    TextButton(onClick = { viewModel.download(song.toSong()) }) {
                        Text(stringResource(R.string.download), color = Color(palette.primary))
                    }
                }
            }
        }
        Spacer(Modifier.height(24.dp))
    }
}
