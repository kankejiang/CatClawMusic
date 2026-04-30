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

public class SearchFragment : Fragment
{
    private SearchViewModel _viewModel = null!;
    private EditText _searchInput = null!;
    private RecyclerView _resultsList = null!;
    private TextView _statusText = null!;
    private SongAdapter _adapter = null!;

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
        => inflater.Inflate(Resource.Layout.fragment_search, container, false)!;

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

        _searchInput.TextChanged += (s, e) => _ = _viewModel.SearchAsync(e?.Text?.ToString() ?? "");

        BindingHelper.BindText(_statusText, _viewModel, nameof(_viewModel.ResultCount), _ => _viewModel.ResultCount);
        _viewModel.SearchResults.CollectionChanged += (s, e) => { var a = Activity; if (a != null) a.RunOnUiThread(() => _adapter.UpdateSongs(_viewModel.SearchResults)); };
    }
}
