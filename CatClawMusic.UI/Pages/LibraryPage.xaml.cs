using CatClawMusic.Core.Models;

namespace CatClawMusic.UI.Pages;

public partial class LibraryPage : ContentPage
{
    public LibraryPage()
    {
        InitializeComponent();
    }
    
    private void OnSongSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is Song selectedSong)
        {
            // TODO: 播放选中的歌曲
            // await AudioPlayerService.PlayAsync(selectedSong.FilePath);
            
            // 导航到播放页面
            Shell.Current.GoToAsync("NowPlayingPage");
        }
    }
}
