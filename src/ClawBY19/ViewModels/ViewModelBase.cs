using CommunityToolkit.Mvvm.ComponentModel;

namespace ClawBY19.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = string.Empty;
}
