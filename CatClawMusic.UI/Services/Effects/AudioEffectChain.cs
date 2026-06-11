namespace CatClawMusic.UI.Services.Effects;

/// <summary>
/// 音频效果处理链编排器：管理所有 IAudioEffect 处理器，按 Priority 顺序执行。
/// EqBandProcessor 作为特殊首步处理（它有独立 API），其余效果按 Priority 排序。
/// </summary>
public class AudioEffectChain
{
    private readonly EqBandProcessor _eqProcessor;
    private readonly List<IAudioEffect> _effects = new();
    private IAudioEffect[] _sortedEffects = Array.Empty<IAudioEffect>();
    private bool _dirty = true;

    public AudioEffectChain(EqBandProcessor eqProcessor, IEnumerable<IAudioEffect> effects)
    {
        _eqProcessor = eqProcessor;
        _effects.AddRange(effects);
        RebuildSorted();
    }

    /// <summary>处理链主开关（关闭时完全旁通所有效果）</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>获取 EQ 处理器引用（供 SoundEffectDialog 使用）</summary>
    public EqBandProcessor EqProcessor => _eqProcessor;

    /// <summary>获取指定类型的效果处理器</summary>
    public T? GetEffect<T>() where T : class, IAudioEffect
        => _effects.OfType<T>().FirstOrDefault();

    /// <summary>
    /// 处理交错立体声样本。依次执行: EQ → 各 IAudioEffect（按 Priority）。
    /// </summary>
    public void Process(float[] samples, int frameCount)
    {
        if (!Enabled) return;

        // 第一步: EQ（使用其自有 API）
        if (_eqProcessor.Enabled)
            _eqProcessor.ProcessInterleaved(samples, frameCount);

        // 后续步骤: 按 Priority 执行
        if (_dirty) RebuildSorted();
        var effects = _sortedEffects;
        for (int i = 0; i < effects.Length; i++)
        {
            var fx = effects[i];
            if (fx.Enabled)
                fx.Process(samples, frameCount);
        }
    }

    /// <summary>向所有效果传播采样率变化</summary>
    public void SetSampleRate(float sampleRate)
    {
        _eqProcessor.SetSampleRate(sampleRate);
        for (int i = 0; i < _effects.Count; i++)
            _effects[i].SetSampleRate(sampleRate);
    }

    /// <summary>向所有效果传播 Reset</summary>
    public void Reset()
    {
        for (int i = 0; i < _effects.Count; i++)
            _effects[i].Reset();
    }

    private void RebuildSorted()
    {
        _sortedEffects = _effects.OrderBy(e => e.Priority).ToArray();
        _dirty = false;
    }
}
