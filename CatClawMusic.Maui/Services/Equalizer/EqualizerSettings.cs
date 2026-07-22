namespace CatClawMusic.Maui.Services.Equalizer;

/// <summary>
/// 均衡器设置模型与预设管理。
/// 频段数为固定两套：
///  - 原生模式（默认）：5 段，对应 Android 原生 Equalizer 在 MIUI/澎湃 OS 上的真实档位，实时全生效。
///  - FFmpeg 模式：10 段，均衡器由 FFmpeg 滤镜烘焙进音频（支持任意段数），UI 显示 10 段。
/// 预设以 10 段为基准定义，应用到 5 段时按对数频率重采样。
/// </summary>
public static class EqualizerSettings
{
    // ─── 原生模式频段（5 段，MIUI/澎湃 OS 真实支持） ───
    public static readonly int[] NativeFrequencies = { 60, 250, 1000, 4000, 12000 };

    // ─── FFmpeg 模式频段（10 段，烘焙进转码音频） ───
    public static readonly int[] FFmpegFrequencies = { 31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };

    /// <summary>是否使用 FFmpeg 烘焙式均衡器（10 段）。false=原生 5 段实时均衡。</summary>
    public static bool UseFFmpegEq
    {
        get => Preferences.Default.Get(KeyUseFFmpeg, false);
        set
        {
            if (value == UseFFmpegEq) return;
            // 切换模式前，把当前增益在旧/新频段数之间重采样，保留曲线形状
            var oldFreqs = UseFFmpegEq ? FFmpegFrequencies : NativeFrequencies;
            var newFreqs = value ? FFmpegFrequencies : NativeFrequencies;
            var oldGains = GetBandGains();
            if (oldGains.Length != oldFreqs.Length) oldGains = new double[oldFreqs.Length];
            var newGains = ResampleGains(oldGains, oldFreqs, newFreqs);
            Preferences.Default.Set(KeyUseFFmpeg, value);
            SetBandGains(newGains);
        }
    }

    /// <summary>当前激活的频段中心频率 (Hz)：FFmpeg 模式 10 段，否则原生 5 段</summary>
    public static int[] BandFrequencies => UseFFmpegEq ? FFmpegFrequencies : NativeFrequencies;

    /// <summary>当前激活的频段显示标签</summary>
    public static string[] BandLabels => UseFFmpegEq ? FFmpegLabels : NativeLabels;

    private static readonly string[] NativeLabels = NativeFrequencies.Select(FormatHz).ToArray();
    private static readonly string[] FFmpegLabels = FFmpegFrequencies.Select(FormatHz).ToArray();

    /// <summary>增益范围 (dB)</summary>
    public const double MinGainDb = -12.0;
    public const double MaxGainDb = 12.0;

    // ─── 预设定义（10 段基准，VLC 开源预设参考，取整 dB） ───
    public static readonly Dictionary<string, double[]> Presets = new()
    {
        ["flat"]      = new double[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
        // VLC Pop: -1.6, 4.8, 7.2, 8.0, 5.6, 0, -2.4, -2.4, -1.6, -1.6
        ["pop"]       = new double[] { -2, 5, 7, 7, 3, 0, -2, -2, -2, -2 },
        // VLC Rock: 8, 4.8, -5.6, -8, -3.2, 4, 8.8, 11.2, 11.2, 11.2
        ["rock"]      = new double[] { 8, 5, -6, -3, 0, 4, 9, 11, 11, 11 },
        // VLC Classical: 0,0,0,0,0,0,-7.2,-7.2,-7.2,-9.6
        ["classical"] = new double[] { 0, 0, 0, 0, 0, 0, -7, -7, -7, -10 },
        // 人声：中频突出
        ["vocal"]     = new double[] { -2, -1, 2, 5, 5, 3, 1, 0, -1, -2 },
        // VLC Dance: 9.6, 7.2, 2.4, 0, 0, -5.6, -7.2, -7.2, 0, 0
        ["dance"]     = new double[] { 10, 7, 2, 0, 0, -6, -7, -7, -2, -2 },
        // 重低音：低频大幅抬升
        ["bass"]      = new double[] { 8, 10, 7, 4, 1, -2, -5, -7, -8, -9 },
        // VLC Techno: 8, 5.6, 0, -5.6, -4.8, 0, 8, 9.6, 9.6, 8.8
        ["electronic"] = new double[] { 8, 6, 0, -6, -5, 0, 8, 10, 10, 9 },
        // 钢琴：中高频明亮
        ["piano"]     = new double[] { -1, 0, 2, 5, 4, 2, 1, -1, -1, -2 },
        // 影院：整体轻微提升、环绕感
        ["cinema"]    = new double[] { 6, 5, 3, 2, 2, 3, 4, 4, 3, 2 },
        // 车载：补偿路噪低频掩蔽
        ["car"]      = new double[] { 7, 6, 4, 2, 1, 1, 2, 3, 3, 3 },
    };

    /// <summary>预设显示名称映射</summary>
    public static readonly (string Key, string Name)[] PresetList =
    {
        ("flat", "原声"), ("pop", "流行"), ("rock", "摇滚"), ("classical", "古典"),
        ("vocal", "人声"), ("dance", "舞曲"), ("bass", "重低音"), ("electronic", "电子"),
        ("piano", "钢琴"), ("cinema", "影院"), ("car", "车载"),
    };

    // ─── Preferences 键 ───
    private const string KeyEnabled = "eq_enabled";
    private const string KeyPreset = "eq_preset";
    private const string KeyGains = "eq_band_gains";
    private const string KeyBassBoost = "eq_bass_boost";
    private const string KeyLoudness = "eq_loudness";
    private const string KeyBalance = "eq_balance";
    private const string KeySpatial = "eq_spatial";
    private const string KeyCrossfade = "eq_crossfade";
    private const string KeyCrossfadeDur = "eq_crossfade_dur";
    private const string KeyUseFFmpeg = "eq_use_ffmpeg";

    /// <summary>均衡器总开关</summary>
    public static bool Enabled
    {
        get => Preferences.Default.Get(KeyEnabled, false);
        set => Preferences.Default.Set(KeyEnabled, value);
    }

    /// <summary>当前预设键名（"custom" 表示自定义）</summary>
    public static string CurrentPreset
    {
        get => Preferences.Default.Get(KeyPreset, "flat");
        set => Preferences.Default.Set(KeyPreset, value);
    }

    /// <summary>当前频段增益 (dB)，长度 = BandFrequencies.Length；段数不匹配时自动重采样</summary>
    public static double[] GetBandGains()
    {
        var activeFreqs = BandFrequencies;
        var raw = Preferences.Default.Get(KeyGains, "");
        if (!string.IsNullOrEmpty(raw))
        {
            var parts = raw.Split(',');
            if (parts.Length == activeFreqs.Length)
            {
                var gains = new double[parts.Length];
                bool ok = true;
                for (int i = 0; i < parts.Length; i++)
                {
                    if (!double.TryParse(parts[i], System.Globalization.CultureInfo.InvariantCulture, out gains[i]))
                    { ok = false; break; }
                }
                if (ok) return gains;
            }
            else if ((parts.Length == 5 || parts.Length == 10) &&
                     (activeFreqs.Length == 5 || activeFreqs.Length == 10))
            {
                // 存储的段数与当前激活段数不同 → 按频率重采样
                var oldGains = new double[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                    double.TryParse(parts[i], System.Globalization.CultureInfo.InvariantCulture, out oldGains[i]);
                var oldFreqs = parts.Length == 5 ? NativeFrequencies : FFmpegFrequencies;
                return ResampleGains(oldGains, oldFreqs, activeFreqs);
            }
        }
        return new double[activeFreqs.Length]; // 全 0
    }

    public static void SetBandGains(double[] gains)
    {
        var raw = string.Join(",", gains.Select(g => g.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)));
        Preferences.Default.Set(KeyGains, raw);
    }

    /// <summary>低音增强强度 0-100</summary>
    public static int BassBoost
    {
        get => Preferences.Default.Get(KeyBassBoost, 0);
        set => Preferences.Default.Set(KeyBassBoost, Math.Clamp(value, 0, 100));
    }

    /// <summary>响度增益 0-100</summary>
    public static int Loudness
    {
        get => Preferences.Default.Get(KeyLoudness, 0);
        set => Preferences.Default.Set(KeyLoudness, Math.Clamp(value, 0, 100));
    }

    /// <summary>左右平衡 -100(L) ~ +100(R)，0 居中（UI 已隐藏，保留供将来）</summary>
    public static int Balance
    {
        get => Preferences.Default.Get(KeyBalance, 0);
        set => Preferences.Default.Set(KeyBalance, Math.Clamp(value, -100, 100));
    }

    /// <summary>空间音频（3D 环绕）开关</summary>
    public static bool SpatialAudioEnabled
    {
        get => Preferences.Default.Get(KeySpatial, false);
        set => Preferences.Default.Set(KeySpatial, value);
    }

    /// <summary>淡入淡出（曲目间交叉淡变）开关</summary>
    public static bool CrossfadeEnabled
    {
        get => Preferences.Default.Get(KeyCrossfade, false);
        set => Preferences.Default.Set(KeyCrossfade, value);
    }

    /// <summary>淡入淡出时长（秒，0~12）</summary>
    public static int CrossfadeDuration
    {
        get => Preferences.Default.Get(KeyCrossfadeDur, 4);
        set => Preferences.Default.Set(KeyCrossfadeDur, Math.Clamp(value, 0, 12));
    }

    /// <summary>应用预设并持久化（按当前激活段数重采样）</summary>
    public static void ApplyPreset(string presetKey)
    {
        CurrentPreset = presetKey;
        if (Presets.TryGetValue(presetKey, out var canonical10))
        {
            var activeFreqs = BandFrequencies;
            var gains = activeFreqs.Length == 10
                ? (double[])canonical10.Clone()
                : ResampleGains(canonical10, FFmpegFrequencies, activeFreqs);
            SetBandGains(gains);
        }
    }

    /// <summary>按对数频率在源增益数组之间插值，得到目标频率的增益</summary>
    private static double[] ResampleGains(double[] srcGains, int[] srcFreqs, int[] dstFreqs)
    {
        var outArr = new double[dstFreqs.Length];
        for (int i = 0; i < dstFreqs.Length; i++)
        {
            var f = dstFreqs[i];
            if (f <= srcFreqs[0]) { outArr[i] = srcGains[0]; continue; }
            if (f >= srcFreqs[^1]) { outArr[i] = srcGains[^1]; continue; }
            for (int j = 0; j < srcFreqs.Length - 1; j++)
            {
                if (f >= srcFreqs[j] && f <= srcFreqs[j + 1])
                {
                    var t = Math.Log((double)f / srcFreqs[j]) / Math.Log((double)srcFreqs[j + 1] / srcFreqs[j]);
                    outArr[i] = srcGains[j] + t * (srcGains[j + 1] - srcGains[j]);
                    break;
                }
            }
        }
        return outArr;
    }

    private static string FormatHz(int hz)
    {
        if (hz >= 1000)
        {
            var k = hz / 1000.0;
            return (Math.Abs(k - Math.Floor(k)) < 0.05 ? ((int)Math.Round(k)).ToString() : k.ToString("0.#")) + "K";
        }
        return hz.ToString();
    }

    /// <summary>生成 FFmpeg equalizer 滤镜链（10 段烘焙进转码音频）</summary>
    /// <returns>滤镜字符串，全零增益时返回空串</returns>
    public static string BuildFFmpegFilterChain()
    {
        if (!Enabled) return "";
        var gains = GetBandGains();
        if (gains.All(g => Math.Abs(g) < 0.1) && BassBoost == 0 && Loudness == 0)
            return "";

        var filters = new List<string>();
        for (int i = 0; i < gains.Length; i++)
        {
            if (Math.Abs(gains[i]) < 0.1) continue;
            // equalizer=f=频率:t=q:w=Q值:g=增益dB（Q=1.4 近似图形均衡器带宽）
            filters.Add($"equalizer=f={BandFrequencies[i]}:t=q:w=1.4:g={gains[i].ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}");
        }

        // 低音增强 → 60Hz 低频架式滤波
        if (BassBoost > 0)
        {
            var boostDb = BassBoost * 12.0 / 100.0;
            filters.Add($"lowshelf=f=120:g={boostDb.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}");
        }

        // 响度增益 → volume 滤镜
        if (Loudness > 0)
        {
            var gainDb = Loudness * 6.0 / 100.0;
            filters.Add($"volume={gainDb.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}dB");
        }

        return string.Join(",", filters);
    }
}
