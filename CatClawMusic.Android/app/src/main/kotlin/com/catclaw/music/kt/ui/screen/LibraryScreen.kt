package com.catclaw.music.kt.ui.screen

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.RowScope
import com.catclaw.music.kt.core.theme.CatClawPalette
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.catclaw.music.kt.R
import com.catclaw.music.kt.ui.library.LibraryViewModel
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette

/**
 * 音乐库 —— 照 MAUI `LibraryPage.xaml` 做成**枢纽页**：Hero 卡 + 4 个统计格 + 资料库列表。
 * 之前这里的 歌曲/艺人/专辑/收藏 四个 tab 是我自己加的，MAUI 没有：统计格才是入口，
 * 点进去是独立列表页（对应 `AllSongsPage` / `ArtistsPage` / `AlbumsPage`）。
 *
 * 只放**取得到**的数据：小时数由本地曲库时长求和；MAUI 的「存储占用」「数据洞察环形图」
 * 需要文件大小与格式分布，本端没有这两个聚合查询，就不摆空壳（宁缺毋假）。
 */
enum class LibraryKind { Songs, Artists, Albums, Favorites }

@Composable
fun LibraryScreen(viewModel: LibraryViewModel = hiltViewModel()) {
    // 统计格 / 资料库行的下钻。MAUI 是 push 独立页（AllSongsPage 等），这里在页内切换：
    // 少一条路由与一次 VM 作用域切换，返回行为一致
    var opened by remember { mutableStateOf<LibraryKind?>(null) }
    opened?.let { kind ->
        LibraryListScreen(kind = kind, onBack = { opened = null }, viewModel = viewModel)
        return
    }
    val onOpenKind: (LibraryKind) -> Unit = { opened = it }
    val palette = LocalCatClawPalette.current
    val songs by viewModel.songs.collectAsStateWithLifecycle()
    val artists by viewModel.artists.collectAsStateWithLifecycle()
    val albums by viewModel.albums.collectAsStateWithLifecycle()
    val favorites by viewModel.favorites.collectAsStateWithLifecycle()
    val scanning by viewModel.scanning.collectAsStateWithLifecycle()
    val hours = songs.sumOf { it.durationSeconds.toLong() } / 3600.0

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(bottom = 132.dp),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth().padding(start = 20.dp, end = 20.dp, top = 12.dp, bottom = 4.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.SpaceBetween,
        ) {
            Text(
                text = stringResource(R.string.TabLibrary),
                style = MaterialTheme.typography.titleLarge,
                color = Color(palette.textPrimary),
            )
            Button(onClick = { viewModel.scan() }, enabled = !scanning) {
                Text(stringResource(if (scanning) R.string.library_scanning else R.string.ScanMusic))
            }
        }

        // Hero 卡：渐变底 + 4 个可点统计格（MAUI 的 HeroCard + st4）
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 18.dp, vertical = 6.dp)
                .clip(RoundedCornerShape(24.dp))
                .background(
                    Brush.linearGradient(
                        listOf(Color(palette.primary).copy(alpha = 0.40f), Color(0x40FFD9A8)),
                    ),
                )
                .padding(22.dp),
        ) {
            Column {
                Text(
                    text = stringResource(R.string.lib_my_library),
                    fontSize = 22.sp,
                    fontWeight = FontWeight.Bold,
                    color = Color(palette.textPrimary),
                )
                Text(
                    text = stringResource(R.string.lib_sources_count, 1),
                    fontSize = 12.5.sp,
                    color = Color(palette.textSecondary),
                    modifier = Modifier.padding(top = 4.dp),
                )
                Row(
                    modifier = Modifier.fillMaxWidth().padding(top = 18.dp),
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    StatTile(String.format("%,d", songs.size), stringResource(R.string.lib_stat_songs), palette) {
                        onOpenKind(LibraryKind.Songs)
                    }
                    StatTile(String.format("%,d", artists.size), stringResource(R.string.lib_stat_artists), palette) {
                        onOpenKind(LibraryKind.Artists)
                    }
                    StatTile(String.format("%,d", albums.size), stringResource(R.string.lib_stat_albums), palette) {
                        onOpenKind(LibraryKind.Albums)
                    }
                    StatTile(String.format("%.1f", hours), stringResource(R.string.lib_stat_hours), palette) {
                        onOpenKind(LibraryKind.Songs)
                    }
                }
            }
        }

        // 资料库卡：本端真实存在的两个来源（本地曲库 / 我喜欢的）
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 18.dp, vertical = 12.dp)
                .clip(RoundedCornerShape(22.dp))
                .background(Color(palette.textPrimary).copy(alpha = 0.10f))
                .padding(horizontal = 16.dp, vertical = 14.dp),
        ) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(
                    text = stringResource(R.string.lib_sources_title),
                    fontSize = 16.sp,
                    fontWeight = FontWeight.Bold,
                    color = Color(palette.textPrimary),
                )
                Text(
                    text = "2",
                    fontSize = 10.sp,
                    color = Color(palette.accent),
                    modifier = Modifier
                        .padding(start = 8.dp)
                        .clip(RoundedCornerShape(999.dp))
                        .background(Color(palette.textPrimary).copy(alpha = 0.08f))
                        .padding(horizontal = 8.dp, vertical = 2.dp),
                )
            }
            Spacer(Modifier.height(6.dp))
            SourceRow(R.drawable.ic_library, stringResource(R.string.lib_local_name), songs.size, palette) {
                onOpenKind(LibraryKind.Songs)
            }
            SourceRow(R.drawable.ic_favorite, stringResource(R.string.lib_favorites_name), favorites.size, palette) {
                onOpenKind(LibraryKind.Favorites)
            }
        }
    }
}

@Composable
private fun RowScope.StatTile(
    value: String,
    label: String,
    palette: CatClawPalette,
    onClick: () -> Unit,
) {
    Box(
        modifier = Modifier
            .weight(1f)
            .clip(RoundedCornerShape(16.dp))
            .background(Color(palette.textPrimary).copy(alpha = 0.20f))
            .clickable(onClick = onClick)
            .padding(horizontal = 8.dp, vertical = 10.dp),
    ) {
        Column {
            Text(value, fontSize = 18.sp, fontWeight = FontWeight.Bold, color = Color(palette.textPrimary))
            Text(label, fontSize = 11.sp, color = Color(palette.textSecondary), modifier = Modifier.padding(top = 2.dp))
        }
    }
}

@Composable
private fun SourceRow(
    iconRes: Int,
    name: String,
    count: Int,
    palette: CatClawPalette,
    onClick: () -> Unit,
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clickable(onClick = onClick)
            .padding(vertical = 12.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(13.dp),
    ) {
        Box(
            modifier = Modifier
                .size(50.dp)
                .clip(RoundedCornerShape(15.dp))
                .background(Color(palette.primary).copy(alpha = 0.30f)),
            contentAlignment = Alignment.Center,
        ) {
            Icon(
                painter = painterResource(iconRes),
                contentDescription = null,
                tint = Color(palette.textPrimary),
                modifier = Modifier.size(25.dp),
            )
        }
        Column(modifier = Modifier.weight(1f)) {
            Text(name, fontSize = 15.sp, fontWeight = FontWeight.Bold, color = Color(palette.textPrimary))
            Text(stringResource(R.string.playlist_count, count), fontSize = 12.sp, color = Color(palette.textSecondary))
        }
        Box(
            modifier = Modifier
                .size(34.dp)
                .clip(RoundedCornerShape(11.dp))
                .background(Color(palette.textPrimary).copy(alpha = 0.07f)),
            contentAlignment = Alignment.Center,
        ) {
            Icon(
                painter = painterResource(R.drawable.ic_arrow_forward),
                contentDescription = null,
                tint = Color(palette.textSecondary),
                modifier = Modifier.size(16.dp),
            )
        }
    }
}

/** 统计格点进去的独立列表页（MAUI 的 AllSongsPage / ArtistsPage / AlbumsPage） */
@Composable
fun LibraryListScreen(
    kind: LibraryKind,
    onBack: () -> Unit,
    viewModel: LibraryViewModel = hiltViewModel(),
) {
    val palette = LocalCatClawPalette.current
    val songs by viewModel.songs.collectAsStateWithLifecycle()
    val artists by viewModel.artists.collectAsStateWithLifecycle()
    val albums by viewModel.albums.collectAsStateWithLifecycle()
    val favorites by viewModel.favorites.collectAsStateWithLifecycle()

    Column(modifier = Modifier.fillMaxSize()) {
        Row(
            modifier = Modifier.fillMaxWidth().padding(start = 8.dp, end = 16.dp, top = 14.dp, bottom = 8.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Icon(
                painter = painterResource(R.drawable.ic_arrow_back),
                contentDescription = stringResource(R.string.queue_back),
                tint = Color(palette.textPrimary),
                modifier = Modifier
                    .size(40.dp)
                    .padding(10.dp)
                    .clickable(onClick = onBack),
            )
            Text(
                text = stringResource(
                    when (kind) {
                        LibraryKind.Songs -> R.string.lib_stat_songs
                        LibraryKind.Artists -> R.string.lib_stat_artists
                        LibraryKind.Albums -> R.string.lib_stat_albums
                        LibraryKind.Favorites -> R.string.lib_favorites_name
                    },
                ),
                fontSize = 17.sp,
                fontWeight = FontWeight.SemiBold,
                color = Color(palette.textPrimary),
            )
        }

        when (kind) {
            LibraryKind.Songs -> SongList(songs.map { it.title to it.artist }, songs.size) { viewModel.playAt(it) }
            LibraryKind.Favorites -> SongList(favorites.map { it.title to it.artist }, favorites.size) { viewModel.playFavoriteAt(it) }
            LibraryKind.Artists -> InfoList(artists.map { it.name to stringResource(R.string.playlist_count, it.songCount) })
            LibraryKind.Albums -> InfoList(albums.map { it.title to it.artist })
        }
    }
}

@Composable
private fun SongList(rows: List<Pair<String, String>>, total: Int, onClick: (Int) -> Unit) {
    val palette = LocalCatClawPalette.current
    if (rows.isEmpty()) {
        EmptyHint(stringResource(R.string.library_empty, total))
        return
    }
    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(bottom = 100.dp),
    ) {
        items(rows.size) { index ->
            val (title, subtitle) = rows[index]
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .clickable { onClick(index) }
                    .padding(horizontal = 20.dp, vertical = 9.dp),
            ) {
                Text(
                    text = title,
                    style = MaterialTheme.typography.bodyLarge,
                    color = Color(palette.textPrimary),
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                Text(
                    text = subtitle,
                    style = MaterialTheme.typography.bodySmall,
                    color = Color(palette.textSecondary),
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
        }
    }
}

@Composable
private fun InfoList(rows: List<Pair<String, String>>) {
    val palette = LocalCatClawPalette.current
    if (rows.isEmpty()) {
        EmptyHint(stringResource(R.string.library_none))
        return
    }
    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(bottom = 100.dp),
    ) {
        items(rows) { (name, detail) ->
            Row(
                modifier = Modifier.fillMaxWidth().padding(horizontal = 20.dp, vertical = 10.dp),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text(
                    text = name,
                    style = MaterialTheme.typography.bodyLarge,
                    color = Color(palette.textPrimary),
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                    modifier = Modifier.weight(1f),
                )
                Text(
                    text = detail,
                    style = MaterialTheme.typography.bodySmall,
                    color = Color(palette.textSecondary),
                )
            }
        }
    }
}

@Composable
private fun EmptyHint(text: String) {
    val palette = LocalCatClawPalette.current
    Box(modifier = Modifier.fillMaxSize().padding(28.dp), contentAlignment = Alignment.Center) {
        Text(text, style = MaterialTheme.typography.bodyMedium, color = Color(palette.textSecondary))
    }
}
