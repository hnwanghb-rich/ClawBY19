using ClawBY19.ViewModels;
using System.Windows;

namespace ClawBY19.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
