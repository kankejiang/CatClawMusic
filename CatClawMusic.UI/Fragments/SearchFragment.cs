using Android.OS;
using Android.Text;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Services;
using CatClawMusic.UI.Adapters;
using CatClawMusic.UI.Helpers;
using CatClawMusic.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

/// <summary>
/// 探索Fragment，提供歌曲搜索输入框和搜索结果列表
/// </summary>
public class SearchFragment : Fragment
{
    private SearchViewModel _viewModel = null!;
    private EditText _searchInput = null!;
    private RecyclerView _resultsList = null!;
    private TextView _statusText = null!;
    private SongAdapter _adapter = null!;
    private System.Collections.Specialized.NotifyCollectionChangedEventHandler? _collectionChangedHandler;

    /// <summary>
    /// 创建探索视图
    /// </summary>
    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
        => inflater.Inflate(Resource.Layout.fragment_search, container, false)!;

    /// <summary>
    /// 视图创建完成后初始化探索控件，绑定搜索事件和结果点击事件
    /// </summary>
    public override void OnViewCreated(View view, Bundle? state)
    {
        base.OnViewCreated(view, state);
        _viewModel = MainApplication.Services.GetRequiredService<SearchViewModel>();

        _searchInput = view.FindViewById<EditText>(Resource.Id.search_input)!;
        _resultsList = view.FindViewById<RecyclerView>(Resource.Id.search_results)!;
        _resultsList.SetLayoutManager(new LinearLayoutManager(Context));
        _statusText = view.FindViewById<TextView>(Resource.Id.status_text)!;

        _adapter = MainApplication.Services.GetRequiredService<SongAdapter>();
        _adapter.SongClicked += (s, song) =>
        {
            var queue = MainApplication.Services.GetRequiredService<PlayQueue>();
            queue.SetSongs(_viewModel.SearchResults);
            queue.SelectSong(song.Id);
            _ = MainApplication.Services.GetRequiredService<Core.Interfaces.IAudioPlayerService>().PlayAsync(song.FilePath);
            MainApplication.Services.GetRequiredService<Core.Interfaces.INavigationService>().SwitchTab(0);
        };
        _resultsList.SetAdapter(_adapter);
        _resultsList.AddOnScrollListener(new SongAdapter.ScrollListener(_adapter));

        _searchInput.TextChanged += (s, e) => _ = _viewModel.SearchAsync(e?.Text?.ToString() ?? "");

        BindingHelper.BindText(_statusText, _viewModel, nameof(_viewModel.ResultCount), _ => _viewModel.ResultCount);
        _collectionChangedHandler = (s, e) => { var a = Activity; if (a != null) a.RunOnUiThread(() => _adapter.UpdateSongs(_viewModel.SearchResults)); };
        _viewModel.SearchResults.CollectionChanged += _collectionChangedHandler;
    }

    /// <summary>
    /// Fragment销毁时解绑搜索结果集合变化事件
    /// </summary>
    public override void OnDestroyView()
    {
        if (_collectionChangedHandler != null)
            _viewModel.SearchResults.CollectionChanged -= _collectionChangedHandler;
        base.OnDestroyView();
    }
}
