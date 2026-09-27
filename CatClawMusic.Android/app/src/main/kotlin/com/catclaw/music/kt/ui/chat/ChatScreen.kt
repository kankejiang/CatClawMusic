package com.catclaw.music.kt.ui.chat

import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.catclaw.music.kt.R
import com.catclaw.music.kt.core.ai.Bubble
import com.catclaw.music.kt.core.model.Song
import com.catclaw.music.kt.ui.theme.LocalCatClawPalette

/**
 * Yuki 聊天页，对应 MAUI `SearchPage.xaml` 的 ChatOverlay 那一层。
 *
 * ⚠ 消息列表是**倒序**的（index 0 = 最新），配 `reverseLayout = true` 后最新消息自然贴在底部，
 * 新消息到达不会把用户正在读的旧消息顶走；加载更多历史因此是尾部追加（视觉上向上长）。
 */
@Composable
fun ChatScreen(onBack: () -> Unit, viewModel: ChatViewModel = hiltViewModel()) {
    val palette = LocalCatClawPalette.current
    val messages by viewModel.messages.collectAsStateWithLifecycle()
    val thinking by viewModel.thinking.collectAsStateWithLifecycle()
    val title by viewModel.title.collectAsStateWithLifecycle()
    var text by rememberSaveable { mutableStateOf("") }
    val listState = rememberLazyListState()

    LaunchedEffect(Unit) { viewModel.open() }

    // 滚到列表末端（= 最旧的一侧）时翻下一页
    LaunchedEffect(messages.size) {
        val lastIndex = listState.layoutInfo.visibleItemsInfo.lastOrNull()?.index ?: -1
        if (lastIndex >= messages.size - 3) viewModel.loadMore()
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(Color(palette.backgroundStops.first().argb))
            .imePadding(),
    ) {
        ChatHeader(title = title, onBack = onBack)

        LazyColumn(
            state = listState,
            reverseLayout = true,
            contentPadding = androidx.compose.foundation.layout.PaddingValues(horizontal = 14.dp, vertical = 8.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp),
            modifier = Modifier
                .fillMaxWidth()
                .weight(1f),
        ) {
            items(messages.size) { i ->
                MessageRow(
                    bubble = messages[i],
                    primary = Color(palette.primary),
                    onPlay = { song, cards -> viewModel.play(song, cards) },
                )
            }
            if (thinking) {
                item(key = "typing") { TypingIndicator(Color(palette.textSecondary)) }
            }
        }

        InputRow(
            text = text,
            thinking = thinking,
            onTextChange = { text = it },
            onSend = {
                viewModel.send(text)
                text = ""
            },
            onStop = viewModel::stop,
        )
        Spacer(Modifier.height(8.dp))
    }
}

@Composable
private fun ChatHeader(title: String, onBack: () -> Unit) {
    val palette = LocalCatClawPalette.current
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .statusBarsPadding()
            .padding(horizontal = 12.dp, vertical = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Box(
            modifier = Modifier
                .size(40.dp)
                .clip(CircleShape)
                .clickable(onClickLabel = stringResource(R.string.chat_back)) { onBack() },
            contentAlignment = Alignment.Center,
        ) {
            Text("←", color = Color(palette.textPrimary), style = MaterialTheme.typography.titleLarge)
        }
        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = title.ifBlank { stringResource(R.string.chat_title) },
                style = MaterialTheme.typography.titleMedium,
                color = Color(palette.textPrimary),
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = stringResource(R.string.chat_yuki_desc),
                style = MaterialTheme.typography.labelSmall,
                color = Color(palette.textSecondary),
            )
        }
        YukiAvatar(size = 34.dp)
    }
}

@Composable
private fun MessageRow(bubble: Bubble, primary: Color, onPlay: (Song, List<Song>) -> Unit) {
    val palette = LocalCatClawPalette.current
    val isUser = bubble.role == "user"
    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = if (isUser) Arrangement.End else Arrangement.Start,
        verticalAlignment = Alignment.Top,
    ) {
        if (!isUser) {
            YukiAvatar(size = 30.dp)
            Spacer(Modifier.size(8.dp))
        }
        Column(horizontalAlignment = if (isUser) Alignment.End else Alignment.Start) {
            MessageBubble(
                bubble = bubble,
                isUser = isUser,
                primary = primary,
                surface = Color(palette.backgroundStops.last().argb),
                onPlay = onPlay,
            )
        }
    }
}

@Composable
private fun MessageBubble(
    bubble: Bubble,
    isUser: Boolean,
    primary: Color,
    surface: Color,
    onPlay: (Song, List<Song>) -> Unit,
) {
    val palette = LocalCatClawPalette.current
    Column(
        modifier = Modifier
            .widthIn(max = 300.dp)
            .clip(
                RoundedCornerShape(
                    topStart = if (isUser) 16.dp else 4.dp,
                    topEnd = if (isUser) 4.dp else 16.dp,
                    bottomStart = 16.dp,
                    bottomEnd = 16.dp,
                ),
            )
            .background(if (isUser) primary else surface)
            .padding(horizontal = 12.dp, vertical = 9.dp),
    ) {
        if (bubble.steps.isNotEmpty()) {
            bubble.steps.forEach { step ->
                Text(
                    text = step,
                    style = MaterialTheme.typography.labelSmall,
                    color = Color(palette.textSecondary),
                )
            }
        }
        if (bubble.reasoning.isNotEmpty()) {
            Text(
                text = bubble.reasoning,
                style = MaterialTheme.typography.bodySmall,
                color = Color(palette.textSecondary),
                maxLines = 6,
                overflow = TextOverflow.Ellipsis,
            )
        }
        if (bubble.content.isNotEmpty() || (bubble.thinking && bubble.steps.isEmpty())) {
            Text(
                text = bubble.content,
                style = MaterialTheme.typography.bodyMedium,
                color = if (isUser) Color(0xFFFFFFFF) else Color(palette.textPrimary),
            )
        }
        if (bubble.songs.isNotEmpty()) {
            Spacer(Modifier.height(6.dp))
            bubble.songs.forEach { song ->
                SongCardRow(song = song, onPlay = { onPlay(song, bubble.songs) })
            }
        }
    }
}

@Composable
private fun SongCardRow(song: Song, onPlay: () -> Unit) {
    val palette = LocalCatClawPalette.current
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(10.dp))
            .clickable(onClick = onPlay)
            .padding(vertical = 5.dp),
    ) {
        Text(
            text = song.title,
            style = MaterialTheme.typography.bodySmall,
            color = Color(palette.textPrimary),
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
        Text(
            text = song.artist,
            style = MaterialTheme.typography.labelSmall,
            color = Color(palette.textSecondary),
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
    }
}

@Composable
private fun YukiAvatar(size: androidx.compose.ui.unit.Dp) {
    Image(
        painter = painterResource(R.drawable.avatar_yuki),
        contentDescription = null,
        modifier = Modifier.size(size).clip(CircleShape),
    )
}

@Composable
private fun TypingIndicator(hint: Color) {
    Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(start = 40.dp)) {
        CircularProgressIndicator(strokeWidth = 2.dp, color = hint, modifier = Modifier.size(14.dp))
        Spacer(Modifier.size(8.dp))
        Text(stringResource(R.string.chat_thinking), style = MaterialTheme.typography.labelSmall, color = hint)
    }
}

@Composable
private fun InputRow(
    text: String,
    thinking: Boolean,
    onTextChange: (String) -> Unit,
    onSend: () -> Unit,
    onStop: () -> Unit,
) {
    val palette = LocalCatClawPalette.current
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = 12.dp),
        verticalAlignment = Alignment.Bottom,
    ) {
        OutlinedTextField(
            value = text,
            onValueChange = onTextChange,
            placeholder = { Text(stringResource(R.string.chat_placeholder), color = Color(palette.textSecondary)) },
            singleLine = true,
            shape = RoundedCornerShape(22.dp),
            modifier = Modifier
                .weight(1f)
                .heightIn(max = 120.dp),
        )
        if (thinking) {
            TextButton(onClick = onStop, modifier = Modifier.padding(bottom = 6.dp)) {
                Text(stringResource(R.string.chat_stop), color = Color(palette.textPrimary))
            }
        } else {
            TextButton(
                onClick = onSend,
                enabled = text.isNotBlank(),
                modifier = Modifier.padding(bottom = 6.dp),
            ) {
                Text(stringResource(R.string.chat_send), color = Color(palette.primary))
            }
        }
    }
}
