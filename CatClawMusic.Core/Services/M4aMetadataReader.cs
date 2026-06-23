using IOFile = System.IO.File;

namespace CatClawMusic.Core.Services;
/// M4A/MP4 手动 atom 树解析器。
/// 当 TagLibSharp 无法解析某些 m4a 文件时，用此类手动遍历 MP4 atom 提取封面、歌词、音频属性和元数据。
/// </summary>
public static class M4aMetadataReader
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
        System.Diagnostics.Debug.WriteLine("[M4aMeta] ReadAllFromStream: wantTags=true, wantLyrics=true");
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

    /// <summary>快速判断 m4a 文件是否为 ALAC 编码（扫描 moov/stsd 中的 alac 标记）</summary>
    public static bool IsAlac(string filePath)
    {
        try
        {
            if (!IOFile.Exists(filePath)) return false;
            using var fs = IOFile.OpenRead(filePath);
            return IsAlac(fs);
        }
        catch { return false; }
    }

    /// <summary>快速判断 m4a 流是否为 ALAC 编码</summary>
    public static bool IsAlac(Stream stream)
    {
        try
        {
            if (!stream.CanSeek) return false;
            var originalPos = stream.Position;
            try
            {
                stream.Position = 0;
                using var br = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
                return WalkTopLevelForAlac(br, stream.Length);
            }
            finally { stream.Position = originalPos; }
        }
        catch { return false; }
    }

    private static bool WalkTopLevelForAlac(BinaryReader br, long streamLength)
    {
        long pos = 0;
        while (pos < streamLength - 7)
        {
            br.BaseStream.Position = pos;
            var size = ReadUInt32BE(br);
            var type = ReadFourCC(br);
            long dataLen = size - 8;
            if (size == 1 && streamLength - pos >= 16)
                dataLen = (long)ReadUInt64BE(br) - 16;
            if (size == 0) dataLen = streamLength - pos - 8;
            long atomEnd = pos + 8 + dataLen;
            if (atomEnd > streamLength || size < 8 || dataLen < 0) break;

            if (type == "moov")
                if (WalkMoovForAlacFlag(br, pos + 8, dataLen)) return true;

            pos = atomEnd;
            if (size == 0) break;
        }
        return false;
    }

    private static bool WalkMoovForAlacFlag(BinaryReader br, long start, long length)
    {
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
            long atomEnd = pos + 8 + dataLen;
            if (atomEnd > end || size < 8 || dataLen < 0) break;

            if (type == "trak" && WalkTrakForAlacFlag(br, pos + 8, dataLen)) return true;

            pos = atomEnd;
            if (size == 0) break;
        }
        return false;
    }

    private static bool WalkTrakForAlacFlag(BinaryReader br, long start, long length)
    {
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
            long atomEnd = pos + 8 + dataLen;
            if (atomEnd > end || size < 8 || dataLen < 0) break;

            if (type == "mdia" && WalkMdiaForAlacFlag(br, pos + 8, dataLen)) return true;

            pos = atomEnd;
            if (size == 0) break;
        }
        return false;
    }

    private static bool WalkMdiaForAlacFlag(BinaryReader br, long start, long length)
    {
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
            long atomEnd = pos + 8 + dataLen;
            if (atomEnd > end || size < 8 || dataLen < 0) break;

            if (type == "minf" && WalkMinfForAlacFlag(br, pos + 8, dataLen)) return true;

            pos = atomEnd;
            if (size == 0) break;
        }
        return false;
    }

    private static bool WalkMinfForAlacFlag(BinaryReader br, long start, long length)
    {
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
            long atomEnd = pos + 8 + dataLen;
            if (atomEnd > end || size < 8 || dataLen < 0) break;

            if (type == "stbl" && WalkStblForAlacFlag(br, pos + 8, dataLen)) return true;

            pos = atomEnd;
            if (size == 0) break;
        }
        return false;
    }

    private static bool WalkStblForAlacFlag(BinaryReader br, long start, long length)
    {
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
            long atomEnd = pos + 8 + dataLen;
            if (atomEnd > end || size < 8 || dataLen < 0) break;

            if (type == "stsd" && dataLen > 16)
            {
                var stsdStart = pos + 8;
                var stsdDataStart = stsdStart + 8; // skip atom header + version/flags/entryCount
                if (stsdDataStart + 8 <= end)
                {
                    br.BaseStream.Position = stsdDataStart;
                    var entrySize = ReadUInt32BE(br);
                    var entryType = ReadFourCC(br);
                    if (entryType.Equals("alac", StringComparison.OrdinalIgnoreCase)) return true;

                    // 某些 ALAC 文件 stsd entry 类型为 mp4a，但其中包含 alac box
                    if (entrySize > 28 && stsdDataStart + entrySize <= end)
                    {
                        var entryEnd = stsdDataStart + entrySize;
                        var searchPos = stsdDataStart + 8 + 6 + 2 + 8 + 12; // 跳过常见音频 sample entry 字段
                        while (searchPos + 8 < entryEnd)
                        {
                            br.BaseStream.Position = searchPos;
                            var boxSize = ReadUInt32BE(br);
                            var boxType = ReadFourCC(br);
                            if (boxSize < 8 || searchPos + boxSize > entryEnd) break;
                            if (boxType.Equals("alac", StringComparison.OrdinalIgnoreCase)) return true;
                            searchPos += boxSize;
                        }
                    }
                }
            }

            pos = atomEnd;
            if (size == 0) break;
        }
        return false;
    }

    /// <summary>从 m4a 文件提取 ALAC 解码所需数据：magic cookie 和 mdat 音频数据</summary>
    public static (byte[]? magicCookie, byte[]? mdatData) ReadAlacData(string filePath)
    {
        using var fs = IOFile.OpenRead(filePath);
        return ReadAlacDataFromStream(fs);
    }

    /// <summary>从 m4a 流提取 ALAC 解码所需数据</summary>
    public static (byte[]? magicCookie, byte[]? mdatData) ReadAlacDataFromStream(Stream stream)
    {
        using var br = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        byte[]? magicCookie = null;
        byte[]? mdatData = null;
        long streamLen = stream.Length;

        WalkTopLevelExtended(br, streamLen, ref magicCookie, ref mdatData);
        return (magicCookie, mdatData);
    }

    private static void WalkTopLevelExtended(BinaryReader br, long streamLen, ref byte[]? magicCookie, ref byte[]? mdatData)
    {
        long pos = 0;
        long end = streamLen;
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

            if (type == "moov" && magicCookie == null)
            {
                WalkMoovForAlac(br, pos + 8, dataLen, ref magicCookie);
            }
            else if (type == "mdat" && mdatData == null)
            {
                if (pos + 8 + dataLen <= end)
                {
                    mdatData = new byte[dataLen];
                    br.BaseStream.Position = pos + 8;
                    br.Read(mdatData, 0, (int)dataLen);
                }
            }

            if (magicCookie != null && mdatData != null) break;
            pos += 8 + dataLen;
        }
    }

    private static void WalkMoovForAlac(BinaryReader br, long start, long length, ref byte[]? magicCookie)
    {
        long pos = start;
        long end = start + length;
        while (pos < end - 7 && magicCookie == null)
        {
            br.BaseStream.Position = pos;
            var size = ReadUInt32BE(br);
            var type = ReadFourCC(br);
            long dataLen = size - 8;
            if (size == 1 && end - pos >= 16) dataLen = (long)ReadUInt64BE(br) - 16;
            if (size == 0) dataLen = end - pos - 8;
            if (pos + size > end || size < 8) break;
            if (dataLen <= 0) { pos += size; continue; }

            if (type == "trak")
                WalkTrakForAlac(br, pos + 8, dataLen, ref magicCookie);

            pos += 8 + dataLen;
        }
    }

    private static void WalkTrakForAlac(BinaryReader br, long start, long length, ref byte[]? magicCookie)
    {
        long pos = start;
        long end = start + length;
        while (pos < end - 7 && magicCookie == null)
        {
            br.BaseStream.Position = pos;
            var size = ReadUInt32BE(br);
            var type = ReadFourCC(br);
            long dataLen = size - 8;
            if (size == 1 && end - pos >= 16) dataLen = (long)ReadUInt64BE(br) - 16;
            if (size == 0) dataLen = end - pos - 8;
            if (pos + size > end || size < 8) break;
            if (dataLen <= 0) { pos += size; continue; }

            if (type == "mdia")
                WalkMdiaForAlac(br, pos + 8, dataLen, ref magicCookie);

            pos += 8 + dataLen;
        }
    }

    private static void WalkMdiaForAlac(BinaryReader br, long start, long length, ref byte[]? magicCookie)
    {
        long pos = start;
        long end = start + length;
        while (pos < end - 7 && magicCookie == null)
        {
            br.BaseStream.Position = pos;
            var size = ReadUInt32BE(br);
            var type = ReadFourCC(br);
            long dataLen = size - 8;
            if (size == 1 && end - pos >= 16) dataLen = (long)ReadUInt64BE(br) - 16;
            if (size == 0) dataLen = end - pos - 8;
            if (pos + size > end || size < 8) break;
            if (dataLen <= 0) { pos += size; continue; }

            if (type == "minf")
                WalkMinfForAlac(br, pos + 8, dataLen, ref magicCookie);

            pos += 8 + dataLen;
        }
    }

    private static void WalkMinfForAlac(BinaryReader br, long start, long length, ref byte[]? magicCookie)
    {
        long pos = start;
        long end = start + length;
        while (pos < end - 7 && magicCookie == null)
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
                WalkStblForAlac(br, pos + 8, dataLen, ref magicCookie);

            pos += 8 + dataLen;
        }
    }

    private static void WalkStblForAlac(BinaryReader br, long start, long length, ref byte[]? magicCookie)
    {
        long pos = start;
        long end = start + length;
        while (pos < end - 7 && magicCookie == null)
        {
            br.BaseStream.Position = pos;
            var size = ReadUInt32BE(br);
            var type = ReadFourCC(br);
            long dataLen = size - 8;
            if (size == 1 && end - pos >= 16) dataLen = (long)ReadUInt64BE(br) - 16;
            if (size == 0) dataLen = end - pos - 8;
            if (pos + size > end || size < 8) break;
            if (dataLen <= 0) { pos += size; continue; }

            if (type == "stsd" && dataLen > 16)
            {
                var stsdStart = pos + 8 + 8;
                if (stsdStart + 8 <= end)
                {
                    br.BaseStream.Position = stsdStart;
                    var entrySize = ReadUInt32BE(br);
                    var entryType = ReadFourCC(br);
                    if (entryType == "alac" && entrySize > 28)
                    {
                        var entryEnd = stsdStart + (int)entrySize;
                        var searchPos = stsdStart + 8 + 6 + 2 + 8 + 12;
                        while (searchPos + 8 < entryEnd && magicCookie == null)
                        {
                            br.BaseStream.Position = searchPos;
                            var boxSize = ReadUInt32BE(br);
                            var boxType = ReadFourCC(br);
                            if (searchPos + boxSize > entryEnd || boxSize < 8) break;
                            if (boxType == "alac" && boxSize > 8)
                            {
                                magicCookie = new byte[boxSize - 8];
                                br.Read(magicCookie, 0, (int)(boxSize - 8));
                            }
                            else
                            {
                                searchPos += (int)boxSize;
                            }
                        }
                    }
                }
            }

            pos += 8 + dataLen;
        }
    }

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
        System.Diagnostics.Debug.WriteLineIf(wantTags, $"[M4aMeta] WalkIlst: wantTags=true, start={start}, len={length}");
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
                System.Diagnostics.Debug.WriteLine($"[M4aMeta] ilst: {itemType} pos={pos} size={itemSize}");
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
                                System.Diagnostics.Debug.WriteLine($"[M4aMeta] Found title: {title}");
                                break;
                            case "\u00a9ART" when wantTags:
                                artist = System.Text.Encoding.UTF8.GetString(data).TrimEnd('\0');
                                System.Diagnostics.Debug.WriteLine($"[M4aMeta] Found artist: {artist}");
                                break;
                            case "\u00a9alb" when wantTags:
                                album = System.Text.Encoding.UTF8.GetString(data).TrimEnd('\0');
                                System.Diagnostics.Debug.WriteLine($"[M4aMeta] Found album: {album}");
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

    private static uint ReadUInt32BE(BinaryReader br)
    {
        var bytes = br.ReadBytes(4);
        if (bytes.Length < 4) return 0;
        return (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);
    }

    private static ulong ReadUInt64BE(BinaryReader br)
    {
        var bytes = br.ReadBytes(8);
        if (bytes.Length < 8) return 0;
        return ((ulong)bytes[0] << 56) | ((ulong)bytes[1] << 48) |
               ((ulong)bytes[2] << 40) | ((ulong)bytes[3] << 32) |
               ((ulong)bytes[4] << 24) | ((ulong)bytes[5] << 16) |
               ((ulong)bytes[6] << 8) | bytes[7];
    }

    private static ushort ReadUInt16BE(BinaryReader br)
    {
        var bytes = br.ReadBytes(2);
        if (bytes.Length < 2) return 0;
        return (ushort)((bytes[0] << 8) | bytes[1]);
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

/// <summary>M4A 手动解析结果</summary>
public class M4aMetadata
{
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public string? Album { get; set; }
    public string? Lyrics { get; set; }
    public int DurationSeconds { get; set; }
    public int Bitrate { get; set; }
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public int BitDepth { get; set; }
    public string? Codec { get; set; }
}
