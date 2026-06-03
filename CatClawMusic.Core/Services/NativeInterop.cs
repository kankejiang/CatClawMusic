using System;
using System.Runtime.InteropServices;

namespace CatClawMusic.Core.Services;

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
        public int Color;
        public float CenterX;
        public float Weight;
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

    /* ============================================================
     * AI Agent JSON 处理接口（C++ 原生实现）
     * ============================================================ */

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeToolCallEntry
    {
        public IntPtr Id;
        public IntPtr Name;
        public IntPtr Arguments;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeLlmResponse
    {
        public IntPtr Content;
        public IntPtr FinishReason;
        public int ToolCallCount;
        public IntPtr ToolCalls;
        public IntPtr Error;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeChatMessage
    {
        public IntPtr Role;
        public IntPtr Content;
        public IntPtr ToolCallId;
        public IntPtr Name;
        public int ToolCallCount;
        public IntPtr ToolCalls;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeToolCallMsg
    {
        public IntPtr Id;
        public IntPtr Name;
        public IntPtr Arguments;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeParamProperty
    {
        public IntPtr Type;
        public IntPtr Description;
        public int EnumCount;
        public IntPtr EnumValues;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeToolDef
    {
        public IntPtr Name;
        public IntPtr Description;
        public int ParamCount;
        public IntPtr ParamNames;
        public IntPtr ParamProperties;
        public int RequiredCount;
        public IntPtr RequiredParams;
    }

    [DllImport(DllName, EntryPoint = "catclaw_ai_build_chat_request")]
    private static extern IntPtr NativeBuildChatRequest(
        IntPtr model, IntPtr messages, int msgCount,
        IntPtr tools, int toolCount,
        double temperature, int maxTokens);

    [DllImport(DllName, EntryPoint = "catclaw_ai_parse_chat_response")]
    private static extern IntPtr NativeParseChatResponse(IntPtr json, int jsonLen);

    [DllImport(DllName, EntryPoint = "catclaw_ai_extract_string_arg")]
    private static extern IntPtr NativeExtractStringArg(IntPtr json, int jsonLen, IntPtr key);

    [DllImport(DllName, EntryPoint = "catclaw_ai_extract_int_arg")]
    private static extern int NativeExtractIntArg(IntPtr json, int jsonLen, IntPtr key, int defaultVal);

    [DllImport(DllName, EntryPoint = "catclaw_ai_build_url")]
    private static extern IntPtr NativeBuildUrl(IntPtr baseUrl);

    [DllImport(DllName, EntryPoint = "catclaw_ai_free")]
    private static extern void AiFree(IntPtr ptr);

    [DllImport(DllName, EntryPoint = "catclaw_ai_free_response")]
    private static extern void AiFreeResponse(IntPtr response);

    public static string? AiBuildChatRequest(
        string model, List<CatClawMusic.Core.Services.AI.ChatMessage> messages,
        List<CatClawMusic.Core.Services.AI.ToolDefinition>? tools,
        double temperature, int maxTokens)
    {
        if (!IsAvailable) return null;
        try
        {
            var modelPtr = Marshal.StringToHGlobalAnsi(model);
            try
            {
                var msgArr = BuildNativeMessages(messages);
                try
                {
                    IntPtr toolsPtr = IntPtr.Zero;
                    int toolCount = 0;
                    var toolDefArrs = new List<IntPtr[]>();
                    var toolNamePtrs = new List<IntPtr>();
                    var toolDescPtrs = new List<IntPtr>();
                    var paramAllocs = new List<IntPtr>();

                    if (tools != null && tools.Count > 0)
                    {
                        toolCount = tools.Count;
                        var nativeToolDefs = new NativeToolDef[toolCount];

                        for (int i = 0; i < toolCount; i++)
                        {
                            var t = tools[i];
                            var namePtr = Marshal.StringToHGlobalAnsi(t.Function.Name);
                            var descPtr = Marshal.StringToHGlobalAnsi(t.Function.Description);
                            toolNamePtrs.Add(namePtr);
                            toolDescPtrs.Add(descPtr);

                            var paramNames = t.Function.Parameters.Properties.Keys.ToList();
                            var paramNamePtrs = new IntPtr[paramNames.Count];
                            var paramProps = new NativeParamProperty[paramNames.Count];
                            var propAllocs = new List<IntPtr>();

                            for (int j = 0; j < paramNames.Count; j++)
                            {
                                paramNamePtrs[j] = Marshal.StringToHGlobalAnsi(paramNames[j]);
                                paramAllocs.Add(paramNamePtrs[j]);

                                var prop = t.Function.Parameters.Properties[paramNames[j]];
                                var typePtr = Marshal.StringToHGlobalAnsi(prop.Type);
                                var descP = Marshal.StringToHGlobalAnsi(prop.Description);
                                propAllocs.Add(typePtr);
                                propAllocs.Add(descP);

                                IntPtr enumPtr = IntPtr.Zero;
                                if (prop.Enum != null && prop.Enum.Count > 0)
                                {
                                    var ep = new IntPtr[prop.Enum.Count];
                                    for (int k = 0; k < prop.Enum.Count; k++)
                                    {
                                        ep[k] = Marshal.StringToHGlobalAnsi(prop.Enum[k]);
                                        propAllocs.Add(ep[k]);
                                    }
                                    enumPtr = Marshal.AllocHGlobal(IntPtr.Size * prop.Enum.Count);
                                    Marshal.Copy(ep, 0, enumPtr, prop.Enum.Count);
                                    propAllocs.Add(enumPtr);
                                }

                                paramProps[j] = new NativeParamProperty
                                {
                                    Type = typePtr,
                                    Description = descP,
                                    EnumCount = prop.Enum?.Count ?? 0,
                                    EnumValues = enumPtr
                                };
                            }

                            var paramNamesPtr = Marshal.AllocHGlobal(IntPtr.Size * paramNames.Count);
                            Marshal.Copy(paramNamePtrs, 0, paramNamesPtr, paramNames.Count);
                            paramAllocs.Add(paramNamesPtr);

                            var paramPropsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<NativeParamProperty>() * paramNames.Count);
                            for (int j = 0; j < paramNames.Count; j++)
                                Marshal.StructureToPtr(paramProps[j], paramPropsPtr + j * Marshal.SizeOf<NativeParamProperty>(), false);
                            paramAllocs.Add(paramPropsPtr);

                            var reqParams = t.Function.Parameters.Required;
                            var reqPtrs = new IntPtr[reqParams.Count];
                            for (int j = 0; j < reqParams.Count; j++)
                            {
                                reqPtrs[j] = Marshal.StringToHGlobalAnsi(reqParams[j]);
                                paramAllocs.Add(reqPtrs[j]);
                            }
                            var reqArrPtr = Marshal.AllocHGlobal(IntPtr.Size * reqParams.Count);
                            if (reqParams.Count > 0) Marshal.Copy(reqPtrs, 0, reqArrPtr, reqParams.Count);
                            paramAllocs.Add(reqArrPtr);

                            nativeToolDefs[i] = new NativeToolDef
                            {
                                Name = namePtr,
                                Description = descPtr,
                                ParamCount = paramNames.Count,
                                ParamNames = paramNamesPtr,
                                ParamProperties = paramPropsPtr,
                                RequiredCount = reqParams.Count,
                                RequiredParams = reqArrPtr
                            };
                        }

                        toolsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<NativeToolDef>() * toolCount);
                        for (int i = 0; i < toolCount; i++)
                            Marshal.StructureToPtr(nativeToolDefs[i], toolsPtr + i * Marshal.SizeOf<NativeToolDef>(), false);
                        paramAllocs.Add(toolsPtr);
                    }

                    try
                    {
                        var resultPtr = NativeBuildChatRequest(modelPtr, msgArr, messages.Count, toolsPtr, toolCount, temperature, maxTokens);
                        if (resultPtr == IntPtr.Zero) return null;
                        var result = Marshal.PtrToStringAnsi(resultPtr);
                        AiFree(resultPtr);
                        return result;
                    }
                    finally
                    {
                        foreach (var p in paramAllocs) Marshal.FreeHGlobal(p);
                    }
                }
                finally
                {
                    FreeNativeMessages(msgArr, messages.Count);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(modelPtr);
            }
        }
        catch { return null; }
    }

    public static CatClawMusic.Core.Services.AI.LlmResponse? AiParseChatResponse(string responseBody)
    {
        if (!IsAvailable) return null;
        try
        {
            var jsonBytes = System.Text.Encoding.UTF8.GetBytes(responseBody);
            var jsonPtr = Marshal.AllocHGlobal(jsonBytes.Length);
            try
            {
                Marshal.Copy(jsonBytes, 0, jsonPtr, jsonBytes.Length);
                var resultPtr = NativeParseChatResponse(jsonPtr, jsonBytes.Length);
                if (resultPtr == IntPtr.Zero) return null;

                try
                {
                    var native = Marshal.PtrToStructure<NativeLlmResponse>(resultPtr);
                    var response = new CatClawMusic.Core.Services.AI.LlmResponse();

                    if (native.Error != IntPtr.Zero)
                    {
                        var errMsg = Marshal.PtrToStringAnsi(native.Error);
                        throw new InvalidOperationException($"API 错误: {errMsg}");
                    }

                    response.Content = native.Content != IntPtr.Zero ? Marshal.PtrToStringAnsi(native.Content) ?? "" : "";
                    response.FinishReason = native.FinishReason != IntPtr.Zero ? Marshal.PtrToStringAnsi(native.FinishReason) ?? "" : "";

                    if (native.ToolCallCount > 0 && native.ToolCalls != IntPtr.Zero)
                    {
                        for (int i = 0; i < native.ToolCallCount; i++)
                        {
                            var tcEntry = Marshal.PtrToStructure<NativeToolCallEntry>(
                                native.ToolCalls + i * Marshal.SizeOf<NativeToolCallEntry>());
                            response.ToolCalls.Add(new CatClawMusic.Core.Services.AI.ToolCall
                            {
                                Id = tcEntry.Id != IntPtr.Zero ? Marshal.PtrToStringAnsi(tcEntry.Id) ?? "" : "",
                                Type = "function",
                                Function = new CatClawMusic.Core.Services.AI.ToolCallFunction
                                {
                                    Name = tcEntry.Name != IntPtr.Zero ? Marshal.PtrToStringAnsi(tcEntry.Name) ?? "" : "",
                                    Arguments = tcEntry.Arguments != IntPtr.Zero ? Marshal.PtrToStringAnsi(tcEntry.Arguments) ?? "{}" : "{}"
                                }
                            });
                        }
                    }
                    return response;
                }
                finally
                {
                    AiFreeResponse(resultPtr);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(jsonPtr);
            }
        }
        catch { return null; }
    }

    public static string? AiExtractStringArg(string argsJson, string key)
    {
        if (!IsAvailable) return null;
        try
        {
            var jsonBytes = System.Text.Encoding.UTF8.GetBytes(argsJson);
            var jsonPtr = Marshal.AllocHGlobal(jsonBytes.Length);
            var keyPtr = Marshal.StringToHGlobalAnsi(key);
            try
            {
                Marshal.Copy(jsonBytes, 0, jsonPtr, jsonBytes.Length);
                var resultPtr = NativeExtractStringArg(jsonPtr, jsonBytes.Length, keyPtr);
                if (resultPtr == IntPtr.Zero) return null;
                var result = Marshal.PtrToStringAnsi(resultPtr);
                AiFree(resultPtr);
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(jsonPtr);
                Marshal.FreeHGlobal(keyPtr);
            }
        }
        catch { return null; }
    }

    public static int AiExtractIntArg(string argsJson, string key, int defaultVal = 0)
    {
        if (!IsAvailable) return defaultVal;
        try
        {
            var jsonBytes = System.Text.Encoding.UTF8.GetBytes(argsJson);
            var jsonPtr = Marshal.AllocHGlobal(jsonBytes.Length);
            var keyPtr = Marshal.StringToHGlobalAnsi(key);
            try
            {
                Marshal.Copy(jsonBytes, 0, jsonPtr, jsonBytes.Length);
                return NativeExtractIntArg(jsonPtr, jsonBytes.Length, keyPtr, defaultVal);
            }
            finally
            {
                Marshal.FreeHGlobal(jsonPtr);
                Marshal.FreeHGlobal(keyPtr);
            }
        }
        catch { return defaultVal; }
    }

    public static string? AiBuildUrl(string baseUrl)
    {
        if (!IsAvailable) return null;
        try
        {
            var ptr = Marshal.StringToHGlobalAnsi(baseUrl);
            try
            {
                var resultPtr = NativeBuildUrl(ptr);
                if (resultPtr == IntPtr.Zero) return null;
                var result = Marshal.PtrToStringAnsi(resultPtr);
                AiFree(resultPtr);
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        catch { return null; }
    }

    private static IntPtr BuildNativeMessages(List<CatClawMusic.Core.Services.AI.ChatMessage> messages)
    {
        var arr = Marshal.AllocHGlobal(Marshal.SizeOf<NativeChatMessage>() * messages.Count);
        for (int i = 0; i < messages.Count; i++)
        {
            var m = messages[i];
            var native = new NativeChatMessage
            {
                Role = Marshal.StringToHGlobalAnsi(m.Role),
                Content = Marshal.StringToHGlobalAnsi(m.Content ?? ""),
                ToolCallId = m.ToolCallId != null ? Marshal.StringToHGlobalAnsi(m.ToolCallId) : IntPtr.Zero,
                Name = m.Name != null ? Marshal.StringToHGlobalAnsi(m.Name) : IntPtr.Zero,
                ToolCallCount = m.ToolCalls?.Count ?? 0,
                ToolCalls = IntPtr.Zero
            };

            if (m.ToolCalls != null && m.ToolCalls.Count > 0)
            {
                native.ToolCalls = Marshal.AllocHGlobal(Marshal.SizeOf<NativeToolCallMsg>() * m.ToolCalls.Count);
                for (int j = 0; j < m.ToolCalls.Count; j++)
                {
                    var tc = m.ToolCalls[j];
                    Marshal.StructureToPtr(new NativeToolCallMsg
                    {
                        Id = Marshal.StringToHGlobalAnsi(tc.Id),
                        Name = Marshal.StringToHGlobalAnsi(tc.Function.Name),
                        Arguments = Marshal.StringToHGlobalAnsi(tc.Function.Arguments)
                    }, native.ToolCalls + j * Marshal.SizeOf<NativeToolCallMsg>(), false);
                }
            }

            Marshal.StructureToPtr(native, arr + i * Marshal.SizeOf<NativeChatMessage>(), false);
        }
        return arr;
    }

    private static void FreeNativeMessages(IntPtr arr, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var native = Marshal.PtrToStructure<NativeChatMessage>(arr + i * Marshal.SizeOf<NativeChatMessage>());
            Marshal.FreeHGlobal(native.Role);
            Marshal.FreeHGlobal(native.Content);
            if (native.ToolCallId != IntPtr.Zero) Marshal.FreeHGlobal(native.ToolCallId);
            if (native.Name != IntPtr.Zero) Marshal.FreeHGlobal(native.Name);
            if (native.ToolCalls != IntPtr.Zero)
            {
                for (int j = 0; j < native.ToolCallCount; j++)
                {
                    var tc = Marshal.PtrToStructure<NativeToolCallMsg>(
                        native.ToolCalls + j * Marshal.SizeOf<NativeToolCallMsg>());
                    Marshal.FreeHGlobal(tc.Id);
                    Marshal.FreeHGlobal(tc.Name);
                    Marshal.FreeHGlobal(tc.Arguments);
                }
                Marshal.FreeHGlobal(native.ToolCalls);
            }
        }
        Marshal.FreeHGlobal(arr);
    }
}
