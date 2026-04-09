using ClawBY19.ViewModels;
using System.Windows.Controls;
using System.Windows.Input;

namespace ClawBY19.Views;

public partial class TasksView : UserControl
{
    public TasksView() => InitializeComponent();

    private void OperationsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is TasksViewModel vm &&
            OperationsGrid.SelectedItem is TaskRecordItem item)
        {
            vm.OpenDetailCommand.Execute(item);
        }
    }
}
