using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;
using CatClawMusic.UI.Helpers;
using Google.Android.Material.BottomSheet;
using IOFile = System.IO.File;
using INavigationService = CatClawMusic.Core.Interfaces.INavigationService;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

/// <summary>歌曲详情底部弹窗（从任意页面底部弹出，下滑/返回关闭）</summary>
public class SongDetailBottomSheet : BottomSheetDialogFragment
{
    private static readonly Dictionary<string, string> SourceLabels = new() {
        ["netease"] = "网易云", ["qq"] = "QQ音乐", ["kugou"] = "酷狗",
        ["soda"] = "汽水", ["apple"] = "Apple"
    };

    private ImageView _albumCover = null!;
    private ImageView _artistThumb = null!;
    private ImageView _albumThumb = null!;
    private TextView _songTitle = null!;
    private TextView _tvArtist = null!;
    private TextView _tvAlbum = null!;
    private TextView _tvDuration = null!;
    private TextView _tvYear = null!;
    private TextView _tvBitrate = null!;
    private TextView _tvSampleRate = null!;
    private TextView _tvChannels = null!;
    private TextView _tvBitDepth = null!;
    private TextView _tvCodec = null!;
    private TextView _tvFormat = null!;
    private TextView _tvFileSize = null!;
    private TextView _tvFilePath = null!;
    private TextView _tvLyrics = null!;
    private RadioGroup _rgLyricSource = null!;
    private RadioButton _rbEmbedded = null!;
    private RadioButton _rbExternal = null!;
    private LinearLayout _rowArtist = null!;
    private LinearLayout _rowAlbum = null!;

    private INavigationService _navigationService = null!;
    private Song? _song;
    private int _songId;
    private string _embeddedLyrics = "";
    private string _externalLyrics = "";

    public static SongDetailBottomSheet NewInstance(int songId)
    {
        var fragment = new SongDetailBottomSheet();
        var args = new Bundle();
        args.PutInt("songId", songId);
        fragment.Arguments = args;
        return fragment;
    }

    public override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _songId = Arguments?.GetInt("songId", 0) ?? 0;
    }

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
        => inflater.Inflate(Resource.Layout.fragment_song_detail, container, false)!;

    public override void OnViewCreated(View view, Bundle? savedInstanceState)
    {
        base.OnViewCreated(view, savedInstanceState);

        _navigationService = MainApplication.Services.GetRequiredService<INavigationService>();

        _albumCover = view.FindViewById<ImageView>(Resource.Id.iv_album_cover)!;
        _artistThumb = view.FindViewById<ImageView>(Resource.Id.iv_artist_thumb)!;
        _albumThumb = view.FindViewById<ImageView>(Resource.Id.iv_album_thumb)!;
        _songTitle = view.FindViewById<TextView>(Resource.Id.tv_song_title)!;
        _tvArtist = view.FindViewById<TextView>(Resource.Id.tv_artist)!;
        _tvAlbum = view.FindViewById<TextView>(Resource.Id.tv_album)!;
        _tvDuration = view.FindViewById<TextView>(Resource.Id.tv_duration)!;
        _tvYear = view.FindViewById<TextView>(Resource.Id.tv_year)!;
        _tvBitrate = view.FindViewById<TextView>(Resource.Id.tv_bitrate)!;
        _tvSampleRate = view.FindViewById<TextView>(Resource.Id.tv_sample_rate)!;
        _tvChannels = view.FindViewById<TextView>(Resource.Id.tv_channels)!;
        _tvBitDepth = view.FindViewById<TextView>(Resource.Id.tv_bit_depth)!;
        _tvCodec = view.FindViewById<TextView>(Resource.Id.tv_codec)!;
        _tvFormat = view.FindViewById<TextView>(Resource.Id.tv_format)!;
        _tvFileSize = view.FindViewById<TextView>(Resource.Id.tv_file_size)!;
        _tvFilePath = view.FindViewById<TextView>(Resource.Id.tv_file_path)!;
        _tvLyrics = view.FindViewById<TextView>(Resource.Id.tv_lyrics)!;
        _rgLyricSource = view.FindViewById<RadioGroup>(Resource.Id.rg_lyric_source)!;
        _rbEmbedded = view.FindViewById<RadioButton>(Resource.Id.rb_embedded)!;
        _rbExternal = view.FindViewById<RadioButton>(Resource.Id.rb_external)!;
        _rowArtist = view.FindViewById<LinearLayout>(Resource.Id.row_artist)!;
        _rowAlbum = view.FindViewById<LinearLayout>(Resource.Id.row_album)!;

        var btnEdit = view.FindViewById<ImageButton>(Resource.Id.btn_edit)!;
        btnEdit.Click += (s, e) => ShowEditDialog();

        _rowArtist.Click += (s, e) =>
        {
            if (_song != null && !string.IsNullOrEmpty(_song.Artist))
            {
                Dismiss();
                _navigationService.PushFragment("ArtistDetail",
                    new Dictionary<string, object> { ["artistName"] = _song.Artist });
            }
        };

        _rowAlbum.Click += (s, e) =>
        {
            if (_song != null && !string.IsNullOrEmpty(_song.Album))
            {
                Dismiss();
                _navigationService.PushFragment("AlbumDetail",
                    new Dictionary<string, object>
                    {
                        ["albumTitle"] = _song.Album,
                        ["albumArtist"] = _song.Artist
                    });
            }
        };

        _rgLyricSource.CheckedChange += (s, e) =>
        {
            if (e.CheckedId == Resource.Id.rb_embedded)
                _tvLyrics.Text = string.IsNullOrEmpty(_embeddedLyrics) ? "无内置歌词" : _embeddedLyrics;
            else if (e.CheckedId == Resource.Id.rb_external)
                _tvLyrics.Text = string.IsNullOrEmpty(_externalLyrics) ? "无外嵌歌词" : _externalLyrics;
        };

        _ = LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        if (_songId <= 0) return;

        var db = MainApplication.Services.GetRequiredService<MusicDatabase>();
        await db.EnsureInitializedAsync();

        try
        {
            _song = await db.GetSongByIdAsync(_songId);

            if (_song == null)
            {
                Activity?.RunOnUiThread(() =>
                {
                    _songTitle.Text = "歌曲不存在";
                    _tvLyrics.Text = "未找到歌曲信息";
                });
                return;
            }

            // 填充艺术家/专辑名称（GetSongByIdAsync 不预加载这些运行时字段）
            if (_song.ArtistId > 0)
            {
                var artist = await db.FindArtistByIdAsync(_song.ArtistId);
                if (!string.IsNullOrEmpty(artist?.Name))
                    _song.Artist = artist.Name;
            }
            if (_song.AlbumId > 0)
            {
                var album = await db.FindAlbumByIdAsync(_song.AlbumId);
                if (!string.IsNullOrEmpty(album?.Title))
                    _song.Album = album.Title;
            }
            if (string.IsNullOrEmpty(_song.Artist)) _song.Artist = "未知艺术家";
            if (string.IsNullOrEmpty(_song.Album)) _song.Album = "未知专辑";

            var durationSec = _song.Duration;

            Activity?.RunOnUiThread(() =>
            {
                _songTitle.Text = _song.Title;
                _tvArtist.Text = _song.Artist;
                _tvAlbum.Text = _song.Album;
                _tvDuration.Text = durationSec > 0
                    ? TimeSpan.FromSeconds(durationSec).ToString(durationSec >= 3600 ? @"h\:mm\:ss" : @"mm\:ss")
                    : "未知";
                _tvYear.Text = _song.Year > 0 ? _song.Year.ToString() : "未知";
                _tvBitrate.Text = _song.Bitrate > 0 ? $"{_song.Bitrate} kbps" : "未知";
                _tvFileSize.Text = FormatFileSize(_song.FileSize);
                _tvFilePath.Text = _song.FilePath ?? "未知";
            });

            var coverTask = LoadCoverAsync();
            var thumbTask = LoadThumbnailsAsync(db);
            var lyricsTask = LoadLyricsAsync(db);
            var audioPropsTask = LoadAudioPropertiesAsync();

            await Task.WhenAll(coverTask, thumbTask, lyricsTask, audioPropsTask);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SongDetail] 加载失败: {ex}");
            Activity?.RunOnUiThread(() =>
            {
                _tvLyrics.Text = "加载失败";
            });
        }
    }

    private async Task LoadCoverAsync()
    {
        if (_song == null) return;

        try
        {
            if (_song.Source == SongSource.Local && _song.MediaStoreId > 0)
            {
                var bitmap = await Task.Run(() =>
                    Platforms.Android.MediaStoreCoverHelper.LoadCoverFromMediaStore(
                        _song.MediaStoreId, 480));
                if (bitmap != null)
                {
                    Activity?.RunOnUiThread(() =>
                    {
                        _albumCover.SetImageBitmap(bitmap);
                        _albumThumb.SetImageBitmap(bitmap);
                    });
                    return;
                }
            }

            if (!string.IsNullOrEmpty(_song.CoverArtPath) && IOFile.Exists(_song.CoverArtPath))
            {
                var bitmap = await Task.Run(() => BitmapFactory.DecodeFile(_song.CoverArtPath));
                if (bitmap != null)
                {
                    Activity?.RunOnUiThread(() =>
                    {
                        _albumCover.SetImageBitmap(bitmap);
                        _albumThumb.SetImageBitmap(bitmap);
                    });
                    return;
                }
            }

            var cachePath = System.IO.Path.Combine(
                global::Android.App.Application.Context.CacheDir!.AbsolutePath,
                "covers", $"cover_{_song.Id}.jpg");
            if (IOFile.Exists(cachePath))
            {
                var bitmap = await Task.Run(() => BitmapFactory.DecodeFile(cachePath));
                if (bitmap != null)
                {
                    Activity?.RunOnUiThread(() =>
                    {
                        _albumCover.SetImageBitmap(bitmap);
                        _albumThumb.SetImageBitmap(bitmap);
                    });
                    return;
                }
            }

            if (!string.IsNullOrEmpty(_song.FilePath)
                && !_song.FilePath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                && !_song.FilePath.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
            {
                var coverBytes = await Task.Run(() => TagReader.ExtractCoverArt(_song.FilePath));
                if (coverBytes is { Length: > 0 })
                {
                    var bitmap = await Task.Run(() =>
                        BitmapFactory.DecodeByteArray(coverBytes, 0, coverBytes.Length));
                    if (bitmap != null)
                    {
                        Activity?.RunOnUiThread(() =>
                        {
                            _albumCover.SetImageBitmap(bitmap);
                            _albumThumb.SetImageBitmap(bitmap);
                        });
                        return;
                    }
                }
            }

            if (!string.IsNullOrEmpty(_song.FilePath)
                && _song.FilePath.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var ctx = global::Android.App.Application.Context;
                    var uri = Android.Net.Uri.Parse(_song.FilePath);
                    if (uri != null)
                    {
                        using var stream = ctx.ContentResolver!.OpenInputStream(uri);
                        if (stream != null)
                        {
                            var abstraction = new ReadOnlyFileAbstraction(_song.FilePath, stream);
                            using var file = TagLib.File.Create(abstraction);
                            if (file.Tag.Pictures is { Length: > 0 })
                            {
                                var pic = file.Tag.Pictures[0].Data.Data;
                                var bitmap = await Task.Run(() =>
                                    BitmapFactory.DecodeByteArray(pic, 0, pic.Length));
                                if (bitmap != null)
                                {
                                    Activity?.RunOnUiThread(() =>
                                    {
                                        _albumCover.SetImageBitmap(bitmap);
                                        _albumThumb.SetImageBitmap(bitmap);
                                    });
                                }
                            }
                        }
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    private async Task LoadThumbnailsAsync(MusicDatabase db)
    {
        if (_song == null) return;

        try
        {
            var artists = await db.GetAllArtistsAsync();
            var artist = artists.FirstOrDefault(a => a.Name == _song.Artist);
            if (artist != null && !string.IsNullOrEmpty(artist.Cover))
            {
                var coverPath = artist.Cover;
                if (IOFile.Exists(coverPath))
                {
                    var bitmap = await Task.Run(() => BitmapFactory.DecodeFile(coverPath));
                    if (bitmap != null)
                    {
                        Activity?.RunOnUiThread(() =>
                        {
                            _artistThumb.SetImageBitmap(bitmap);
                            _artistThumb.ImageTintList = null;
                        });
                    }
                }
            }
        }
        catch { }
    }

    private async Task LoadAudioPropertiesAsync()
    {
        if (_song == null) return;

        try
        {
            TagLib.Properties? props = null;
            string? fileExtension = null;

            if (!string.IsNullOrEmpty(_song.FilePath))
            {
                fileExtension = System.IO.Path.GetExtension(_song.FilePath).TrimStart('.').ToUpperInvariant();
                if (fileExtension == "")
                    fileExtension = null;
            }

            if (!string.IsNullOrEmpty(_song.FilePath) && _song.FilePath.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
            {
                var ctx = global::Android.App.Application.Context;
                var uri = Android.Net.Uri.Parse(_song.FilePath);
                if (uri != null)
                {
                    using var stream = ctx.ContentResolver!.OpenInputStream(uri);
                    if (stream != null)
                    {
                        var abstraction = new ReadOnlyFileAbstraction(System.IO.Path.GetFileName(_song.FilePath) ?? "audio", stream);
                        using var file = TagLib.File.Create(abstraction);
                        props = file.Properties;
                        fileExtension ??= GetExtensionFromMimeType(ctx.ContentResolver!.GetType(uri));
                    }
                }
            }
            else if (!string.IsNullOrEmpty(_song.FilePath) && IOFile.Exists(_song.FilePath))
            {
                using var file = TagLib.File.Create(_song.FilePath);
                props = file.Properties;
            }

            if (props == null) return;

            var sampleRate = props.AudioSampleRate;
            var channels = props.AudioChannels;
            var bitDepth = props.BitsPerSample;
            var codec = props.Codecs.FirstOrDefault();

            Activity?.RunOnUiThread(() =>
            {
                _tvSampleRate.Text = sampleRate > 0 ? FormatSampleRate(sampleRate) : "未知";
                _tvChannels.Text = channels > 0 ? FormatChannels(channels) : "未知";
                _tvBitDepth.Text = bitDepth > 0 ? $"{bitDepth} bit" : "未知";
                _tvCodec.Text = !string.IsNullOrWhiteSpace(GetCodecDescription(codec, fileExtension))
                    ? GetCodecDescription(codec, fileExtension)
                    : (fileExtension ?? "未知");
                _tvFormat.Text = !string.IsNullOrWhiteSpace(fileExtension) ? fileExtension : "未知";
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SongDetail] 读取音频属性失败: {ex.Message}");
        }
    }

    private static string FormatSampleRate(int sampleRate)
    {
        if (sampleRate >= 1000)
            return $"{sampleRate / 1000.0:F1} kHz";
        return $"{sampleRate} Hz";
    }

    private static string FormatChannels(int channels)
    {
        return channels switch
        {
            1 => "单声道 (Mono)",
            2 => "立体声 (Stereo)",
            6 => "5.1 声道",
            8 => "7.1 声道",
            _ => $"{channels} 声道"
        };
    }

    private static string? GetExtensionFromMimeType(string? mimeType)
    {
        if (string.IsNullOrWhiteSpace(mimeType)) return null;
        var lowered = mimeType.ToLowerInvariant();
        return lowered switch
        {
            "audio/mpeg" => "MP3",
            "audio/flac" => "FLAC",
            "audio/wav" => "WAV",
            "audio/x-wav" => "WAV",
            "audio/aac" => "AAC",
            "audio/mp4" => "M4A",
            "audio/x-m4a" => "M4A",
            "audio/ogg" => "OGG",
            "audio/opus" => "OPUS",
            "audio/x-ms-wma" => "WMA",
            _ => null
        };
    }

    private static string? GetCodecDescription(TagLib.ICodec? codec, string? fileExtension)
    {
        if (codec != null && !string.IsNullOrWhiteSpace(codec.Description))
        {
            var desc = codec.Description;
            // 简化常见编码描述
            if (desc.Contains("FLAC", StringComparison.OrdinalIgnoreCase)) return "FLAC";
            if (desc.Contains("MPEG", StringComparison.OrdinalIgnoreCase)) return "MP3";
            if (desc.Contains("AAC", StringComparison.OrdinalIgnoreCase)) return "AAC";
            if (desc.Contains("ALAC", StringComparison.OrdinalIgnoreCase)) return "ALAC";
            if (desc.Contains("Vorbis", StringComparison.OrdinalIgnoreCase)) return "Vorbis";
            if (desc.Contains("Opus", StringComparison.OrdinalIgnoreCase)) return "Opus";
            return desc;
        }
        return fileExtension;
    }

    private async Task LoadLyricsAsync(MusicDatabase db)
    {
        if (_song == null) return;

        var lyricsService = MainApplication.Services.GetRequiredService<LyricsService>();

        // ── 内嵌歌词 ──
        try
        {
            var embeddedRaw = await Task.Run(() => LyricsService.ReadEmbeddedLyricsStatic(_song.FilePath));
            if (!string.IsNullOrWhiteSpace(embeddedRaw))
            {
                var parsed = await Task.Run(() => lyricsService.TryParseLyrics(embeddedRaw));
                _embeddedLyrics = parsed != null ? LyricsFormatter.FormatLrcLyrics(parsed) : "";
            }
            else
            {
                _embeddedLyrics = "";
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SongDetail] 读取内置歌词失败: {ex.Message}");
            _embeddedLyrics = "";
        }

        // ── 外嵌歌词 ──
        try
        {
            LrcLyrics? parsedExternal = null;

            // 路径1：数据库获取
            var lyric = await db.GetLyricAsync(_song.Id);
            if (lyric != null && !string.IsNullOrEmpty(lyric.Content))
            {
                parsedExternal = await Task.Run(() => lyricsService.TryParseLyrics(lyric.Content));
            }

            // 路径2：LyricsPath 文件
            if (parsedExternal == null && !string.IsNullOrEmpty(_song.LyricsPath))
            {
                string? raw = null;
                if (_song.LyricsPath.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
                {
                    raw = await LyricsService.ReadContentUriLyricsAsync(_song.LyricsPath);
                }
                else if (IOFile.Exists(_song.LyricsPath))
                {
                    raw = await Task.Run(() => IOFile.ReadAllText(_song.LyricsPath));
                }
                else if (LyricsService.FileBytesReaderAsync != null)
                {
                    try
                    {
                        var bytes = await LyricsService.FileBytesReaderAsync(_song.LyricsPath);
                        if (bytes != null && bytes.Length > 0)
                            raw = LyricsService.EncodingDetectAndDecode(bytes);
                    }
                    catch { }
                }

                if (!string.IsNullOrWhiteSpace(raw))
                {
                    parsedExternal = await Task.Run(() => lyricsService.TryParseLyrics(raw));
                }
            }

            // 路径3：LyricsService 自动查找外部歌词文件
            if (parsedExternal == null)
            {
                parsedExternal = await lyricsService.GetLocalLyricsAsync(_song, skipEmbedded: true);
            }

            // 路径4：FindExternalLyricsTextAsync（content:// URI 专项回退）
            if (parsedExternal == null)
            {
                var raw = await lyricsService.FindExternalLyricsTextAsync(_song);
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    parsedExternal = await Task.Run(() => lyricsService.TryParseLyrics(raw));
                }
            }

            _externalLyrics = parsedExternal != null ? LyricsFormatter.FormatLrcLyrics(parsedExternal) : "";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SongDetail] 读取外嵌歌词失败: {ex.Message}");
            _externalLyrics = "";
        }

        bool hasEmbedded = !string.IsNullOrWhiteSpace(_embeddedLyrics);
        bool hasExternal = !string.IsNullOrWhiteSpace(_externalLyrics);

        Activity?.RunOnUiThread(() =>
        {
            _rbEmbedded.Enabled = true;
            _rbExternal.Enabled = true;

            if (hasEmbedded && hasExternal)
            {
                _rbExternal.Checked = true;
                _tvLyrics.Text = _externalLyrics;
            }
            else if (hasEmbedded)
            {
                _rbEmbedded.Checked = true;
                _tvLyrics.Text = _embeddedLyrics;
            }
            else if (hasExternal)
            {
                _rbExternal.Checked = true;
                _tvLyrics.Text = _externalLyrics;
            }
            else
            {
                _rbEmbedded.Checked = true;
                _tvLyrics.Text = "暂无歌词";
            }
        });
    }

    private void ShowEditDialog()
    {
        var ctx = Context;
        if (ctx == null || _song == null) return;

        var scrollView = new ScrollView(ctx);
        var ll = new LinearLayout(ctx)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        ll.SetPadding(48, 24, 48, 16);

        // ── 元数据编辑 ──
        var tvSection1 = new TextView(ctx)
        {
            Text = "📝 元数据",
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        tvSection1.SetTextSize(Android.Util.ComplexUnitType.Sp, 16f);
        tvSection1.SetTextColor(Android.Graphics.Color.ParseColor("#FF8C00"));
        tvSection1.SetPadding(0, 0, 0, 12);
        ll.AddView(tvSection1);

        var etTitle = CreateEditField(ctx, "歌曲标题", _song.Title);
        var etArtist = CreateEditField(ctx, "艺术家", _song.Artist);
        var etAlbum = CreateEditField(ctx, "专辑", _song.Album);
        ll.AddView(etTitle);
        ll.AddView(etArtist);
        ll.AddView(etAlbum);

        // ── 分隔线 ──
        ll.AddView(CreateSectionDivider(ctx));

        // ── 歌词搜索 ──
        var tvSection2 = new TextView(ctx)
        {
            Text = "🎵 在线歌词搜索",
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        tvSection2.SetTextSize(Android.Util.ComplexUnitType.Sp, 16f);
        tvSection2.SetTextColor(Android.Graphics.Color.ParseColor("#4CAF50"));
        tvSection2.SetPadding(0, 16, 0, 8);
        ll.AddView(tvSection2);

        var tvLyricHint = new TextView(ctx)
        {
            Text = $"搜索\"{_song.Artist} - {_song.Title}\"的在线歌词",
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        tvLyricHint.SetTextSize(Android.Util.ComplexUnitType.Sp, 13f);
        tvLyricHint.SetTextColor(Android.Graphics.Color.Gray);
        tvLyricHint.SetPadding(0, 0, 0, 8);
        ll.AddView(tvLyricHint);

        var resultsListLyric = new LinearLayout(ctx)
        {
            Orientation = Orientation.Vertical,
            Visibility = ViewStates.Gone
        };
        ll.AddView(resultsListLyric);

        var btnSearchLyric = new Android.Widget.Button(ctx)
        {
            Text = "🔍 搜索歌词 (网易云/QQ/酷狗/汽水/Apple)",
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        btnSearchLyric.SetAllCaps(false);
        ll.AddView(btnSearchLyric);

        // ── 分隔线 ──
        ll.AddView(CreateSectionDivider(ctx));

        // ── 封面搜索 ──
        var tvSection3 = new TextView(ctx)
        {
            Text = "🖼️ 在线封面搜索",
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        tvSection3.SetTextSize(Android.Util.ComplexUnitType.Sp, 16f);
        tvSection3.SetTextColor(Android.Graphics.Color.ParseColor("#E91E63"));
        tvSection3.SetPadding(0, 16, 0, 8);
        ll.AddView(tvSection3);

        var resultsListCover = new LinearLayout(ctx)
        {
            Orientation = Orientation.Vertical,
            Visibility = ViewStates.Gone
        };
        ll.AddView(resultsListCover);

        var btnSearchCover = new Android.Widget.Button(ctx)
        {
            Text = "🔍 搜索封面",
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        btnSearchCover.SetAllCaps(false);
        ll.AddView(btnSearchCover);

        scrollView.AddView(ll);

        var dialog = new Google.Android.Material.Dialog.MaterialAlertDialogBuilder(ctx)
            .SetTitle("编辑歌曲信息")
            .SetView(scrollView)
            .SetPositiveButton("保存", async (s, e) =>
            {
                var newTitle = etTitle.Text?.Trim();
                var newArtist = etArtist.Text?.Trim();
                var newAlbum = etAlbum.Text?.Trim();

                if (string.IsNullOrEmpty(newTitle))
                {
                    Toast.MakeText(ctx, "标题不能为空", ToastLength.Short)?.Show();
                    return;
                }

                try
                {
                    var db = MainApplication.Services.GetRequiredService<MusicDatabase>();
                    await db.EnsureInitializedAsync();

                    _song!.Title = newTitle;
                    _song.Artist = newArtist ?? "";
                    _song.Album = newAlbum ?? "";

                    await db.SaveSongAsync(_song);

                    Activity?.RunOnUiThread(() =>
                    {
                        _songTitle.Text = _song.Title;
                        _tvArtist.Text = _song.Artist;
                        _tvAlbum.Text = _song.Album;
                        Toast.MakeText(ctx, "已保存", ToastLength.Short)?.Show();
                    });
                }
                catch
                {
                    Activity?.RunOnUiThread(() =>
                        Toast.MakeText(ctx, "保存失败", ToastLength.Short)?.Show());
                }
            })
            .SetNegativeButton("取消", (s, e) => { })
            .Create();

        // Wire up search buttons
        btnSearchLyric.Click += async (s, e) =>
        {
            btnSearchLyric.Enabled = false;
            btnSearchLyric.Text = "搜索中...";
            await SearchLyricsInDialogAsync(ctx, resultsListLyric, resultsListCover);
            btnSearchLyric.Text = "🔍 重新搜索歌词";
            btnSearchLyric.Enabled = true;
        };

        btnSearchCover.Click += async (s, e) =>
        {
            btnSearchCover.Enabled = false;
            btnSearchCover.Text = "搜索中...";
            await SearchCoversInDialogAsync(ctx, resultsListCover, resultsListLyric);
            btnSearchCover.Text = "🔍 重新搜索封面";
            btnSearchCover.Enabled = true;
        };

        dialog?.Show();
    }

    private static View CreateSectionDivider(Android.Content.Context ctx)
    {
        var divider = new View(ctx)
        {
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, 1)
        };
        divider.SetBackgroundColor(Android.Graphics.Color.ParseColor("#33FFFFFF"));
        return divider;
    }

    private async Task SearchLyricsInDialogAsync(Android.Content.Context ctx,
        LinearLayout resultsList, LinearLayout otherResultsList)
    {
        if (_song == null) return;
        resultsList.RemoveAllViews();
        otherResultsList.RemoveAllViews();
        resultsList.Visibility = ViewStates.Gone;

        var keyword = $"{_song.Artist} {_song.Title}".Trim();
        if (string.IsNullOrWhiteSpace(keyword)) return;

        var searchService = MainApplication.Services.GetRequiredService<MultiSourceSearchService>();
        var results = await Task.Run(() => searchService.SearchAllAsync(keyword));

        if (results.Count == 0)
        {
            var tv = new TextView(ctx)
            {
                Text = "未找到在线歌词",
                LayoutParameters = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
            };
            tv.SetTextSize(Android.Util.ComplexUnitType.Sp, 13f);
            tv.SetTextColor(Android.Graphics.Color.Gray);
            tv.SetPadding(0, 8, 0, 8);
            resultsList.AddView(tv);
            resultsList.Visibility = ViewStates.Visible;
            return;
        }

        var sourceLabels = SourceLabels;

        foreach (var r in results.Take(12))
        {
            var sourceTag = sourceLabels.GetValueOrDefault(r.Source, r.Source);
            var itemText = $"[{sourceTag}] {r.Title}  —  {r.Artist}";
            var btn = new Android.Widget.Button(ctx)
            {
                Text = itemText,
                LayoutParameters = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
                { TopMargin = 4 }
            };
            btn.SetAllCaps(false);
            btn.Gravity = Android.Views.GravityFlags.Start;
            btn.SetTextSize(Android.Util.ComplexUnitType.Sp, 12f);
            btn.SetPadding(12, 8, 12, 8);

            var captured = r;
            btn.Click += async (s2, e2) =>
            {
                btn.Enabled = false;
                btn.Text = "获取中...";
                await FetchAndSaveLyricFromDialogAsync(ctx, captured, searchService);
                btn.Text = "✓ 已保存";
            };

            resultsList.AddView(btn);
        }
        resultsList.Visibility = ViewStates.Visible;
    }

    private async Task FetchAndSaveLyricFromDialogAsync(Android.Content.Context ctx,
        SearchResultItem item, MultiSourceSearchService searchService)
    {
        if (_song == null) return;
        try
        {
            var result = await Task.Run(() => searchService.FetchLyricAsync(item));
            if (result == null || string.IsNullOrWhiteSpace(result.LrcContent))
            {
                Activity?.RunOnUiThread(() =>
                    Toast.MakeText(ctx, "该源歌词不可用", ToastLength.Short)?.Show());
                return;
            }

            var rawLrc = result.LrcContent;
            if (!string.IsNullOrWhiteSpace(result.TlyricContent))
                rawLrc = MergeLrcWithTranslation(rawLrc, result.TlyricContent);

            var lyricsService = MainApplication.Services.GetRequiredService<LyricsService>();
            var parsed = await Task.Run(() => lyricsService.TryParseLyrics(rawLrc));
            if (parsed == null)
            {
                Activity?.RunOnUiThread(() =>
                    Toast.MakeText(ctx, "歌词解析失败", ToastLength.Short)?.Show());
                return;
            }

            var db = MainApplication.Services.GetRequiredService<MusicDatabase>();
            await db.EnsureInitializedAsync();
            await db.SaveLyricAsync(_song.Id, null, rawLrc);

            var formatted = LyricsFormatter.FormatLrcLyrics(parsed);
            _externalLyrics = formatted;

            Activity?.RunOnUiThread(() =>
            {
                _rbExternal.Enabled = true;
                _rbExternal.Checked = true;
                _tvLyrics.Text = formatted;
                var sourceLabel = new Dictionary<string, string> {
                    ["netease"]="网易云",["qq"]="QQ音乐",["kugou"]="酷狗",["soda"]="汽水",["apple"]="Apple"
                }.GetValueOrDefault(item.Source, item.Source);
                Toast.MakeText(ctx, $"已保存 {sourceLabel} 歌词", ToastLength.Short)?.Show();
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SongDetail] FetchLyric: {ex}");
            Activity?.RunOnUiThread(() =>
                Toast.MakeText(ctx, "获取失败", ToastLength.Short)?.Show());
        }
    }

    private async Task SearchCoversInDialogAsync(Android.Content.Context ctx,
        LinearLayout resultsList, LinearLayout otherResultsList)
    {
        if (_song == null) return;
        resultsList.RemoveAllViews();
        otherResultsList.RemoveAllViews();
        resultsList.Visibility = ViewStates.Gone;

        var keyword = $"{_song.Artist} {_song.Title}".Trim();
        if (string.IsNullOrWhiteSpace(keyword)) return;

        var searchService = MainApplication.Services.GetRequiredService<MultiSourceSearchService>();
        var results = await Task.Run(() => searchService.SearchAllAsync(keyword));

        // Filter those with cover URLs
        var withCovers = results.Where(r => !string.IsNullOrWhiteSpace(r.CoverUrl)).Take(8).ToList();
        if (withCovers.Count == 0)
        {
            var tv = new TextView(ctx)
            {
                Text = "未找到封面图片",
                LayoutParameters = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
            };
            tv.SetTextSize(Android.Util.ComplexUnitType.Sp, 13f);
            tv.SetTextColor(Android.Graphics.Color.Gray);
            tv.SetPadding(0, 8, 0, 8);
            resultsList.AddView(tv);
            resultsList.Visibility = ViewStates.Visible;
            return;
        }

        var sourceLabels = new Dictionary<string, string> {
            ["netease"] = "网易云", ["qq"] = "QQ音乐", ["kugou"] = "酷狗",
            ["soda"] = "汽水", ["apple"] = "Apple"
        };

        foreach (var r in withCovers)
        {
            var sourceTag = sourceLabels.GetValueOrDefault(r.Source, r.Source);
            var itemText = $"[{sourceTag}]{(!string.IsNullOrWhiteSpace(r.Album) ? " " + r.Album : "")}";
            var btn = new Android.Widget.Button(ctx)
            {
                Text = itemText,
                LayoutParameters = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
                { TopMargin = 4 }
            };
            btn.SetAllCaps(false);
            btn.Gravity = Android.Views.GravityFlags.Start;
            btn.SetTextSize(Android.Util.ComplexUnitType.Sp, 12f);
            btn.SetPadding(12, 8, 12, 8);

            var captured = r;
            btn.Click += async (s2, e2) =>
            {
                btn.Enabled = false;
                btn.Text = "下载中...";
                await DownloadAndSaveCoverAsync(ctx, captured);
                btn.Text = "✓ 已设置";
            };

            resultsList.AddView(btn);
        }
        resultsList.Visibility = ViewStates.Visible;
    }

    private async Task DownloadAndSaveCoverAsync(Android.Content.Context ctx, SearchResultItem item)
    {
        if (_song == null || string.IsNullOrWhiteSpace(item.CoverUrl)) return;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var bytes = await http.GetByteArrayAsync(item.CoverUrl);
            if (bytes == null || bytes.Length < 1024) return;

            // Save to cover directory
            var coverDir = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal),
                "covers");
            System.IO.Directory.CreateDirectory(coverDir);
            var coverPath = System.IO.Path.Combine(coverDir, $"cover_{_song.Id}.jpg");
            await System.IO.File.WriteAllBytesAsync(coverPath, bytes);

            // Update song
            _song.CoverArtPath = coverPath;
            var db = MainApplication.Services.GetRequiredService<MusicDatabase>();
            await db.EnsureInitializedAsync();
            await db.SaveSongAsync(_song);

            Activity?.RunOnUiThread(() =>
            {
                Toast.MakeText(ctx, "封面已更新", ToastLength.Short)?.Show();
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SongDetail] DownloadCover: {ex}");
            Activity?.RunOnUiThread(() =>
                Toast.MakeText(ctx, "封面下载失败", ToastLength.Short)?.Show());
        }
    }

    /// <summary>将 LRC 原文与翻译合并</summary>
    private static string MergeLrcWithTranslation(string lrc, string tlyric)
    {
        if (string.IsNullOrWhiteSpace(tlyric)) return lrc;
        var lrcLines = lrc.Split('\n');
        var tlyricLines = tlyric.Split('\n');
        var tlyricDict = new Dictionary<string, string>();
        foreach (var line in tlyricLines)
        {
            var match = System.Text.RegularExpressions.Regex.Match(line.Trim(),
                @"^\[(\d+:\d{2}\.\d+)\](.+)$");
            if (match.Success)
                tlyricDict[match.Groups[1].Value] = match.Groups[2].Value.Trim();
        }
        var merged = new System.Text.StringBuilder();
        foreach (var line in lrcLines)
        {
            var trimmed = line.Trim();
            merged.AppendLine(trimmed);
            var match = System.Text.RegularExpressions.Regex.Match(trimmed,
                @"^\[(\d+:\d{2}\.\d+)\](.+)$");
            if (match.Success && tlyricDict.TryGetValue(match.Groups[1].Value, out var trans))
                merged.AppendLine(trans);
        }
        return merged.ToString();
    }

    private static EditText CreateEditField(Android.Content.Context ctx, string hint, string text)
    {
        var et = new EditText(ctx)
        {
            Hint = hint,
            Text = text,
            InputType = Android.Text.InputTypes.TextFlagCapSentences
        };
        et.SetTextSize(Android.Util.ComplexUnitType.Sp, 14f);
        var lp = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        lp.BottomMargin = 16;
        et.LayoutParameters = lp;
        return et;
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes <= 0) return "未知";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }
}