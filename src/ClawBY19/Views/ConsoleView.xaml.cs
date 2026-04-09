using System.Windows.Controls;
using ClawBY19.ViewModels;

namespace ClawBY19.Views;

public partial class ConsoleView : UserControl
{
    public ConsoleView() => InitializeComponent();

    private void FlowTab_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is ConsoleViewModel vm)
            vm.SelectedTabIndex = 0;
    }

    private void CostTab_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is ConsoleViewModel vm)
            vm.SelectedTabIndex = 1;
    }
}
