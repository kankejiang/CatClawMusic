namespace CatClawMusic.Maui.Converters;

// 以下 5 个转换器实现已上移至共享库 CatClaw.Shared.Maui（与猫爪影视共用），本文件保留同名占位子类，XAML/C# 调用面零改动。

/// <summary>标题首字符占位转换器</summary>
public class InitialConverter : CatClaw.Shared.Maui.Converters.InitialConverter
{
}

/// <summary>封面路径有效性转换器</summary>
public class CoverArtConverter : CatClaw.Shared.Maui.Converters.CoverArtConverter
{
}

/// <summary>按 ID 生成一致占位颜色转换器</summary>
public class PlaceholderColorConverter : CatClaw.Shared.Maui.Converters.PlaceholderColorConverter
{
}

/// <summary>播放次数格式化转换器</summary>
public class PlaybackCountConverter : CatClaw.Shared.Maui.Converters.PlaybackCountConverter
{
}

/// <summary>艺术家名称转索引字母转换器</summary>
public class NameToLetterConverter : CatClaw.Shared.Maui.Converters.NameToLetterConverter
{
}
