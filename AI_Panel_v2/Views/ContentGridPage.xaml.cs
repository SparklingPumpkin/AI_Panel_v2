using AI_Panel_v2.ViewModels;

using Microsoft.UI.Xaml.Controls;

namespace AI_Panel_v2.Views;

public sealed partial class ContentGridPage : Page
{
    public ContentGridViewModel ViewModel
    {
        get;
    }

    public ContentGridPage()
    {
        ViewModel = App.GetService<ContentGridViewModel>();
        InitializeComponent();
    }
}
