using IOFile = System.IO.File;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Core.Services;

/// <summary>M4A/MP4 手动 atom 树解析器 —— ALAC 探测与解码数据提取 partial 文件。</summary>
public static partial class M4aMetadataReader
{
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
}
