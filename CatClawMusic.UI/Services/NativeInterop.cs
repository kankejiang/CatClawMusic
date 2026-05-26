using System;
using System.Runtime.InteropServices;

namespace CatClawMusic.UI.Services;

/// <summary>
/// C++ 原生库 P/Invoke 桥接层
///
/// 提供 FFT 频谱分析、LRC 歌词解析、音频标签读取的 C++ 原生实现接口。
/// 原生库编译为 libcatclaw_native.so，通过 DllImport 调用。
/// </summary>
public static class NativeInterop
{
    private const string DllName = "catclaw_native";

    private static bool? _isAvailable;

    /// <summary>
    /// 检查原生库是否可用（已加载）
    /// 首次调用时尝试加载，失败则标记为不可用，后续调用直接返回 false
    /// </summary>
    public static bool IsAvailable
    {
        get
        {
            if (_isAvailable == null)
            {
                try
                {
                    var version = catclaw_get_version();
                    _isAvailable = version > 0;
                }
                catch
                {
                    _isAvailable = false;
                }
            }
            return _isAvailable.Value;
        }
    }

    /* ============================================================
     * 原生库初始化
     * ============================================================ */

    /// <summary>获取原生库版本号（100 = 1.0.0）</summary>
    [DllImport(DllName, EntryPoint = "catclaw_get_version")]
    private static extern int catclaw_get_version();

    /// <summary>初始化原生库（CPU 特性检测等）</summary>
    [DllImport(DllName, EntryPoint = "catclaw_init")]
    public static extern void Init();

    /* ============================================================
     * FFT 频谱分析
     * ============================================================ */

    /// <summary>
    /// 对 PCM 音频数据执行 FFT 并计算频谱条形图
    /// </summary>
    /// <param name="pcmData">输入 PCM 采样数据（float，-1.0~1.0）</param>
    /// <param name="dataLen">PCM 数据长度（必须是 2 的幂）</param>
    /// <param name="barCount">输出频谱条数</param>
    /// <param name="bars">输出频谱条数组（长度 barCount）</param>
    /// <param name="minFreq">最低频率（Hz），通常 20</param>
    /// <param name="maxFreq">最高频率（Hz），通常 20000</param>
    /// <param name="sampleRate">采样率（Hz），通常 44100</param>
    [DllImport(DllName, EntryPoint = "catclaw_fft_compute_bars")]
    public static extern void FftComputeBars(
        float[] pcmData, int dataLen, int barCount, float[] bars,
        float minFreq, float maxFreq, int sampleRate);

    /// <summary>
    /// 计算 PCM 数据的 RMS 响度
    /// </summary>
    /// <param name="pcmData">输入 PCM 采样数据</param>
    /// <param name="dataLen">数据长度</param>
    /// <returns>RMS 值（0.0~1.0）</returns>
    [DllImport(DllName, EntryPoint = "catclaw_compute_rms")]
    public static extern float ComputeRms(float[] pcmData, int dataLen);

    /* ============================================================
     * 编码检测与转换
     * ============================================================ */

    /// <summary>
    /// 编码类型枚举，与 C++ 端 catclaw_detect_encoding 返回值对应
    /// </summary>
    public enum TextEncoding
    {
        Unknown = 0,
        Utf8Bom = 1,
        Utf8 = 2,
        Gbk = 3,
       Gb2312 = 4,
        ShiftJis = 5
    }

    /// <summary>
    /// 检测字节流的文本编码
    /// </summary>
    /// <param name="data">原始字节数据</param>
    /// <param name="dataLen">数据长度</param>
    /// <returns>编码类型</returns>
    [DllImport(DllName, EntryPoint = "catclaw_detect_encoding")]
    public static extern TextEncoding DetectEncoding(byte[] data, int dataLen);

    /// <summary>
    /// 将字节数据从指定编码转换为 UTF-8
    /// </summary>
    /// <param name="srcData">源字节数据</param>
    /// <param name="srcLen">源数据长度</param>
    /// <param name="encoding">编码类型</param>
    /// <param name="outUtf8">输出 UTF-8 缓冲区</param>
    /// <param name="outLen">输出缓冲区大小（字节），返回实际写入长度</param>
    /// <returns>0 成功，-1 失败</returns>
    [DllImport(DllName, EntryPoint = "catclaw_convert_to_utf8")]
    public static extern int ConvertToUtf8(
        byte[] srcData, int srcLen, TextEncoding encoding,
        byte[] outUtf8, ref int outLen);

    /* ============================================================
     * LRC 歌词解析
     * ============================================================ */

    /// <summary>
    /// 原生歌词行结构，与 C++ CatClawLyricLine 对应
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct NativeLyricLine
    {
        /// <summary>时间戳（毫秒）</summary>
        public int TimeMs;
        /// <summary>逐字歌词词数，0 表示普通行歌词</summary>
        public int WordCount;
        /// <summary>逐字歌词各词时间指针（IntPtr，指向原生内存）</summary>
        public IntPtr WordTimes;
        /// <summary>歌词文本指针（IntPtr，指向原生内存，UTF-8）</summary>
        public IntPtr Text;
    }

    /// <summary>
    /// 原生歌词解析结果，与 C++ CatClawLyricResult 对应
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct NativeLyricResult
    {
        /// <summary>歌词行数组指针</summary>
        public IntPtr Lines;
        /// <summary>歌词行数</summary>
        public int LineCount;
        /// <summary>内部分配容量</summary>
        public int Capacity;
        /// <summary>内部文本缓冲区指针</summary>
        public IntPtr TextBuffer;
        /// <summary>文本缓冲区长度</summary>
        public int TextBufferLen;
        /// <summary>逐字时间缓冲区指针</summary>
        public IntPtr WordTimeBuffer;
        /// <summary>逐字时间总数</summary>
        public int WordTimeCount;
    }

    /// <summary>
    /// 解析 LRC 歌词文本（UTF-8）
    /// </summary>
    /// <param name="lrcText">LRC 歌词文本（UTF-8 编码）</param>
    /// <param name="textLen">文本长度</param>
    /// <returns>解析结果指针（需要调用 LyricFree 释放）</returns>
    [DllImport(DllName, EntryPoint = "catclaw_parse_lrc")]
    public static extern IntPtr ParseLrc(byte[] lrcText, int textLen);

    /// <summary>
    /// 释放歌词解析结果
    /// </summary>
    /// <param name="result">解析结果指针</param>
    [DllImport(DllName, EntryPoint = "catclaw_lyric_free")]
    public static extern void LyricFree(IntPtr result);

    /// <summary>
    /// 二分查找当前歌词行索引
    /// </summary>
    /// <param name="result">解析结果指针</param>
    /// <param name="timeMs">当前播放位置（毫秒）</param>
    /// <returns>当前歌词行索引，-1 表示无匹配</returns>
    [DllImport(DllName, EntryPoint = "catclaw_lyric_find_index")]
    public static extern int LyricFindIndex(IntPtr result, int timeMs);

    /* ============================================================
     * 音频标签读取
     * ============================================================ */

    /// <summary>
    /// 原生标签信息结构，与 C++ CatClawTagInfo 对应
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct NativeTagInfo
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)]
        public string Title;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)]
        public string Artist;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)]
        public string Album;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)]
        public string AlbumArtist;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Genre;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)]
        public string Comment;
        public int Year;
        public int Track;
        public int Disc;
        public int DurationMs;
        public int BitrateKbps;
        public int SampleRate;
        public int Channels;
        [MarshalAs(UnmanagedType.U1)]
        public bool HasCover;
        public int CoverOffset;
        public int CoverSize;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string CoverMime;
    }

    /// <summary>
    /// 从音频文件读取标签信息
    /// </summary>
    /// <param name="filePath">文件路径（UTF-8 字节数组）</param>
    /// <param name="info">输出标签信息</param>
    /// <returns>0 成功，-1 失败</returns>
    [DllImport(DllName, EntryPoint = "catclaw_read_tags")]
    public static extern int ReadTags(byte[] filePath, ref NativeTagInfo info);

    [DllImport(DllName, EntryPoint = "catclaw_read_tags_from_memory")]
    private static extern int ReadTagsFromMemory(IntPtr data, int size, ref NativeTagInfo info);

    public static bool TryReadTagsFromMemory(byte[] data, out NativeTagInfo info)
    {
        info = default;
        if (data == null || data.Length == 0) return false;
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            return ReadTagsFromMemory(handle.AddrOfPinnedObject(), data.Length, ref info) == 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            handle.Free();
        }
    }

    /// <summary>
    /// 从音频文件提取封面图片数据
    /// </summary>
    /// <param name="filePath">文件路径（UTF-8 字节数组）</param>
    /// <param name="outData">输出图片数据缓冲区</param>
    /// <param name="outSize">缓冲区大小，返回实际数据大小</param>
    /// <returns>0 成功，-1 失败，1 缓冲区不足</returns>
    [DllImport(DllName, EntryPoint = "catclaw_read_cover")]
    public static extern int ReadCover(byte[] filePath, byte[] outData, ref int outSize);

    /* ============================================================
     * 封面取色接口
     * ============================================================ */

    /// <summary>
    /// 原生颜色条目结构，与 C++ CatClawColorEntry 对应
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct NativeColorEntry
    {
        /// <summary>ARGB 颜色值</summary>
        public int Color;
        /// <summary>水平中心位置（0~1 归一化）</summary>
        public float CenterX;
    }

    /// <summary>
    /// 从 ARGB 像素数据提取主色调
    /// </summary>
    /// <param name="pixels">ARGB 像素数据（与 Android Bitmap 格式一致）</param>
    /// <param name="width">图片宽度</param>
    /// <param name="height">图片高度</param>
    /// <param name="maxEntries">最多提取的颜色数</param>
    /// <param name="entries">输出颜色数组</param>
    /// <returns>实际提取的颜色数</returns>
    [DllImport(DllName, EntryPoint = "catclaw_extract_colors")]
    public static extern int ExtractColors(
        uint[] pixels, int width, int height, int maxEntries,
        [Out] NativeColorEntry[] entries);

    /* ============================================================
     * 频谱数据处理接口
     * ============================================================ */

    /// <summary>
    /// 处理 FFT 频谱数据：幅度计算 + 对数频带映射 + 时间平滑
    /// </summary>
    [DllImport(DllName, EntryPoint = "catclaw_process_spectrum")]
    public static extern void ProcessSpectrum(
        float[] real, float[] imag, int fftSize,
        int[] bandEdges, int bandCount,
        float[] prevBands, float[] outBands,
        float attack, float decay);

    /// <summary>
    /// 构建对数频带边界索引
    /// </summary>
    [DllImport(DllName, EntryPoint = "catclaw_build_band_edges")]
    public static extern void BuildBandEdges(
        int sampleRate, int fftSize, float minFreq, float maxFreq,
        int bandCount, int[] bandEdges);

    /* ============================================================
     * 实时音频 PCM 处理接口
     * ============================================================ */

    /// <summary>
    /// 将 16-bit PCM 数据转换为单声道绝对值浮点数组
    /// </summary>
    [DllImport(DllName, EntryPoint = "catclaw_pcm_to_mono_abs")]
    public static extern int PcmToMonoAbs(
        short[] pcmData, int dataLen, int channelCount, float[] outFloat);

    /// <summary>
    /// 从单声道浮点 PCM 数据计算频谱条带
    /// </summary>
    [DllImport(DllName, EntryPoint = "catclaw_compute_spectrum_bands")]
    public static extern void ComputeSpectrumBands(
        float[] samples, int sampleCount, int bandCount, float[] outBands, float gain);

    /* ============================================================
     * 图像模糊接口
     * ============================================================ */

    /// <summary>
    /// 对 ARGB 像素数据执行 Stack Blur 模糊（就地修改）
    /// </summary>
    [DllImport(DllName, EntryPoint = "catclaw_stack_blur_argb")]
    public static extern void StackBlurArgb(
        uint[] pixels, int width, int height, int radius);

    /* ============================================================
     * 高级封装方法
     * ============================================================ */

    /// <summary>
    /// 使用原生库检测编码并转换为 UTF-8 字符串
    /// 如果原生库不可用，返回 null（由 C# 回退逻辑处理）
    /// </summary>
    /// <param name="rawBytes">原始字节数据</param>
    /// <returns>UTF-8 字符串，失败返回 null</returns>
    public static string? DetectAndConvertToUtf8(byte[] rawBytes)
    {
        if (!IsAvailable || rawBytes == null || rawBytes.Length == 0)
            return null;

        try
        {
            var encoding = DetectEncoding(rawBytes, rawBytes.Length);
            if (encoding == TextEncoding.Unknown)
                return null;

            /* 如果是 UTF-8，直接解码 */
            if (encoding == TextEncoding.Utf8 || encoding == TextEncoding.Utf8Bom)
            {
                int start = (encoding == TextEncoding.Utf8Bom && rawBytes.Length >= 3 &&
                             rawBytes[0] == 0xEF && rawBytes[1] == 0xBB && rawBytes[2] == 0xBF) ? 3 : 0;
                return System.Text.Encoding.UTF8.GetString(rawBytes, start, rawBytes.Length - start);
            }

            /* 非 UTF-8 编码：调用原生转换 */
            var outBuf = new byte[rawBytes.Length * 3]; /* 最坏情况：1字节→3字节 UTF-8 */
            int outLen = outBuf.Length;
            int result = ConvertToUtf8(rawBytes, rawBytes.Length, encoding, outBuf, ref outLen);
            if (result != 0 || outLen <= 0)
                return null;

            return System.Text.Encoding.UTF8.GetString(outBuf, 0, outLen);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 使用原生库读取音频文件标签
    /// 如果原生库不可用，返回 null
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>标签信息，失败返回 null</returns>
    public static NativeTagInfo? ReadAudioTags(string filePath)
    {
        if (!IsAvailable || string.IsNullOrEmpty(filePath))
            return null;

        try
        {
            var pathBytes = System.Text.Encoding.UTF8.GetBytes(filePath + "\0");
            var info = new NativeTagInfo();
            int result = ReadTags(pathBytes, ref info);
            return result == 0 ? info : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 使用原生库读取音频文件封面
    /// 如果原生库不可用或无封面，返回 null
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>封面图片字节数据，失败返回 null</returns>
    public static byte[]? ReadAudioCover(string filePath)
    {
        if (!IsAvailable || string.IsNullOrEmpty(filePath))
            return null;

        try
        {
            var pathBytes = System.Text.Encoding.UTF8.GetBytes(filePath + "\0");

            /* 第一次调用：获取封面大小 */
            int outSize = 0;
            int result = ReadCover(pathBytes, null!, ref outSize);
            if (outSize <= 0) return null;

            /* 第二次调用：读取封面数据 */
            var coverData = new byte[outSize];
            result = ReadCover(pathBytes, coverData, ref outSize);
            return result == 0 ? coverData : null;
        }
        catch
        {
            return null;
        }
    }

    [ThreadStatic]
    private static int[]? _cachedPixelBuffer;
    [ThreadStatic]
    private static uint[]? _cachedUintBuffer;

    /// <summary>
    /// 使用原生库从 Bitmap 提取主色调
    /// 如果原生库不可用，返回 null（由 C# 回退逻辑处理）
    /// </summary>
    /// <param name="bitmap">Android Bitmap</param>
    /// <returns>颜色条目列表，失败返回 null</returns>
    public static List<ColorEntry>? ExtractColorsFromBitmap(Android.Graphics.Bitmap? bitmap)
    {
        if (!IsAvailable || bitmap == null || bitmap.IsRecycled)
            return null;

        try
        {
            /* 降采样：大图缩放至 120x120 以内 */
            const int maxSampleSize = 120;
            Android.Graphics.Bitmap? sampled = null;
            int sampleW, sampleH;
            if (bitmap.Width > maxSampleSize || bitmap.Height > maxSampleSize)
            {
                var scale = (float)maxSampleSize / Math.Max(bitmap.Width, bitmap.Height);
                sampleW = Math.Max((int)(bitmap.Width * scale), 1);
                sampleH = Math.Max((int)(bitmap.Height * scale), 1);
                sampled = Android.Graphics.Bitmap.CreateScaledBitmap(bitmap, sampleW, sampleH, false);
            }
            else
            {
                sampled = bitmap;
                sampleW = sampled.Width;
                sampleH = sampled.Height;
            }

            int pixelCount = sampleW * sampleH;

            /* 复用线程静态缓冲区，避免每次切歌分配 LOS 大对象 */
            if (_cachedPixelBuffer == null || _cachedPixelBuffer.Length < pixelCount)
                _cachedPixelBuffer = new int[pixelCount];
            if (_cachedUintBuffer == null || _cachedUintBuffer.Length < pixelCount)
                _cachedUintBuffer = new uint[pixelCount];

            sampled.GetPixels(_cachedPixelBuffer, 0, sampleW, 0, 0, sampleW, sampleH);
            for (int i = 0; i < pixelCount; i++)
                _cachedUintBuffer[i] = (uint)_cachedPixelBuffer[i];

            /* 释放降采样位图 */
            if (sampled != bitmap && sampled != null)
                sampled.Recycle();

            /* 调用原生取色 */
            var entries = new NativeColorEntry[6];
            int count = ExtractColors(_cachedUintBuffer, sampleW, sampleH, 6, entries);

            if (count <= 0) return null;

            /* 转换为 C# ColorEntry 列表 */
            var result = new List<ColorEntry>();
            for (int i = 0; i < count; i++)
            {
                result.Add(new ColorEntry
                {
                    Color = entries[i].Color,
                    CenterX = entries[i].CenterX
                });
            }
            return result;
        }
        catch
        {
            return null;
        }
    }
}
