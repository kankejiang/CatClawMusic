namespace CatClawMusic.Core.Interfaces;

/// <summary>导航服务（Android Fragment 回退栈管理）</summary>
public interface INavigationService
{
    /// <summary>推入全屏 Fragment（如 NowPlaying）</summary>
    void PushFragment(string route, System.Collections.Generic.Dictionary<string, object>? parameters = null);
    /// <summary>返回上一页</summary>
    void GoBack();
    /// <summary>切换 Tab（不推入回退栈）</summary>
    void SwitchTab(int tabIndex);
}
