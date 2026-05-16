using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Core.Models;
using CatClawMusic.UI.Adapters;
using CatClawMusic.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

/// <summary>
/// 歌单列表Fragment，显示系统歌单和用户创建的歌单
/// </summary>
public class PlaylistFragment : Fragment
{
    private PlaylistViewModel _viewModel = null!;
    private PlaylistAdapter _adapter = null!;
    private RecyclerView _playlistList = null!;
    private TextView _statusText = null!;

    /// <summary>
    /// 创建歌单列表视图
    /// </summary>
    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
    {
        return inflater.Inflate(Resource.Layout.fragment_playlist, container, false)!;
    }

    /// <summary>
    /// 视图创建完成后初始化控件，绑定歌单点击和长按事件
    /// </summary>
    public override void OnViewCreated(View view, Bundle? state)
    {
        base.OnViewCreated(view, state);

        _viewModel = MainApplication.Services.GetRequiredService<PlaylistViewModel>();

        _playlistList = view.FindViewById<RecyclerView>(Resource.Id.playlist_list)!;
        _playlistList.SetLayoutManager(new LinearLayoutManager(Context));

        _statusText = view.FindViewById<TextView>(Resource.Id.status_text)!;

        _adapter = MainApplication.Services.GetRequiredService<PlaylistAdapter>();
        _adapter.PlaylistClicked += (s, playlist) =>
        {
            _viewModel.NavigateToPlaylist(playlist.Id, playlist.Name);
        };
        _adapter.PlaylistLongClicked += (s, playlist) =>
        {
            if (!playlist.IsSystem)
                ShowDeletePlaylistDialog(playlist);
        };
        _playlistList.SetAdapter(_adapter);

        _viewModel.Playlists.CollectionChanged += (s, e) =>
        {
            var a = Activity;
            if (a != null) a.RunOnUiThread(() =>
            {
                _adapter.UpdatePlaylists(_viewModel.Playlists);
                _statusText.Visibility = _viewModel.Playlists.Count == 0
                    ? ViewStates.Visible : ViewStates.Gone;
                _statusText.Text = _viewModel.StatusText;
            });
        };

        _adapter.NewPlaylistClicked += (s, e) => ShowCreatePlaylistDialog();

        _ = _viewModel.LoadPlaylistsAsync();
    }

    /// <summary>
    /// 显示新建歌单对话框
    /// </summary>
    private void ShowCreatePlaylistDialog()
    {
        var editText = new EditText(Context!)
        {
            Hint = "请输入歌单名称",
            InputType = Android.Text.InputTypes.TextFlagCapSentences
        };

        new Android.App.AlertDialog.Builder(Context!)
            .SetTitle("新建歌单")
            .SetView(editText)
            .SetPositiveButton("创建", async (s, e) =>
            {
                var name = editText.Text?.Trim();
                if (!string.IsNullOrEmpty(name))
                    await _viewModel.CreatePlaylistAsync(name);
            })
            .SetNegativeButton("取消", (s, e) => { })
            .Show();
    }

    /// <summary>
    /// 显示删除歌单确认对话框
    /// </summary>
    private void ShowDeletePlaylistDialog(Playlist playlist)
    {
        new Android.App.AlertDialog.Builder(Context!)
            .SetTitle("删除歌单")
            .SetMessage($"确定要删除歌单「{playlist.Name}」吗？\n歌单中的歌曲不会被删除。")
            .SetPositiveButton("删除", async (s, e) =>
            {
                await _viewModel.DeletePlaylistAsync(playlist.Id);
            })
            .SetNegativeButton("取消", (s, e) => { })
            .Show();
    }

    /// <summary>
    /// Fragment恢复可见时重新加载歌单列表
    /// </summary>
    public override void OnResume()
    {
        base.OnResume();
        _ = _viewModel.LoadPlaylistsAsync();
    }
}
