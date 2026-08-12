using IOFile = System.IO.File;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Core.Services;
/// <summary>
/// M4A/MP4 手动 atom 树解析器。
/// 当 TagLibSharp 无法解析某些 m4a 文件时，用此类手动遍历 MP4 atom 提取封面、歌词、音频属性和元数据。
/// </summary>

public static partial class M4aMetadataReader
{
    /// <summary>从 m4a 文件提取封面图字节数组</summary>
    public static byte[]? ExtractCoverArt(string filePath)
    {
        using var fs = IOFile.OpenRead(filePath);
        return ExtractCoverFromStream(fs);
    }

    /// <summary>从 m4a 流中提取封面图字节数组</summary>
    public static byte[]? ExtractCoverFromStream(Stream stream)
    {
        using var br = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        return WalkTopLevel(br, stream.Length, wantCover: true).cover;
    }

    /// <summary>从 m4a 文件提取嵌入歌词</summary>
    public static string? ExtractLyrics(string filePath)
    {
        using var fs = IOFile.OpenRead(filePath);
        return ExtractLyricsFromStream(fs);
    }

    /// <summary>从 m4a 流中提取嵌入歌词</summary>
    public static string? ExtractLyricsFromStream(Stream stream)
    {
        using var br = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        return WalkTopLevel(br, stream.Length, wantLyrics: true).lyrics;
    }

    /// <summary>从 m4a 文件读取所有可用元数据（标签 + 音频属性）</summary>
    public static M4aMetadata? ReadAll(string filePath)
    {
        using var fs = IOFile.OpenRead(filePath);
        return ReadAllFromStream(fs);
    }

    /// <summary>从 m4a 流读取所有可用元数据</summary>
    public static M4aMetadata? ReadAllFromStream(Stream stream)
    {
        using var br = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        Log.Debug("M4aMetadataReader", "[M4aMeta] ReadAllFromStream: wantTags=true, wantLyrics=true");
        var result = WalkTopLevel(br, stream.Length, wantCover: false, wantLyrics: true, wantProperties: true, wantTags: true);
        return new M4aMetadata
        {
            Title = result.title,
            Artist = result.artist,
            Album = result.album,
            Lyrics = result.lyrics,
            DurationSeconds = result.durationSeconds,
            Bitrate = result.bitrate,
            SampleRate = result.sampleRate,
            Channels = result.channels,
            BitDepth = result.bitDepth,
            Codec = result.codec
        };
    }

    /// <summary>从 m4a 流中仅读取音频属性</summary>
    public static M4aMetadata? ReadAudioProperties(Stream stream)
    {
        using var br = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        var result = WalkTopLevel(br, stream.Length, wantProperties: true);
        return new M4aMetadata
        {
            DurationSeconds = result.durationSeconds,
            Bitrate = result.bitrate,
            SampleRate = result.sampleRate,
            Channels = result.channels,
            BitDepth = result.bitDepth,
            Codec = result.codec
        };
    }

    /// <summary>
    /// 从 m4a 文件尾部数据中解析元数据。
    /// 适用于 moov box 位于文件末尾的「非 faststart」M4A/MP4：网络补全只下载文件头（256KB/2MB）时
    /// TagLib 因找不到 moov 抛 CorruptFileException，需 Range 下载文件尾段后在此手动解析。
    /// 在尾部数据中搜索合法的 moov box（size 校验过滤 mdat 中的随机假 moov），解析标签与时长。
    /// </summary>
    /// <param name="tailData">文件尾部字节（Range 请求下载的最后一段）</param>
    /// <param name="fileSize">远程文件完整大小（用于重算码率，截断流长度算出的码率不准）</param>
    public static M4aMetadata? ReadAllFromTail(byte[] tailData, long fileSize)
    {
        foreach (var (start, size) in FindMoovCandidates(tailData))
        {
            try
            {
                using var ms = new MemoryStream(tailData, start, size, writable: false);
                using var br = new BinaryReader(ms, System.Text.Encoding.UTF8, leaveOpen: true);
                var r = WalkTopLevel(br, size, wantCover: false, wantLyrics: false, wantProperties: true, wantTags: true);
                if (r.title == null && r.artist == null && r.album == null && r.durationSeconds <= 0)
                    continue; // 假 moov（mdat 中的随机字节），尝试下一个候选
                return new M4aMetadata
                {
                    Title = r.title,
                    Artist = r.artist,
                    Album = r.album,
                    DurationSeconds = r.durationSeconds,
                    Bitrate = r.durationSeconds > 0 ? (int)(fileSize * 8 / r.durationSeconds / 1000) : r.bitrate,
                    SampleRate = r.sampleRate,
                    Channels = r.channels,
                    BitDepth = r.bitDepth,
                    Codec = r.codec
                };
            }
            catch { /* 无效候选，继续下一个 */ }
        }
        return null;
    }

    /// <summary>从 m4a 文件尾部数据中提取封面（moov 在末尾时 covr 也位于其中，可省去整首下载兜底）。</summary>
    public static byte[]? ExtractCoverFromTail(byte[] tailData)
    {
        foreach (var (start, size) in FindMoovCandidates(tailData))
        {
            try
            {
                using var ms = new MemoryStream(tailData, start, size, writable: false);
                using var br = new BinaryReader(ms, System.Text.Encoding.UTF8, leaveOpen: true);
                var r = WalkTopLevel(br, size, wantCover: true);
                if (r.cover != null) return r.cover;
            }
            catch { }
        }
        return null;
    }

    /// <summary>在尾部数据中搜索合法的 moov box 候选（8 字节对齐：size(4B) + "moov"）。</summary>
    private static IEnumerable<(int start, int size)> FindMoovCandidates(byte[] data)
    {
        if (data == null || data.Length < 12) yield break;
        for (int i = 4; i <= data.Length - 8; i++)
        {
            if (data[i] != (byte)'m' || data[i + 1] != (byte)'o' ||
                data[i + 2] != (byte)'o' || data[i + 3] != (byte)'v')
                continue;
            var size = ReadUInt32BE(data, i - 4);
            if (size == 0)
            {
                // size=0 表示 box 延伸到流末尾（截断尾部内）
                yield return (i - 4, data.Length - (i - 4));
            }
            else if (size >= 8 && i - 4 + (long)size <= data.Length)
            {
                yield return (i - 4, (int)size);
            }
            // size==1（64 位扩展大小）极少用于 moov，此处不处理
        }
    }

    private static uint ReadUInt32BE(byte[] data, int offset)
    {
        if (offset < 0 || offset + 4 > data.Length) return 0;
        return (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
    }

    /// <summary>快速判断 m4a 文件是否为 ALAC 编码（扫描 moov/stsd 中的 alac 标记）</summary>

    // ──── 内部实现 ────

    private static (byte[]? cover, string? lyrics, string? title, string? artist, string? album,
        int durationSeconds, int bitrate, int sampleRate, int channels, int bitDepth, string? codec)
        WalkTopLevel(BinaryReader br, long streamLength,
            bool wantCover = false, bool wantLyrics = false, bool wantProperties = false, bool wantTags = false)
    {
        byte[]? cover = null;
        string? lyrics = null;
        string? title = null, artist = null, album = null;
        long timescale = 0, duration = 0;
        int sampleRate = 0, channels = 0, bitDepth = 0;
        string? codec = null;

        long pos = 0;
        while (pos < streamLength - 7)
        {
            br.BaseStream.Position = pos;
            var size = ReadUInt32BE(br);
            var type = ReadFourCC(br);
            long dataLen = size - 8;

            if (size == 1 && streamLength - pos >= 16) // extended size
            {
                dataLen = (long)ReadUInt64BE(br) - 16;
            }
            if (size == 0) dataLen = streamLength - pos - 8; // rest of file
            if (pos + size > streamLength || size < 8) break;
            if (dataLen <= 0) { pos += size; continue; }

            switch (type)
            {
                case "moov":
                    var moovResult = WalkMoov(br, pos + 8, dataLen, wantCover, wantLyrics, wantProperties, wantTags);
                    cover ??= moovResult.cover;
                    lyrics ??= moovResult.lyrics;
                    title ??= moovResult.title;
                    artist ??= moovResult.artist;
                    album ??= moovResult.album;
                    if (moovResult.timescale > 0) timescale = moovResult.timescale;
                    if (moovResult.duration > 0) duration = moovResult.duration;
                    if (moovResult.sampleRate > 0) sampleRate = moovResult.sampleRate;
                    if (moovResult.channels > 0) channels = moovResult.channels;
                    if (moovResult.bitDepth > 0) bitDepth = moovResult.bitDepth;
                    codec ??= moovResult.codec;
                    break;
            }

            pos += 8 + dataLen;
            if (size == 0) break;
        }

        int durationSec = 0;
        int bitrateVal = 0;
        if (timescale > 0 && duration > 0)
        {
            durationSec = (int)(duration / timescale);
            if (durationSec > 0)
                bitrateVal = (int)(streamLength * 8 / durationSec / 1000);
        }

        return (cover, lyrics, title, artist, album, durationSec, bitrateVal, sampleRate, channels, bitDepth, codec);
    }

    private static (byte[]? cover, string? lyrics, string? title, string? artist, string? album,
        long timescale, long duration, int sampleRate, int channels, int bitDepth, string? codec)
        WalkMoov(BinaryReader br, long start, long length,
            bool wantCover, bool wantLyrics, bool wantProperties, bool wantTags)
    {
        byte[]? cover = null;
        string? lyrics = null;
        string? title = null, artist = null, album = null;
        long timescale = 0, duration = 0;
        int sampleRate = 0, channels = 0, bitDepth = 0;
        string? codec = null;

        long pos = start;
        long end = start + length;
        while (pos < end - 7)
        {
            br.BaseStream.Position = pos;
            var size = ReadUInt32BE(br);
            var type = ReadFourCC(br);
            long dataLen = size - 8;
            if (size == 1 && end - pos >= 16) dataLen = (long)ReadUInt64BE(br) - 16;
            if (size == 0) dataLen = end - pos - 8;
            if (pos + size > end || size < 8) break;
            if (dataLen <= 0) { pos += size; continue; }

            switch (type)
            {
                case "mvhd":
                    if (wantProperties)
                    {
                        var mvhdStart = pos + 8;
                        var version = br.ReadByte();
                        if (version == 0)
                        {
                            br.BaseStream.Position = mvhdStart + 12; // skip version+flags+creationTime+modificationTime
                            timescale = ReadUInt32BE(br);
                            duration = ReadUInt32BE(br);
                        }
                        else if (version == 1)
                        {
                            br.BaseStream.Position = mvhdStart + 20; // skip version+flags+creationTime(8)+modificationTime(8)
                            timescale = ReadUInt32BE(br);
                            duration = (long)ReadUInt64BE(br);
                        }
                    }
                    break;

                case "trak":
                    var trakResult = WalkTrak(br, pos + 8, dataLen, wantProperties);
                    if (wantProperties && trakResult.timescale > 0)
                    {
                        timescale = trakResult.timescale;
                        duration = trakResult.duration;
                    }
                    if (trakResult.sampleRate > 0) sampleRate = trakResult.sampleRate;
                    if (trakResult.channels > 0) channels = trakResult.channels;
                    if (trakResult.bitDepth > 0) bitDepth = trakResult.bitDepth;
                    codec ??= trakResult.codec;
                    break;

                case "udta":
                    var udtaResult = WalkUdta(br, pos + 8, dataLen, wantCover, wantLyrics, wantTags);
                    cover ??= udtaResult.cover;
                    lyrics ??= udtaResult.lyrics;
                    title ??= udtaResult.title;
                    artist ??= udtaResult.artist;
                    album ??= udtaResult.album;
                    break;
            }

            pos += 8 + dataLen;
            if (size == 0) break;
        }

        return (cover, lyrics, title, artist, album, timescale, duration, sampleRate, channels, bitDepth, codec);
    }

    private static (long timescale, long duration, int sampleRate, int channels, int bitDepth, string? codec)
        WalkTrak(BinaryReader br, long start, long length, bool wantProperties)
    {
        long timescale = 0, duration = 0;
        int sampleRate = 0, channels = 0, bitDepth = 0;
        string? codec = null;

        long pos = start;
        long end = start + length;
        while (pos < end - 7)
        {
            br.BaseStream.Position = pos;
            var size = ReadUInt32BE(br);
            var type = ReadFourCC(br);
            long dataLen = size - 8;
            if (size == 1 && end - pos >= 16) dataLen = (long)ReadUInt64BE(br) - 16;
            if (size == 0) dataLen = end - pos - 8;
            if (pos + size > end || size < 8) break;
            if (dataLen <= 0) { pos += size; continue; }

            switch (type)
            {
                case "mdia":
                    var mdiaResult = WalkMdia(br, pos + 8, dataLen, wantProperties);
                    if (mdiaResult.timescale > 0) timescale = mdiaResult.timescale;
                    if (mdiaResult.duration > 0) duration = mdiaResult.duration;
                    if (mdiaResult.sampleRate > 0) sampleRate = mdiaResult.sampleRate;
                    if (mdiaResult.channels > 0) channels = mdiaResult.channels;
                    if (mdiaResult.bitDepth > 0) bitDepth = mdiaResult.bitDepth;
                    codec ??= mdiaResult.codec;
                    break;
            }

            pos += 8 + dataLen;
            if (size == 0) break;
        }

        return (timescale, duration, sampleRate, channels, bitDepth, codec);
    }

    private static (long timescale, long duration, int sampleRate, int channels, int bitDepth, string? codec)
        WalkMdia(BinaryReader br, long start, long length, bool wantProperties)
    {
        long timescale = 0, duration = 0;
        int sampleRate = 0, channels = 0, bitDepth = 0;
        string? codec = null;

        long pos = start;
        long end = start + length;
        while (pos < end - 7)
        {
            br.BaseStream.Position = pos;
            var size = ReadUInt32BE(br);
            var type = ReadFourCC(br);
            long dataLen = size - 8;
            if (size == 1 && end - pos >= 16) dataLen = (long)ReadUInt64BE(br) - 16;
            if (size == 0) dataLen = end - pos - 8;
            if (pos + size > end || size < 8) break;
            if (dataLen <= 0) { pos += size; continue; }

            switch (type)
            {
                case "mdhd":
                    if (wantProperties)
                    {
                        var mdhdStart = pos + 8;
                        var version = br.ReadByte();
                        if (version == 0)
                        {
                            br.BaseStream.Position = mdhdStart + 12;
                            timescale = ReadUInt32BE(br);
                            duration = ReadUInt32BE(br);
                        }
                        else if (version == 1)
                        {
                            br.BaseStream.Position = mdhdStart + 20;
                            timescale = ReadUInt32BE(br);
                            duration = (long)ReadUInt64BE(br);
                        }
                    }
                    break;

                case "minf":
                    var minfResult = WalkMinf(br, pos + 8, dataLen, wantProperties);
                    if (minfResult.sampleRate > 0) sampleRate = minfResult.sampleRate;
                    if (minfResult.channels > 0) channels = minfResult.channels;
                    if (minfResult.bitDepth > 0) bitDepth = minfResult.bitDepth;
                    codec ??= minfResult.codec;
                    break;
            }

            pos += 8 + dataLen;
            if (size == 0) break;
        }

        return (timescale, duration, sampleRate, channels, bitDepth, codec);
    }

    private static (int sampleRate, int channels, int bitDepth, string? codec)
        WalkMinf(BinaryReader br, long start, long length, bool wantProperties)
    {
        int sampleRate = 0, channels = 0, bitDepth = 0;
        string? codec = null;

        long pos = start;
        long end = start + length;
        while (pos < end - 7)
        {
            br.BaseStream.Position = pos;
            var size = ReadUInt32BE(br);
            var type = ReadFourCC(br);
            long dataLen = size - 8;
            if (size == 1 && end - pos >= 16) dataLen = (long)ReadUInt64BE(br) - 16;
            if (size == 0) dataLen = end - pos - 8;
            if (pos + size > end || size < 8) break;
            if (dataLen <= 0) { pos += size; continue; }

            if (type == "stbl")
            {
                var stblResult = WalkStbl(br, pos + 8, dataLen, wantProperties);
                if (stblResult.sampleRate > 0) sampleRate = stblResult.sampleRate;
                if (stblResult.channels > 0) channels = stblResult.channels;
                if (stblResult.bitDepth > 0) bitDepth = stblResult.bitDepth;
                codec ??= stblResult.codec;
            }

            pos += 8 + dataLen;
            if (size == 0) break;
        }

        return (sampleRate, channels, bitDepth, codec);
    }

    private static (int sampleRate, int channels, int bitDepth, string? codec)
        WalkStbl(BinaryReader br, long start, long length, bool wantProperties)
    {
        int sampleRate = 0, channels = 0, bitDepth = 0;
        string? codec = null;

        long pos = start;
        long end = start + length;
        while (pos < end - 7)
        {
            br.BaseStream.Position = pos;
            var size = ReadUInt32BE(br);
            var type = ReadFourCC(br);
            long dataLen = size - 8;
            if (size == 1 && end - pos >= 16) dataLen = (long)ReadUInt64BE(br) - 16;
            if (size == 0) dataLen = end - pos - 8;
            if (pos + size > end || size < 8) break;
            if (dataLen <= 0) { pos += size; continue; }

            if (type == "stsd" && wantProperties)
            {
                // stsd: version(1) + flags(3) + entryCount(4) = 8 bytes header, then entries
                var stsdDataStart = pos + 8 + 8; // skip atom header + version/flags/entryCount
                if (stsdDataStart + 8 <= end)
                {
                    br.BaseStream.Position = stsdDataStart;
                    var entrySize = ReadUInt32BE(br);
                    var entryType = ReadFourCC(br);
                    codec = MapCodecName(entryType);

                    // Audio sample entry common fields:
                    // reserved(6) + dataRefIndex(2) + reserved2(8) + channels(2) + sampleSize(2) + compressionId(2) + packetSize(2) + sampleRate(4, 16.16 fixed)
                    var entryStart = stsdDataStart;
                    var audioFieldStart = entryStart + 8 + 6 + 2 + 8; // after reserved+dataRefIndex+reserved2
                    if (audioFieldStart + 12 <= entryStart + entrySize)
                    {
                        br.BaseStream.Position = audioFieldStart;
                        var ch = ReadUInt16BE(br);
                        var ss = ReadUInt16BE(br);
                        br.ReadBytes(4); // skip compressionId + packetSize
                        var srRaw = ReadUInt32BE(br);
                        channels = ch;
                        bitDepth = ss;
                        sampleRate = (int)(srRaw >> 16);
                    }
                }
            }

            pos += 8 + dataLen;
            if (size == 0) break;
        }

        return (sampleRate, channels, bitDepth, codec);
    }

    private static (byte[]? cover, string? lyrics, string? title, string? artist, string? album)
        WalkUdta(BinaryReader br, long start, long length, bool wantCover, bool wantLyrics, bool wantTags)
    {
        byte[]? cover = null;
        string? lyrics = null;
        string? title = null, artist = null, album = null;

        long pos = start;
        long end = start + length;
        while (pos < end - 7)
        {
            br.BaseStream.Position = pos;
            var size = ReadUInt32BE(br);
            var type = ReadFourCC(br);
            long dataLen = size - 8;
            if (size == 1 && end - pos >= 16) dataLen = (long)ReadUInt64BE(br) - 16;
            if (size == 0) dataLen = end - pos - 8;
            if (pos + size > end || size < 8) break;
            if (dataLen <= 0) { pos += size; continue; }

            if (type == "meta" && (wantCover || wantLyrics || wantTags))
            {
                var metaResult = WalkMeta(br, pos + 8, dataLen, wantCover, wantLyrics, wantTags);
                cover ??= metaResult.cover;
                lyrics ??= metaResult.lyrics;
                title ??= metaResult.title;
                artist ??= metaResult.artist;
                album ??= metaResult.album;
            }

            pos += 8 + dataLen;
            if (size == 0) break;
        }

        return (cover, lyrics, title, artist, album);
    }

    private static (byte[]? cover, string? lyrics, string? title, string? artist, string? album)
        WalkMeta(BinaryReader br, long start, long length, bool wantCover, bool wantLyrics, bool wantTags)
    {
        // meta is a full atom: 4 bytes version/flags before children
        long childrenStart = start + 4;
        long end = start + length;

        // Detect if first 4 bytes are version/flags or a child atom
        // If version/flags, first byte should be 0 and next 3 bytes are flags (usually 0)
        br.BaseStream.Position = start;
        var b0 = br.ReadByte();
        var b1 = br.ReadByte();
        var b2 = br.ReadByte();
        var b3 = br.ReadByte();

        // Heuristic: if b0 == 0 and b1-b3 are small, it's likely version/flags
        if (b0 == 0 && b1 == 0 && b2 == 0 && (b3 == 0 || b3 == 1))
        {
            // version/flags detected, children start at start+4
        }
        else
        {
            // No version/flags, children start at beginning of meta data
            childrenStart = start;
        }

        return WalkIlstContainer(br, childrenStart, end - childrenStart, wantCover, wantLyrics, wantTags);
    }

    private static (byte[]? cover, string? lyrics, string? title, string? artist, string? album)
        WalkIlstContainer(BinaryReader br, long start, long length, bool wantCover, bool wantLyrics, bool wantTags)
    {
        byte[]? cover = null;
        string? lyrics = null;
        string? title = null, artist = null, album = null;

        long pos = start;
        long end = start + length;
        while (pos < end - 7)
        {
            br.BaseStream.Position = pos;
            var size = ReadUInt32BE(br);
            var type = ReadFourCC(br);
            long dataLen = size - 8;
            if (pos + size > end || size < 8) break;
            if (dataLen <= 0) { pos += size; continue; }

            if (type == "ilst")
            {
                var ilstResult = WalkIlst(br, pos + 8, dataLen, wantCover, wantLyrics, wantTags);
                cover ??= ilstResult.cover;
                lyrics ??= ilstResult.lyrics;
                title ??= ilstResult.title;
                artist ??= ilstResult.artist;
                album ??= ilstResult.album;
            }

            pos += 8 + dataLen;
            if (size == 0) break;
        }

        return (cover, lyrics, title, artist, album);
    }

    private static (byte[]? cover, string? lyrics, string? title, string? artist, string? album)
        WalkIlst(BinaryReader br, long start, long length, bool wantCover, bool wantLyrics, bool wantTags)
    {
        if (wantTags) Log.Debug("M4aMetadataReader", $"[M4aMeta] WalkIlst: wantTags=true, start={start}, len={length}");
        byte[]? cover = null;
        string? lyrics = null;
        string? title = null, artist = null, album = null;

        long pos = start;
        long end = start + length;
        while (pos < end - 7)
        {
            br.BaseStream.Position = pos;
            var itemSize = ReadUInt32BE(br);
            var itemType = ReadFourCC(br);
            if (wantTags && (itemType[0] == '\u00a9' || itemType == "covr"))
                Log.Debug("M4aMetadataReader", $"[M4aMeta] ilst: {itemType} pos={pos} size={itemSize}");
            long itemDataLen = itemSize - 8;
            if (pos + itemSize > end || itemSize < 8) break;
            if (itemDataLen <= 0) { pos += itemSize; continue; }

            bool needed = (wantCover && itemType == "covr") ||
                          (wantLyrics && itemType == "\u00a9lyr") ||
                          (wantTags && itemType == "\u00a9nam") ||
                          (wantTags && itemType == "\u00a9ART") ||
                          (wantTags && itemType == "\u00a9alb");

            if (needed)
            {
                // ilst item contains a 'data' atom: size(4) + 'data'(4) + typeIndicator(4) + locale(4) + actualData
                long dataAtomPos = pos + 8;
                br.BaseStream.Position = dataAtomPos;
                var dataAtomSize = ReadUInt32BE(br);
                var dataAtomType = ReadFourCC(br);

                if (dataAtomType == "data" && dataAtomSize > 16)
                {
                    br.ReadBytes(4); // type indicator (e.g. 13=UTF8, 14=JPEG, 13=PNG for covr)
                    br.ReadBytes(4); // locale
                    var actualDataLen = (int)(dataAtomSize - 16);
                    if (actualDataLen > 0 && actualDataLen <= itemDataLen - 8)
                    {
                        var data = br.ReadBytes(actualDataLen);
                        switch (itemType)
                        {
                            case "covr" when wantCover:
                                cover = data;
                                break;
                            case "\u00a9lyr" when wantLyrics:
                                lyrics = System.Text.Encoding.UTF8.GetString(data).TrimEnd('\0');
                                break;
                            case "\u00a9nam" when wantTags:
                                title = System.Text.Encoding.UTF8.GetString(data).TrimEnd('\0');
                                Log.Debug("M4aMetadataReader", $"[M4aMeta] Found title: {title}");
                                break;
                            case "\u00a9ART" when wantTags:
                                artist = System.Text.Encoding.UTF8.GetString(data).TrimEnd('\0');
                                Log.Debug("M4aMetadataReader", $"[M4aMeta] Found artist: {artist}");
                                break;
                            case "\u00a9alb" when wantTags:
                                album = System.Text.Encoding.UTF8.GetString(data).TrimEnd('\0');
                                Log.Debug("M4aMetadataReader", $"[M4aMeta] Found album: {album}");
                                break;
                        }
                    }
                }
            }

            pos += 8 + itemDataLen;
            if (itemSize == 0) break;
        }

        return (cover, lyrics, title, artist, album);
    }

    // ──── 辅助方法 ────

    // 大端读取实现：BinaryReader 有内部缓冲，必须走 br.ReadUInt32/ReadUInt16 再反转字节序，
    // 避免 ReadBytes 每次分配 byte[4]/[8]（大 atom 树上千次分配）且不破坏缓冲一致性。
    // EOF 用 BaseStream 长度预检（文件流/MemoryStream 均支持），不足时返回 0 与原行为一致。

    private static uint ReadUInt32BE(BinaryReader br)
    {
        if (br.BaseStream.Length - br.BaseStream.Position < 4) return 0;
        var v = br.ReadUInt32(); // 小端组装
        // 反转字节序 → 大端值
        return (v & 0x000000FFu) << 24 | (v & 0x0000FF00u) << 8 | (v & 0x00FF0000u) >> 8 | (v & 0xFF000000u) >> 24;
    }

    private static ulong ReadUInt64BE(BinaryReader br)
    {
        if (br.BaseStream.Length - br.BaseStream.Position < 8) return 0;
        var v = br.ReadUInt64(); // 小端组装
        // 反转字节序 → 大端值
        return (v & 0x00000000000000FFUL) << 56 | (v & 0x000000000000FF00UL) << 40 |
               (v & 0x0000000000FF0000UL) << 24 | (v & 0x00000000FF000000UL) << 8 |
               (v & 0x000000FF00000000UL) >> 8 | (v & 0x0000FF0000000000UL) >> 24 |
               (v & 0x00FF000000000000UL) >> 40 | (v & 0xFF00000000000000UL) >> 56;
    }

    private static ushort ReadUInt16BE(BinaryReader br)
    {
        if (br.BaseStream.Length - br.BaseStream.Position < 2) return 0;
        var v = br.ReadUInt16();
        return (ushort)(v << 8 | v >> 8);
    }

    private static string ReadFourCC(BinaryReader br)
    {
        var bytes = br.ReadBytes(4);
        if (bytes.Length < 4) return "";
        // Use Latin-1 which maps bytes 0-255 directly to Unicode, preserving © (0xA9)
        return System.Text.Encoding.Latin1.GetString(bytes);
    }

    private static string MapCodecName(string fourCC)
    {
        return fourCC switch
        {
            "mp4a" => "AAC",
            "alac" => "ALAC",
            "fLaC" => "FLAC",
            "Opus" => "Opus",
            "ac-3" => "AC-3",
            "ec-3" => "E-AC-3",
            _ => fourCC
        };
    }
}
