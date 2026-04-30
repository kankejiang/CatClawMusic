using AndroidX.Fragment.App;
using AndroidX.ViewPager2.Adapter;

namespace CatClawMusic.UI;

public class TabPagerAdapter : FragmentStateAdapter
{
    private readonly Fragment[] _fragments;

    public TabPagerAdapter(FragmentActivity activity, Fragment[] fragments) : base(activity)
    {
        _fragments = fragments;
    }

    public override Fragment CreateFragment(int position) => _fragments[position];

    public override int ItemCount => _fragments.Length;
}
