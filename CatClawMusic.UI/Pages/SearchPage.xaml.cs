namespace CatClawMusic.UI.Pages;

public partial class SearchPage : ContentPage
{
    public SearchPage()
    {
        InitializeComponent();
    }
    
    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        // TODO: 实现搜索逻辑
        var keyword = e.NewTextValue;
    }
}
