using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;

namespace CatClawMusic.MusicTagPlugin;

/// <summary>
/// MusicTag 菜单贡献者插件，实现 <see cref="IMenuContributorPlugin"/> 接口。
/// 提供歌曲元数据编辑、歌词/封面联网搜索、批量自动匹配标签等功能。
/// 支持 Local、Cache、WebDAV 三种歌曲来源的读写操作。
/// </summary>
public class MusicTagMenuContributor : IMenuContributorPlugin
{
    /// <summary>"匹配元数据"菜单项标识符</summary>
    private const int MenuMatchMetadata = 10001;
    /// <summary>共享的 HTTP 客户端，超时时间 10 秒，用于联网搜索歌词与封面</summary>
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>插件唯一标识符</summary>
    public string PluginId => "musictag.menu";
    /// <summary>插件显示名称</summary>
    public string Name => "MusicTag 元数据编辑";
    /// <summary>插件版本号</summary>
    public string Version => "2.0.0";
    /// <summary>插件作者</summary>
    public string Author => "CatClawMusic";
    /// <summary>插件功能描述</summary>
    public string Description => "提供歌曲元数据匹配功能，通过网络搜索自动获取歌词、封面和元数据信息并写入标签。";

    /// <summary>插件能力列表，用于在主界面展示功能概览</summary>
    public List<string> Capabilities => new()
    {
        "元数据匹配: 标题、艺术家、专辑、年份、音轨号、流派",
        "歌词搜索: LRCLIB / 网易云音乐",
        "封面搜索: iTunes / Deezer 高清封面",
        "MusicBrainz: 智能匹配元数据",
        "WebDAV 支持: 下载-编辑-上传回服务器"
    };

    /// <summary>插件初始化，返回已完成的任务</summary>
    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>插件关闭时释放 <see cref="_httpClient"/> 资源</summary>
    public Task ShutdownAsync()
    {
        _httpClient.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>获取曲目右键菜单项，包含"匹配元数据"</summary>
    /// <param name="song">当前选中的歌曲</param>
    /// <returns>菜单项列表</returns>
    public List<MenuItemEntry> GetMenuItems(Song song)
    {
        return new List<MenuItemEntry>
        {
            new(MenuMatchMetadata, "匹配元数据")
        };
    }

    /// <summary>菜单项点击处理，根据 <paramref name="itemId"/> 分发到匹配元数据弹窗</summary>
    /// <param name="itemId">被点击的菜单项标识符</param>
    /// <param name="song">当前歌曲</param>
    /// <param name="fragment">Android Fragment 对象，用于获取 Context 和启动弹窗</param>
    public Task OnMenuItemClicked(int itemId, Song song, object fragment)
    {
        if (itemId == MenuMatchMetadata)
            ShowMatchMetadataDialog(fragment, song);

        return Task.CompletedTask;
    }

    #region Context 获取（兼容 AndroidX Fragment）

    /// <summary>从 Android Fragment 对象中提取 <see cref="global::Android.Content.Context"/>，兼容 AndroidX</summary>
    /// <param name="fragment">Android Fragment 对象</param>
    /// <returns>获取到的 Context；若无法获取则返回 null</returns>
    private static global::Android.Content.Context? GetContext(object fragment)
    {
        if (fragment is global::Android.App.Fragment appFrag)
            return appFrag.Context ?? appFrag.Activity;

        if (fragment != null)
        {
            var type = fragment.GetType();
            var ctxProp = type.GetProperty("Context") ?? type.GetProperty("Activity");
            return ctxProp?.GetValue(fragment) as global::Android.Content.Context;
        }

        return null;
    }

    #endregion

    #region 匹配元数据弹窗

    /// <summary>
    /// 显示匹配元数据弹窗。用户选择歌词搜索源（LRCLIB/网易云音乐）、输入关键字、
    /// 勾选需要匹配的字段，系统将通过网络搜索自动获取并写入元数据
    /// </summary>
    /// <param name="fragment">Android Fragment 对象</param>
    /// <param name="song">当前歌曲</param>
    private async void ShowMatchMetadataDialog(object fragment, Song song)
    {
        var ctx = GetContext(fragment);
        if (ctx == null) return;

        var activity = ctx as global::Android.App.Activity;
        if (!IsSongEditable(song))
        {
            ShowToast(ctx, "当前歌曲来源不支持编辑", activity);
            return;
        }

        var dp = dpToPx(ctx, 1);

        var scrollView = new global::Android.Widget.ScrollView(ctx);
        var layout = new global::Android.Widget.LinearLayout(ctx)
        { Orientation = global::Android.Widget.Orientation.Vertical };
        layout.SetPadding(dp * 16, dp * 10, dp * 16, dp * 10);

        var sourceLabel = new global::Android.Widget.TextView(ctx) { Text = "歌词搜索源" };
        sourceLabel.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 13f);
        sourceLabel.SetTextColor(global::Android.Graphics.Color.ParseColor("#B0A8BA"));
        layout.AddView(sourceLabel);

        var spSource = new global::Android.Widget.Spinner(ctx);
        var sourceAdapter = new global::Android.Widget.ArrayAdapter<string>(ctx,
            global::Android.Resource.Layout.SimpleSpinnerItem,
            new[] { "LRCLIB", "网易云音乐" });
        sourceAdapter.SetDropDownViewResource(global::Android.Resource.Layout.SimpleSpinnerDropDownItem);
        spSource.Adapter = sourceAdapter;
        layout.AddView(spSource);

        var keywordRow = new global::Android.Widget.LinearLayout(ctx)
        { Orientation = global::Android.Widget.Orientation.Horizontal };
        keywordRow.SetGravity(global::Android.Views.GravityFlags.CenterVertical);
        keywordRow.SetPadding(0, dp * 8, 0, dp * 4);

        var etKeyword = new global::Android.Widget.EditText(ctx)
        { Text = $"{song.Title} {song.Artist}", Hint = "输入搜索关键字" };
        etKeyword.SetTextColor(global::Android.Graphics.Color.ParseColor("#E8E0F0"));
        etKeyword.SetHintTextColor(global::Android.Graphics.Color.ParseColor("#605870"));
        etKeyword.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 14f);
        etKeyword.InputType = global::Android.Text.InputTypes.TextVariationPersonName;
        etKeyword.SetPadding(dp * 12, dp * 8, dp * 12, dp * 8);

        var btnSearch = CreateAccentButton(ctx, "🔍 搜索", "#7B61AF");
        btnSearch.SetPadding(dp * 16, dp * 4, dp * 16, dp * 4);

        keywordRow.AddView(etKeyword, new global::Android.Widget.LinearLayout.LayoutParams(0, wrap) { Weight = 1 });
        keywordRow.AddView(btnSearch, new global::Android.Widget.LinearLayout.LayoutParams(wrap, wrap));
        layout.AddView(keywordRow);

        layout.AddView(CreateDivider(ctx));

        var allLabel = new global::Android.Widget.TextView(ctx) { Text = "匹配字段" };
        allLabel.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 14f);
        allLabel.SetTextColor(global::Android.Graphics.Color.ParseColor("#9B7ED8"));
        allLabel.SetTypeface(null, global::Android.Graphics.TypefaceStyle.Bold);
        allLabel.SetPadding(0, dp * 4, 0, dp * 4);
        layout.AddView(allLabel);

        var cbAll = new global::Android.Widget.CheckBox(ctx)
        { Text = "全部匹配", Checked = false };
        cbAll.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 14f);
        cbAll.SetTextColor(global::Android.Graphics.Color.ParseColor("#E8E0F0"));
        layout.AddView(cbAll);

        var fieldDefs = new[]
        {
            ("lyrics", "歌词", true, global::Android.Graphics.Color.ParseColor("#9B7ED8")),
            ("cover", "封面", true, global::Android.Graphics.Color.ParseColor("#D87E9B")),
            ("title", "标题", false, global::Android.Graphics.Color.ParseColor("#E8E0F0")),
            ("artist", "艺术家", true, global::Android.Graphics.Color.ParseColor("#E8E0F0")),
            ("album", "专辑", true, global::Android.Graphics.Color.ParseColor("#E8E0F0")),
            ("year", "年份", true, global::Android.Graphics.Color.ParseColor("#E8E0F0")),
            ("track", "音轨号", false, global::Android.Graphics.Color.ParseColor("#E8E0F0")),
            ("genre", "流派", false, global::Android.Graphics.Color.ParseColor("#E8E0F0"))
        };

        var checkboxes = new Dictionary<string, global::Android.Widget.CheckBox>();

        foreach (var (key, label, defaultChecked, color) in fieldDefs)
        {
            var cb = new global::Android.Widget.CheckBox(ctx)
            { Text = label, Checked = defaultChecked };
            cb.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 13f);
            cb.SetTextColor(color);
            cb.SetPadding(dp * 12, 0, 0, 0);
            layout.AddView(cb);
            checkboxes[key] = cb;
        }

        cbAll.CheckedChange += (s, e) =>
        {
            foreach (var cb in checkboxes.Values)
                cb.Checked = e.IsChecked;
        };

        var resultTv = new global::Android.Widget.TextView(ctx);
        resultTv.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 12f);
        resultTv.SetTextColor(global::Android.Graphics.Color.ParseColor("#B0A8BA"));
        resultTv.SetPadding(dp * 8, dp * 8, dp * 8, dp * 8);
        resultTv.Visibility = global::Android.Views.ViewStates.Gone;
        layout.AddView(resultTv);

        MetadataMatchResult? matchedMeta = null;
        LrcSearchResult? matchedLyrics = null;
        CoverSearchResult? matchedCover = null;
        List<MetadataMatchResult> allMetaResults = new();
        List<LrcSearchResult> allLyricsResults = new();
        List<CoverSearchResult> allCoverResults = new();

        scrollView.AddView(layout);

        btnSearch.Click += async (s, e) =>
        {
            var keyword = etKeyword.Text?.Trim();
            if (string.IsNullOrWhiteSpace(keyword))
            {
                activity?.RunOnUiThread(() => ShowToast(ctx, "请输入搜索关键字", activity));
                return;
            }

            btnSearch.Text = "搜索中...";
            btnSearch.Enabled = false;
            resultTv.Text = "⏳ 正在搜索...";
            resultTv.Visibility = global::Android.Views.ViewStates.Visible;

            var selectedSource = spSource.SelectedItemPosition == 0 ? "LRCLIB" : "网易云音乐";

            var needMeta = checkboxes["title"].Checked || checkboxes["artist"].Checked ||
                           checkboxes["album"].Checked || checkboxes["year"].Checked ||
                           checkboxes["track"].Checked || checkboxes["genre"].Checked;
            var needLyrics = checkboxes["lyrics"].Checked;
            var needCover = checkboxes["cover"].Checked;

            try
            {
                var parts = keyword.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                var searchTitle = parts[0];
                var searchArtist = parts.Length > 1 ? parts[1] : song.Artist ?? "";

                var searchSong = new Song
                {
                    Title = searchTitle,
                    Artist = searchArtist,
                    Album = song.Album ?? "",
                    Duration = song.Duration,
                    Year = song.Year
                };

                var searchTasks = new List<Task>();
                List<MetadataMatchResult> metaRes = new();
                List<LrcSearchResult> lyricsRes = new();
                List<CoverSearchResult> coverRes = new();

                if (needMeta)
                {
                    searchTasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            var r = await new MusicBrainzMetadataPlugin().SearchMetadataAsync(searchSong);
                            lock (metaRes) metaRes.AddRange(r);
                        }
                        catch { }
                    }));
                }

                if (needLyrics)
                {
                    searchTasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            if (selectedSource == "LRCLIB")
                            {
                                var r = await new MusicTagLyricsPlugin().SearchLrcLibAsync(searchSong);
                                lock (lyricsRes) lyricsRes.AddRange(r);
                            }
                            else
                            {
                                var r = await new MusicTagLyricsPlugin().SearchNeteaseAsync(searchSong);
                                lock (lyricsRes) lyricsRes.AddRange(r);
                            }
                        }
                        catch { }
                    }));
                }

                if (needCover)
                {
                    searchTasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            var iTunesTask = new MusicTagCoverPlugin().SearchiTunesAsync(searchSong);
                            var deezerTask = new MusicTagCoverPlugin().SearchDeezerAsync(searchSong);
                            await Task.WhenAll(iTunesTask, deezerTask);
                            lock (coverRes)
                            {
                                coverRes.AddRange(iTunesTask.Result);
                                coverRes.AddRange(deezerTask.Result);
                            }
                        }
                        catch { }
                    }));
                }

                if (searchTasks.Count > 0)
                    await Task.WhenAll(searchTasks);

                allMetaResults = metaRes;
                allLyricsResults = lyricsRes;
                allCoverResults = coverRes;

                matchedMeta = allMetaResults.Count > 0 ? allMetaResults[0] : null;
                matchedLyrics = allLyricsResults.Count > 0 ? allLyricsResults[0] : null;
                matchedCover = allCoverResults.Count > 0 ? allCoverResults[0] : null;

                activity?.RunOnUiThread(() =>
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("===== 搜索结果 =====");

                    if (needMeta)
                    {
                        if (matchedMeta != null)
                        {
                            sb.AppendLine($"🎵 元数据({allMetaResults.Count}条): {matchedMeta.Title} - {matchedMeta.Artist}");
                            sb.AppendLine($"   专辑: {matchedMeta.Album ?? "无"}");
                            sb.AppendLine($"   年份: {(matchedMeta.Year > 0 ? matchedMeta.Year.ToString() : "无")}");
                            if (allMetaResults.Count > 1)
                                sb.AppendLine($"   💡 点击此处选择其他版本");
                        }
                        else
                            sb.AppendLine("🎵 元数据: 未找到");
                    }

                    if (needLyrics)
                    {
                        if (matchedLyrics != null)
                        {
                            var lineCount = matchedLyrics.Lyrics?.Lines.Count ?? 0;
                            sb.AppendLine($"📝 歌词({allLyricsResults.Count}条): {matchedLyrics.Source} ({lineCount}行)");
                            if (allLyricsResults.Count > 1)
                                sb.AppendLine($"   💡 点击此处选择其他版本");
                        }
                        else
                            sb.AppendLine($"📝 歌词: {selectedSource} 未找到");
                    }

                    if (needCover)
                    {
                        if (matchedCover != null)
                            sb.AppendLine($"🖼 封面({allCoverResults.Count}条): {matchedCover.Source} ({matchedCover.Width}x{matchedCover.Height})");
                        else
                            sb.AppendLine("🖼 封面: 未找到");
                    }

                    resultTv.Text = sb.ToString();
                    resultTv.Clickable = true;
                    btnSearch.Text = "🔍 搜索";
                    btnSearch.Enabled = true;
                });
            }
            catch (Exception ex)
            {
                activity?.RunOnUiThread(() =>
                {
                    resultTv.Text = $"❌ 搜索失败: {ex.Message}";
                    btnSearch.Text = "🔍 搜索";
                    btnSearch.Enabled = true;
                });
            }
        };

        resultTv.Click += (s, e) =>
        {
            var items = new List<string>();
            var actions = new List<Action>();

            if (allLyricsResults.Count > 1)
            {
                items.Add("📝 选择歌词版本");
                actions.Add(() => ShowLyricsSelectionDialog(ctx, allLyricsResults, chosen =>
                {
                    matchedLyrics = chosen;
                    var lineCount = chosen.Lyrics?.Lines.Count ?? 0;
                    resultTv.Text = $"✅ 歌词已选择: {chosen.Source} | {chosen.Artist} - {chosen.Title} ({lineCount}行)";
                    resultTv.SetTextColor(global::Android.Graphics.Color.ParseColor("#2D7A50"));
                }, activity));
            }

            if (allCoverResults.Count > 1)
            {
                items.Add("🖼 选择封面版本");
                actions.Add(() => ShowCoverSelectionDialog(ctx, allCoverResults, chosen =>
                {
                    matchedCover = chosen;
                    resultTv.Text = $"✅ 封面已选择: {chosen.Source} | {chosen.AlbumName} ({chosen.Width}x{chosen.Height})";
                    resultTv.SetTextColor(global::Android.Graphics.Color.ParseColor("#2D7A50"));
                }, activity));
            }

            if (allMetaResults.Count > 1)
            {
                items.Add("🎵 选择元数据版本");
                actions.Add(() =>
                {
                    var metaItems = new string[allMetaResults.Count];
                    for (int i = 0; i < allMetaResults.Count; i++)
                    {
                        var m = allMetaResults[i];
                        metaItems[i] = $"[{i + 1}] {m.Title} - {m.Artist} | {m.Album ?? "无"} (匹配度:{m.MatchScore})";
                    }
                    new global::Android.App.AlertDialog.Builder(ctx)
                        .SetTitle("选择元数据版本")
                        .SetSingleChoiceItems(metaItems, allMetaResults.IndexOf(matchedMeta!), (d, which) => { })
                        .SetPositiveButton("确定", (d, which) =>
                        {
                            var dialog = (global::Android.App.AlertDialog)d;
                            var pos = dialog.ListView.CheckedItemPosition;
                            if (pos >= 0 && pos < allMetaResults.Count)
                            {
                                matchedMeta = allMetaResults[pos];
                                resultTv.Text = $"✅ 元数据已选择: {matchedMeta.Title} - {matchedMeta.Artist}";
                                resultTv.SetTextColor(global::Android.Graphics.Color.ParseColor("#2D7A50"));
                            }
                        })
                        .SetNegativeButton("取消", (d, which) => { })
                        .Show();
                });
            }

            if (items.Count == 0)
            {
                ShowToast(ctx, "暂无可选择的多版本结果", activity);
                return;
            }

            if (items.Count == 1)
            {
                actions[0]();
                return;
            }

            new global::Android.App.AlertDialog.Builder(ctx)
                .SetTitle("选择结果版本")
                .SetItems(items.ToArray(), (d, which) => actions[which.Which]())
                .Show();
        };

        new global::Android.App.AlertDialog.Builder(ctx)
            .SetTitle($"匹配元数据 - {song.Title}")
            .SetView(scrollView)
            .SetPositiveButton("应用", async (d, args) =>
            {
                if (matchedMeta == null && matchedLyrics == null && matchedCover == null)
                {
                    ShowToast(ctx, "请先搜索后再应用", activity);
                    return;
                }

                string? title = checkboxes["title"].Checked && matchedMeta != null ? matchedMeta.Title : null;
                string? artist = checkboxes["artist"].Checked && matchedMeta != null ? matchedMeta.Artist : null;
                string? album = checkboxes["album"].Checked && matchedMeta != null ? matchedMeta.Album : null;
                uint? year = checkboxes["year"].Checked && matchedMeta?.Year > 0 ? matchedMeta.Year : null;
                uint? track = checkboxes["track"].Checked && matchedMeta?.TrackNumber > 0 ? matchedMeta.TrackNumber : null;
                string? genre = checkboxes["genre"].Checked && matchedMeta != null ? matchedMeta.Genre : null;

                byte[]? coverBytes = checkboxes["cover"].Checked ? matchedCover?.ImageBytes : null;
                string? lrcText = null;
                if (checkboxes["lyrics"].Checked && matchedLyrics?.Lyrics != null && matchedLyrics.Lyrics.Lines.Count > 0)
                    lrcText = string.Join("\n", matchedLyrics.Lyrics.Lines.Select(l => $"[{l.Timestamp:mm\\:ss\\.ff}]{l.Text}"));

                var success = await SaveSongMetadataAsync(ctx, song, title, artist, album,
                    year, track, genre, coverBytes, lrcText, 0, 0, activity);

                ShowToast(ctx, success ? "✅ 元数据已保存" : "❌ 保存失败", activity);
            })
            .SetNegativeButton("取消", (d, args) => { })
            .Show();
    }

    #endregion

    #region 保存逻辑（Local / WebDAV / Cache）

    /// <summary>判断歌曲是否可编辑（Local、Cache 或 WebDAV 来源可编辑，网络流等不可编辑）</summary>
    /// <param name="song">歌曲对象</param>
    /// <returns>可编辑返回 true，否则返回 false</returns>
    private static bool IsSongEditable(Song song)
    {
        return song.Source is SongSource.Local or SongSource.Cache or SongSource.WebDAV;
    }

    /// <summary>保存歌曲元数据到文件，支持写入标题/艺术家/专辑/年份/音轨号/流派、封面和歌词，WebDAV 歌曲会先下载再上传</summary>
    /// <param name="ctx">Android Context</param>
    /// <param name="song">要保存的歌曲</param>
    /// <param name="title">新标题（null 不修改）</param>
    /// <param name="artist">新艺术家（null 不修改）</param>
    /// <param name="album">新专辑（null 不修改）</param>
    /// <param name="year">新年份（null 不修改）</param>
    /// <param name="trackNum">新音轨号（null 不修改）</param>
    /// <param name="genre">新流派（null 不修改）</param>
    /// <param name="coverBytes">封面图片字节数组（null 不写入）</param>
    /// <param name="lrcText">歌词文本内容（null 不写入）</param>
    /// <param name="lyricsSaveMode">歌词保存模式：0=存到标签，1=存到文件，2=两者都存，-1=不保存</param>
    /// <param name="coverSaveMode">封面保存模式：0=存到标签，1=存到文件，2=两者都存，-1=不保存</param>
    /// <param name="activity">当前 Activity（用于 UI 线程回调）</param>
    /// <returns>保存成功返回 true，失败返回 false</returns>
    private static async Task<bool> SaveSongMetadataAsync(global::Android.Content.Context ctx, Song song,
        string? title, string? artist, string? album, uint? year, uint? trackNum, string? genre,
        byte[]? coverBytes, string? lrcText, int lyricsSaveMode, int coverSaveMode,
        global::Android.App.Activity? activity)
    {
        try
        {
            string? localPath = null;
            bool needUploadBack = false;
            string? webDavRemotePath = null;

            if (song.Source == SongSource.Local)
            {
                localPath = song.FilePath;
            }
            else if (song.Source == SongSource.Cache)
            {
                localPath = song.FilePath;
            }
            else if (song.Source == SongSource.WebDAV)
            {
                localPath = await DownloadToLocalAsync(ctx, song);
                if (localPath == null) return false;
                needUploadBack = true;
                webDavRemotePath = song.RemoteId ?? ExtractWebDavPath(song.FilePath);
            }
            else
            {
                return false;
            }

            if (string.IsNullOrEmpty(localPath) || !System.IO.File.Exists(localPath))
                return false;

            // 写入元数据
            bool hasMetaChanges = title != null || artist != null || album != null || year.HasValue || trackNum.HasValue || genre != null;
            if (hasMetaChanges)
            {
                TagReader.WriteMetadata(localPath, title, artist, album, year, trackNum, genre);
            }

            // 写入封面
            if (coverBytes != null && coverSaveMode >= 0)
            {
                if (coverSaveMode == 0 || coverSaveMode == 2)
                    TagReader.WriteCoverToFile(localPath, coverBytes);
                if (coverSaveMode == 1 || coverSaveMode == 2)
                    SaveCoverAsFile(localPath, coverBytes);
            }

            // 写入歌词
            if (!string.IsNullOrEmpty(lrcText) && lyricsSaveMode >= 0)
            {
                if (lyricsSaveMode == 0 || lyricsSaveMode == 2)
                    TagReader.WriteEmbeddedLyrics(localPath, lrcText);
                if (lyricsSaveMode == 1 || lyricsSaveMode == 2)
                    SaveLyricsAsFile(localPath, lrcText);
            }

            // WebDAV 上传回服务器
            if (needUploadBack && !string.IsNullOrEmpty(webDavRemotePath))
            {
                await UploadBackToWebDavAsync(ctx, localPath, webDavRemotePath);
            }

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MusicTag] SaveMetadata failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>将 WebDAV 歌曲下载到本地临时文件以便编辑</summary>
    /// <param name="ctx">Android Context</param>
    /// <param name="song">WebDAV 来源的歌曲</param>
    /// <returns>本地临时文件路径；失败返回 null</returns>
    private static async Task<string?> DownloadToLocalAsync(global::Android.Content.Context ctx, Song song)
    {
        try
        {
            var cacheDir = global::Android.App.Application.Context!.CacheDir!.AbsolutePath;
            var tempDir = System.IO.Path.Combine(cacheDir, "musictag_temp");
            System.IO.Directory.CreateDirectory(tempDir);
            var tempPath = System.IO.Path.Combine(tempDir, $"edit_{song.Id}_{Guid.NewGuid():N}.tmp");

            if (song.Source == SongSource.WebDAV && !string.IsNullOrEmpty(song.FilePath))
            {
                var netFileService = GetService<INetworkFileService>();
                if (netFileService == null) return null;

                var webDavPath = ExtractWebDavPath(song.FilePath) ?? song.FilePath;
                using var stream = await netFileService.OpenReadAsync(webDavPath);
                using var fs = new System.IO.FileStream(tempPath, System.IO.FileMode.Create, System.IO.FileAccess.Write);
                await stream.CopyToAsync(fs);
                return tempPath;
            }

            return song.FilePath;
        }
        catch { return null; }
    }

    /// <summary>编辑完成后将本地文件上传回 WebDAV 服务器</summary>
    /// <param name="ctx">Android Context</param>
    /// <param name="localPath">本地文件路径</param>
    /// <param name="remotePath">WebDAV 远程路径</param>
    private static async Task UploadBackToWebDavAsync(global::Android.Content.Context ctx, string localPath, string remotePath)
    {
        try
        {
            var bytes = await System.IO.File.ReadAllBytesAsync(localPath);
            var netFileService = GetService<INetworkFileService>();
            if (netFileService != null)
            {
                var result = await netFileService.UploadFileAsync(remotePath, bytes, "audio/mpeg");
                System.Diagnostics.Debug.WriteLine($"[MusicTag] WebDAV upload back: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MusicTag] WebDAV upload failed: {ex.Message}");
        }
    }

    /// <summary>将歌词文本保存为与音频文件同名的 .lrc 文件</summary>
    /// <param name="audioPath">音频文件路径</param>
    /// <param name="lrcText">LRC 歌词文本</param>
    private static void SaveLyricsAsFile(string audioPath, string lrcText)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(audioPath) ?? "";
            var baseName = System.IO.Path.GetFileNameWithoutExtension(audioPath);
            var lrcPath = System.IO.Path.Combine(dir, $"{baseName}.lrc");
            System.IO.File.WriteAllText(lrcPath, lrcText, System.Text.Encoding.UTF8);
        }
        catch { }
    }

    /// <summary>将封面图片保存为与音频文件同名的 .jpg 文件</summary>
    /// <param name="audioPath">音频文件路径</param>
    /// <param name="coverBytes">封面图片字节数组</param>
    private static void SaveCoverAsFile(string audioPath, byte[] coverBytes)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(audioPath) ?? "";
            var baseName = System.IO.Path.GetFileNameWithoutExtension(audioPath);
            var coverPath = System.IO.Path.Combine(dir, $"{baseName}.jpg");
            System.IO.File.WriteAllBytes(coverPath, coverBytes);
        }
        catch { }
    }

    /// <summary>从 HTTP/HTTPS URL 中提取 WebDAV 路径部分（去掉协议和域名）</summary>
    /// <param name="url">原始 URL 或路径</param>
    /// <returns>提取后的路径；解析失败返回 null</returns>
    private static string? ExtractWebDavPath(string url)
    {
        try
        {
            if (url.StartsWith("http://") || url.StartsWith("https://"))
            {
                var uri = new Uri(url);
                return uri.AbsolutePath;
            }
            return url;
        }
        catch { return null; }
    }

    #endregion

    #region 选择弹窗

    /// <summary>显示歌词版本选择弹窗，允许用户从搜索结果中选取一个歌词版本</summary>
    /// <param name="ctx">Android Context</param>
    /// <param name="results">歌词搜索结果列表</param>
    /// <param name="onSelected">用户确认选择后的回调</param>
    /// <param name="activity">当前 Activity</param>
    private static void ShowLyricsSelectionDialog(global::Android.Content.Context ctx,
        List<LrcSearchResult> results, Action<LrcSearchResult> onSelected, global::Android.App.Activity? activity)
    {
        var items = new string[results.Count];
        var checkedItem = 0;
        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            var lines = r.Lyrics?.Lines.Count ?? 0;
            items[i] = $"[{i + 1}] {r.Source} | {r.Artist} - {r.Title}\n     ({lines} 行歌词)";
        }

        new global::Android.App.AlertDialog.Builder(ctx)
            .SetTitle("选择歌词版本")
            .SetSingleChoiceItems(items, checkedItem, (d, which) => { })
            .SetPositiveButton("确定", (d, which) =>
            {
                var dialog = (global::Android.App.AlertDialog)d;
                var pos = dialog.ListView.CheckedItemPosition;
                if (pos >= 0 && pos < results.Count)
                    onSelected(results[pos]);
            })
            .SetNegativeButton("取消", (d, which) => { })
            .Show();
    }

    /// <summary>显示封面版本选择弹窗，以网格卡片形式展示搜索结果，允许用户点击选中一个封面</summary>
    /// <param name="ctx">Android Context</param>
    /// <param name="results">封面搜索结果列表</param>
    /// <param name="onSelected">用户确认选择后的回调</param>
    /// <param name="activity">当前 Activity</param>
    private static async void ShowCoverSelectionDialog(global::Android.Content.Context ctx,
        List<CoverSearchResult> results, Action<CoverSearchResult> onSelected, global::Android.App.Activity? activity)
    {
        var scrollView = new global::Android.Widget.ScrollView(ctx);
        var gridLayout = new global::Android.Widget.LinearLayout(ctx)
        { Orientation = global::Android.Widget.Orientation.Vertical };
        gridLayout.SetPadding(dpToPx(ctx, 8), dpToPx(ctx, 8), dpToPx(ctx, 8), dpToPx(ctx, 8));

        CoverSearchResult? chosenResult = null;

        int cols = Math.Max(1, Math.Min(3, results.Count));
        int idx = 0;
        foreach (var r in results)
        {
            var card = new global::Android.Widget.LinearLayout(ctx)
            { Orientation = global::Android.Widget.Orientation.Vertical };
            card.SetGravity(global::Android.Views.GravityFlags.CenterHorizontal);
            card.SetPadding(dpToPx(ctx, 4), dpToPx(ctx, 4), dpToPx(ctx, 4), dpToPx(ctx, 4));
            card.SetBackgroundColor(global::Android.Graphics.Color.ParseColor("#2A2438"));

            var iv = new global::Android.Widget.ImageView(ctx);
            iv.LayoutParameters = new global::Android.Views.ViewGroup.LayoutParams(
                dpToPx(ctx, 160), dpToPx(ctx, 160));
            iv.SetScaleType(global::Android.Widget.ImageView.ScaleType.FitCenter);
            if (r.ImageBytes != null)
                iv.SetImageBitmap(global::Android.Graphics.BitmapFactory.DecodeByteArray(r.ImageBytes, 0, r.ImageBytes.Length));

            var tvLabel = new global::Android.Widget.TextView(ctx);
            tvLabel.Text = $"[{idx + 1}] {r.Source}\n{r.AlbumName}\n{r.Width}x{r.Height}";
            tvLabel.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 10f);
            tvLabel.SetTextColor(global::Android.Graphics.Color.ParseColor("#B0A8BA"));
            tvLabel.Gravity = global::Android.Views.GravityFlags.CenterHorizontal;
            tvLabel.SetPadding(0, dpToPx(ctx, 4), 0, 0);

            card.AddView(iv);
            card.AddView(tvLabel);

            var cardIdx = idx;
            card.Click += (s, e) =>
            {
                for (int ci = 0; ci < gridLayout.ChildCount; ci++)
                    if (gridLayout.GetChildAt(ci) is global::Android.Widget.LinearLayout ll)
                        ll.SetBackgroundColor(global::Android.Graphics.Color.ParseColor("#2A2438"));
                card.SetBackgroundColor(global::Android.Graphics.Color.ParseColor("#9B7ED8"));
                chosenResult = results[cardIdx];
            };

            card.LayoutParameters = new global::Android.Widget.LinearLayout.LayoutParams(0, wrap) { Weight = 1 };
            gridLayout.AddView(card);
            idx++;
        }

        // 默认选中第一个
        if (gridLayout.ChildCount > 0 && gridLayout.GetChildAt(0) is global::Android.Widget.LinearLayout firstCard)
            firstCard.SetBackgroundColor(global::Android.Graphics.Color.ParseColor("#9B7ED8"));
        if (results.Count > 0) chosenResult = results[0];

        scrollView.AddView(gridLayout);

        new global::Android.App.AlertDialog.Builder(ctx)
            .SetTitle($"选择封面版本（共{results.Count}张）")
            .SetMessage("点击图片选中，然后点确定")
            .SetView(scrollView)
            .SetPositiveButton("确定", (d, args) =>
            {
                if (chosenResult != null) onSelected(chosenResult);
            })
            .SetNegativeButton("取消", (d, args) => { })
            .Show();
    }

    #endregion

    #region UI 工具方法

    /// <summary>Android LayoutParams MATCH_PARENT 常量值</summary>
    private const int matchParent = -1;
    /// <summary>Android LayoutParams WRAP_CONTENT 常量值</summary>
    private const int wrap = -2;

    /// <summary>创建带样式预设的 EditText 输入框</summary>
    /// <param name="ctx">Android Context</param>
    /// <param name="value">初始文本值</param>
    private static global::Android.Widget.EditText CreateEditText(global::Android.Content.Context ctx, string value)
    {
        var et = new global::Android.Widget.EditText(ctx)
        { Text = value };
        et.SetTextColor(global::Android.Graphics.Color.ParseColor("#E8E0F0"));
        et.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 14f);
        et.InputType = global::Android.Text.InputTypes.TextVariationPersonName;
        var padding = (int)(14 * ctx.Resources!.DisplayMetrics!.Density);
        et.SetPadding(padding, padding / 2, padding, padding / 2);
        return et;
    }

    /// <summary>向布局添加一个标签-输入框行（标签在上，输入框在下）</summary>
    /// <param name="parent">父布局容器</param>
    /// <param name="ctx">Android Context</param>
    /// <param name="label">字段标签文本</param>
    /// <param name="editText">对应的输入框</param>
    private static void AddFieldRow(global::Android.Widget.LinearLayout parent, global::Android.Content.Context ctx,
        string label, global::Android.Widget.EditText editText)
    {
        var tv = new global::Android.Widget.TextView(ctx) { Text = label };
        tv.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 12f);
        tv.SetTextColor(global::Android.Graphics.Color.ParseColor("#B0A8BA"));
        parent.AddView(tv);
        parent.AddView(editText);
    }

    /// <summary>创建区域标题 TextView（紫色加粗）</summary>
    /// <param name="ctx">Android Context</param>
    /// <param name="text">标题文本</param>
    private static global::Android.Widget.TextView CreateSectionHeader(global::Android.Content.Context ctx, string text)
    {
        var tv = new global::Android.Widget.TextView(ctx) { Text = text };
        tv.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 15f);
        tv.SetTextColor(global::Android.Graphics.Color.ParseColor("#9B7ED8"));
        tv.SetPadding(0, dpToPx(ctx, 12), 0, 0);
        tv.SetTypeface(null, global::Android.Graphics.TypefaceStyle.Bold);
        return tv;
    }

    /// <summary>创建强调色按钮（文字和背景使用指定颜色的不同透明度）</summary>
    /// <param name="ctx">Android Context</param>
    /// <param name="text">按钮文本</param>
    /// <param name="colorHex">颜色十六进制字符串（如 "#9B7ED8"）</param>
    private static global::Android.Widget.Button CreateAccentButton(global::Android.Content.Context ctx, string text, string colorHex)
    {
        var btn = new global::Android.Widget.Button(ctx) { Text = text };
        btn.SetTextColor(global::Android.Graphics.Color.ParseColor(colorHex));
        btn.SetBackgroundColor(global::Android.Graphics.Color.ParseColor($"{colorHex}20"));
        btn.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 13f);
        return btn;
    }

    /// <summary>创建下拉选择器 Spinner</summary>
    /// <param name="ctx">Android Context</param>
    /// <param name="items">选项数组</param>
    private static global::Android.Widget.Spinner CreateSpinner(global::Android.Content.Context ctx, string[] items)
    {
        var adapter = new global::Android.Widget.ArrayAdapter<string>(
            ctx, global::Android.Resource.Layout.SimpleSpinnerItem, items);
        adapter.SetDropDownViewResource(global::Android.Resource.Layout.SimpleSpinnerDropDownItem);
        var spinner = new global::Android.Widget.Spinner(ctx);
        spinner.Adapter = adapter;
        return spinner;
    }

    /// <summary>创建结果显示用的 TextView（默认隐藏，绿色文字）</summary>
    /// <param name="ctx">Android Context</param>
    private static global::Android.Widget.TextView CreateResultTextView(global::Android.Content.Context ctx)
    {
        var tv = new global::Android.Widget.TextView(ctx);
        tv.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 12f);
        tv.SetTextColor(global::Android.Graphics.Color.ParseColor("#2D7A50"));
        tv.SetPadding(0, dpToPx(ctx, 4), 0, dpToPx(ctx, 4));
        tv.Visibility = global::Android.Views.ViewStates.Gone;
        return tv;
    }

    /// <summary>创建半透明白色分隔线 View</summary>
    /// <param name="ctx">Android Context</param>
    private static global::Android.Views.View CreateDivider(global::Android.Content.Context ctx)
    {
        var v = new global::Android.Views.View(ctx);
        v.LayoutParameters = new global::Android.Views.ViewGroup.LayoutParams(matchParent, 1);
        v.SetBackgroundColor(global::Android.Graphics.Color.ParseColor("#30FFFFFF"));
        v.SetPadding(0, dpToPx(ctx, 6), 0, dpToPx(ctx, 6));
        return v;
    }

    /// <summary>将 TextView 设置为红色错误文本样式</summary>
    /// <param name="tv">目标 TextView</param>
    /// <param name="text">错误消息文本（会自动添加 ❌ 前缀）</param>
    private static void SetErrorText(global::Android.Widget.TextView tv, string text)
    {
        tv.Text = $"❌ {text}";
        tv.SetTextColor(global::Android.Graphics.Color.ParseColor("#CC5555"));
        tv.Visibility = global::Android.Views.ViewStates.Visible;
    }

    /// <summary>格式化歌词搜索结果为可显示的文本（最多显示前 5 条）</summary>
    /// <param name="results">歌词搜索结果列表</param>
    /// <returns>格式化的文本字符串</returns>
    private static string FormatLyricsResults(List<LrcSearchResult> results)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < results.Count && i < 5; i++)
        {
            var r = results[i];
            var lineCount = r.Lyrics?.Lines.Count ?? 0;
            sb.AppendLine($"[{i + 1}] {r.Source} | {r.Artist} - {r.Title} ({lineCount}行)");
        }
        if (results.Count > 5) sb.AppendLine($"... 共{results.Count}个结果");
        return sb.ToString().TrimEnd();
    }

    /// <summary>格式化封面搜索结果为可显示的文本（最多显示前 5 条）</summary>
    /// <param name="results">封面搜索结果列表</param>
    /// <returns>格式化的文本字符串</returns>
    private static string FormatCoverResults(List<CoverSearchResult> results)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < results.Count && i < 5; i++)
        {
            var r = results[i];
            sb.AppendLine($"[{i + 1}] {r.Source} | {r.AlbumName} ({r.Width}x{r.Height})");
        }
        if (results.Count > 5) sb.AppendLine($"... 共{results.Count}个结果");
        return sb.ToString().TrimEnd();
    }

    /// <summary>在 UI 线程上显示 Toast 提示消息</summary>
    /// <param name="ctx">Android Context</param>
    /// <param name="msg">提示消息文本</param>
    /// <param name="activity">当前 Activity（用于切回 UI 线程）</param>
    private static void ShowToast(global::Android.Content.Context ctx, string msg, global::Android.App.Activity? activity)
    {
        if (activity != null)
            activity.RunOnUiThread(() => global::Android.Widget.Toast.MakeText(ctx, msg, global::Android.Widget.ToastLength.Short)?.Show());
        else
            global::Android.Widget.Toast.MakeText(ctx, msg, global::Android.Widget.ToastLength.Short)?.Show();
    }

    /// <summary>将 dp 单位转换为像素值</summary>
    /// <param name="ctx">Android Context（用于获取屏幕密度）</param>
    /// <param name="dp">dp 值</param>
    /// <returns>对应的像素值</returns>
    private static int dpToPx(global::Android.Content.Context ctx, int dp)
    {
        return (int)(dp * ctx.Resources!.DisplayMetrics!.Density);
    }

    /// <summary>安全地将字符串解析为 uint?（解析失败返回 null）</summary>
    /// <param name="s">待解析的字符串</param>
    /// <returns>解析成功返回 uint 值，否则返回 null</returns>
    private static uint? ParseUint(string? s)
    {
        if (uint.TryParse(s?.Trim(), out var v)) return v;
        return null;
    }

    /// <summary>将文件大小限制字符串（如 "500MB"、"1GB"、"不限"）解析为 MB 数值</summary>
    /// <param name="s">大小限制字符串</param>
    /// <returns>MB 数值；"不限" 返回 <see cref="long.MaxValue"/>；无法解析时返回 500</returns>
    private static long ParseSizeLimit(string s)
    {
        if (s == "不限") return long.MaxValue;
        var num = new string(s.Where(char.IsDigit).ToArray());
        if (long.TryParse(num, out var mb)) return mb;
        return 500;
    }

    /// <summary>通过反射从 Application 全局 Services 容器中获取指定类型的服务实例</summary>
    /// <typeparam name="T">服务类型</typeparam>
    /// <returns>服务实例；获取失败返回 null</returns>
    private static T? GetService<T>() where T : class
    {
        try
        {
            var appType = global::Android.App.Application.Context?.GetType();
            if (appType == null) return null;

            var servicesProp = appType.GetProperty("Services",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (servicesProp == null) return null;

            var services = servicesProp.GetValue(null);
            if (services == null) return null;

            var getServiceMethod = services.GetType().GetMethod("GetService",
                new[] { typeof(Type) });
            if (getServiceMethod == null) return null;

            return getServiceMethod.Invoke(services, new object[] { typeof(T) }) as T;
        }
        catch
        {
            return null;
        }
    }

    #endregion
}
