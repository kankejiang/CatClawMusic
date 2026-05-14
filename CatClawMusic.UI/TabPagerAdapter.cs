using AndroidX.Fragment.App;
using AndroidX.ViewPager2.Adapter;
using CatClawMusic.UI.Fragments;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI;

public class TabPagerAdapter : FragmentStateAdapter
{
    private readonly FragmentActivity _activity;

    // 缓存已创建的 Fragment 引用，避免 DI Transient 导致每次创建新实例
    private FullLyricsFragment? _fullLyricsFragment;
    private NowPlayingFragment? _nowPlayingFragment;
    private PlaylistFragment? _playlistFragment;
    private SearchFragment? _searchFragment;
    private LibraryFragment? _libraryFragment;

    public TabPagerAdapter(FragmentActivity activity) : base(activity)
    {
        _activity = activity;
    }

    public override Fragment CreateFragment(int position) => position switch
    {
        0 => _fullLyricsFragment ??= MainApplication.Services.GetRequiredService<FullLyricsFragment>(),
        1 => _nowPlayingFragment ??= MainApplication.Services.GetRequiredService<NowPlayingFragment>(),
        2 => _playlistFragment ??= MainApplication.Services.GetRequiredService<PlaylistFragment>(),
        3 => _searchFragment ??= MainApplication.Services.GetRequiredService<SearchFragment>(),
        4 => _libraryFragment ??= MainApplication.Services.GetRequiredService<LibraryFragment>(),
        _ => throw new ArgumentOutOfRangeException(nameof(position))
    };

    public override int ItemCount => 5;

    // 稳定 ID，避免 FragmentStateAdapter 误判重复
    public override long GetItemId(int position) => position;
    public override bool ContainsItem(long itemId) => itemId >= 0 && itemId < ItemCount;

    /// <summary>
    /// 获取歌词页 Fragment 实例（可能为 null，如果尚未创建）
    /// </summary>
    public FullLyricsFragment? FullLyricsFragment => _fullLyricsFragment;
}
