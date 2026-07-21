namespace CatClawMusic.Maui.Services.Equalizer;

/// <summary>均衡器设置模型与预设管理（预设参考 VLC 开源 equalizer_presets.h，适配 7 频段）</summary>
public static class EqualizerSettings
{
    // ─── UI 频段定义（7 段，与原型一致） ───
    /// <summary>UI 频段中心频率 (Hz)</summary>
    public static readonly int[] BandFrequencies = { 60, 150, 400, 1000, 2400, 6000, 15000 };

    /// <summary>频段显示标签</summary>
    public static readonly string[] BandLabels = { "60", "150", "400", "1K", "2.4K", "6K", "15K" };

    /// <summary>增益范围 (dB)</summary>
    public const double MinGainDb = -12.0;
    public const double MaxGainDb = 12.0;

    // ─── 预设定义（VLC 10 频段预设重采样至 7 频段，取整 dB） ───
    public static readonly Dictionary<string, double[]> Presets = new()
    {
        // VLC Flat
        ["flat"] = new double[] { 0, 0, 0, 0, 0, 0, 0 },
        // VLC Pop: -1.6, 4.8, 7.2, 8.0, 5.6, 0, -2.4, -2.4, -1.6, -1.6
        ["pop"] = new double[] { -2, 3, 6, 6, 3, -2, -2 },
        // VLC Rock: 8, 4.8, -5.6, -8, -3.2, 4, 8.8, 11.2, 11.2, 11.2
        ["rock"] = new double[] { 8, 5, -7, -3, 0, 9, 11 },
        // VLC Classical: 0×6, -7.2, -7.2, -7.2, -9.6
        ["classical"] = new double[] { 0, 0, 0, 0, 1, -7, -8 },
        // 人声：中频突出（参考 VLC Pop + Live 中间带）
        ["vocal"] = new double[] { -2, -1, 2, 5, 4, 1, 0 },
        // VLC Dance: 9.6, 7.2, 2.4, 0, 0, -5.6, -7.2, -7.2, 0, 0
        ["dance"] = new double[] { 10, 7, 1, 0, -3, -7, -2 },
        // VLC Full bass: -8, 9.6, 9.6, 5.6, 1.6, -4, -8, -10.4, -11.2, -11.2（60Hz 取 170Hz 附近提升）
        ["bass"] = new double[] { 8, 10, 6, 2, -1, -6, -8 },
        // VLC Techno: 8, 5.6, 0, -5.6, -4.8, 0, 8, 9.6, 9.6, 8.8
        ["electronic"] = new double[] { 8, 6, 0, -5, -2, 8, 9 },
        // 钢琴：中高频明亮
        ["piano"] = new double[] { -1, 0, 2, 4, 3, 1, -1 },
        // 影院：参考 VLC Large Hall + 环绕感
        ["cinema"] = new double[] { 6, 5, 2, 1, 2, 4, 3 },
        // 车载：补偿路噪低频掩蔽
        ["car"] = new double[] { 7, 6, 4, 2, 0, 1, 2 },
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

    /// <summary>7 频段增益 (dB)</summary>
    public static double[] GetBandGains()
    {
        var raw = Preferences.Default.Get(KeyGains, "");
        if (!string.IsNullOrEmpty(raw))
        {
            var parts = raw.Split(',');
            if (parts.Length == BandFrequencies.Length)
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
        }
        return new double[BandFrequencies.Length]; // 全 0
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

    /// <summary>左右平衡 -100(L) ~ +100(R)，0 居中</summary>
    public static int Balance
    {
        get => Preferences.Default.Get(KeyBalance, 0);
        set => Preferences.Default.Set(KeyBalance, Math.Clamp(value, -100, 100));
    }

    /// <summary>应用预设并持久化</summary>
    public static void ApplyPreset(string presetKey)
    {
        CurrentPreset = presetKey;
        if (Presets.TryGetValue(presetKey, out var gains))
            SetBandGains((double[])gains.Clone());
    }

    /// <summary>生成 FFmpeg equalizer 滤镜链（供转码管线使用）</summary>
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
