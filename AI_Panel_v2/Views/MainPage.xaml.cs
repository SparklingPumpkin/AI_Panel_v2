using AI_Panel_v2.ViewModels;

using Microsoft.UI.Xaml.Controls;

namespace AI_Panel_v2.Views;

public sealed partial class MainPage : Page
{
    public MainViewModel ViewModel
    {
        get;
    }

    public MainPage()
    {
        ViewModel = App.GetService<MainViewModel>();
        InitializeComponent();
    }
}
