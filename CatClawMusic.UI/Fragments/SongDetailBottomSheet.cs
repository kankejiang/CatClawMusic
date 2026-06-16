using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;
using Google.Android.Material.BottomSheet;
using IOFile = System.IO.File;
using INavigationService = CatClawMusic.Core.Interfaces.INavigationService;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

/// <summary>歌曲详情底部弹窗（从任意页面底部弹出，下滑/返回关闭）</summary>
public class SongDetailBottomSheet : BottomSheetDialogFragment
{
    private ImageView _albumCover = null!;
    private ImageView _artistThumb = null!;
    private ImageView _albumThumb = null!;
    private TextView _songTitle = null!;
    private TextView _tvArtist = null!;
    private TextView _tvAlbum = null!;
    private TextView _tvDuration = null!;
    private TextView _tvYear = null!;
    private TextView _tvBitrate = null!;
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
            var songs = await db.GetSongsWithDetailsAsync();
            _song = songs.FirstOrDefault(s => s.Id == _songId);

            if (_song == null)
            {
                Activity?.RunOnUiThread(() =>
                {
                    _songTitle.Text = "歌曲不存在";
                    _tvLyrics.Text = "未找到歌曲信息";
                });
                return;
            }

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

            await Task.WhenAll(coverTask, thumbTask, lyricsTask);
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

    private async Task LoadLyricsAsync(MusicDatabase db)
    {
        if (_song == null) return;

        var lyricsService = MainApplication.Services.GetRequiredService<LyricsService>();

        try
        {
            // 内嵌歌词：使用 LyricsService 回退链（含 AndroidFileStreamOpener / ContentUriLyricsReader）
            var embeddedRaw = await Task.Run(() => LyricsService.ReadEmbeddedLyricsStatic(_song.FilePath));
            _embeddedLyrics = embeddedRaw ?? "";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SongDetail] 读取内置歌词失败: {ex.Message}");
        }

        try
        {
            // 外嵌歌词：优先数据库 → LyricsPath（含 content:// URI）→ LyricsService 查找 .lrc
            var lyric = await db.GetLyricAsync(_song.Id);
            if (lyric != null && !string.IsNullOrEmpty(lyric.Content))
            {
                _externalLyrics = lyric.Content;
            }
            else if (!string.IsNullOrEmpty(_song.LyricsPath))
            {
                if (_song.LyricsPath.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
                {
                    // SAF content:// URI：通过 ContentResolver 读取
                    var raw = await LyricsService.ReadContentUriLyricsAsync(_song.LyricsPath);
                    if (!string.IsNullOrEmpty(raw)) _externalLyrics = raw;
                }
                else if (IOFile.Exists(_song.LyricsPath))
                {
                    _externalLyrics = await Task.Run(() => IOFile.ReadAllText(_song.LyricsPath));
                }
                else if (LyricsService.FileBytesReaderAsync != null)
                {
                    // Android scoped storage 回退：通过 ContentResolver 读取
                    try
                    {
                        var bytes = await LyricsService.FileBytesReaderAsync(_song.LyricsPath);
                        if (bytes != null && bytes.Length > 0)
                            _externalLyrics = LyricsService.EncodingDetectAndDecode(bytes);
                    }
                    catch { }
                }
            }

            if (string.IsNullOrWhiteSpace(_externalLyrics))
            {
                // LyricsService.GetLocalLyricsAsync 内部 TryReadLrcFileAsync 已有 ContentResolver 回退
                var lrcLyrics = await lyricsService.GetLocalLyricsAsync(_song, skipEmbedded: true);
                if (lrcLyrics != null)
                    _externalLyrics = FormatLrcLyrics(lrcLyrics);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SongDetail] 读取外嵌歌词失败: {ex.Message}");
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

    /// <summary>将 LrcLyrics 格式化为可读文本</summary>
    private static string FormatLrcLyrics(LrcLyrics lyrics)
    {
        if (lyrics.Lines == null || lyrics.Lines.Count == 0) return "";
        return string.Join("\n", lyrics.Lines.Select(l => l.Text));
    }

    private void ShowEditDialog()
    {
        var ctx = Context;
        if (ctx == null || _song == null) return;

        var ll = new LinearLayout(ctx)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        ll.SetPadding(48, 24, 48, 16);

        var etTitle = CreateEditField(ctx, "歌曲标题", _song.Title);
        var etArtist = CreateEditField(ctx, "艺术家", _song.Artist);
        var etAlbum = CreateEditField(ctx, "专辑", _song.Album);
        ll.AddView(etTitle);
        ll.AddView(etArtist);
        ll.AddView(etAlbum);

        var dialog = new Google.Android.Material.Dialog.MaterialAlertDialogBuilder(ctx)
            .SetTitle("编辑歌曲信息")
            .SetView(ll)
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

        dialog?.Show();
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