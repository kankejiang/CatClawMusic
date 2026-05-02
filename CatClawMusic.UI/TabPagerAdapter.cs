using AndroidX.Fragment.App;
using AndroidX.ViewPager2.Adapter;
using CatClawMusic.UI.Fragments;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI;

public class TabPagerAdapter : FragmentStateAdapter
{
    private readonly FragmentActivity _activity;

    public TabPagerAdapter(FragmentActivity activity) : base(activity)
    {
        _activity = activity;
    }

    public override Fragment CreateFragment(int position) => position switch
    {
        0 => MainApplication.Services.GetRequiredService<FullLyricsFragment>(),
        1 => MainApplication.Services.GetRequiredService<NowPlayingFragment>(),
        2 => MainApplication.Services.GetRequiredService<PlaylistFragment>(),
        3 => MainApplication.Services.GetRequiredService<SearchFragment>(),
        4 => MainApplication.Services.GetRequiredService<LibraryFragment>(),
        _ => throw new ArgumentOutOfRangeException(nameof(position))
    };

    public override int ItemCount => 5;

    // 稳定 ID，避免 FragmentStateAdapter 误判重复
    public override long GetItemId(int position) => position;
    public override bool ContainsItem(long itemId) => itemId >= 0 && itemId < ItemCount;
}
