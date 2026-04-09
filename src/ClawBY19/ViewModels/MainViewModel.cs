using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClawBY19.Services;

namespace ClawBY19.ViewModels;

public enum ActiveView { Chat, Console, Tasks, Settings }

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty] private ActiveView _activeView = ActiveView.Chat;
    [ObservableProperty] private string _appTitle = "ClawBY19 v2.0";
    [ObservableProperty] private string _currentThemeName = "Dark";

    public ChatViewModel Chat { get; }
    public ConsoleViewModel Console { get; }
    public TasksViewModel Tasks { get; }
    public SettingsViewModel Settings { get; }

    private readonly ThemeService _themeService;

    public MainViewModel(ChatViewModel chat, ConsoleViewModel console,
        TasksViewModel tasks, SettingsViewModel settings, ThemeService themeService)
    {
        Chat = chat;
        Console = console;
        Tasks = tasks;
        Settings = settings;
        _themeService = themeService;

        CurrentThemeName = themeService.CurrentTheme;
        themeService.ThemeChanged += (_, name) => CurrentThemeName = name;

        // 设置页主题变更同步到主窗口底部
        settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.SelectedTheme))
                CurrentThemeName = settings.SelectedTheme;
        };
    }

    /// <summary>当前激活的子 ViewModel，ContentControl + DataTemplate 据此自动选择对应 View</summary>
    public ViewModelBase ActiveViewModel => ActiveView switch
    {
        ActiveView.Chat     => Chat,
        ActiveView.Console  => Console,
        ActiveView.Tasks    => Tasks,
        ActiveView.Settings => Settings,
        _                   => Chat
    };

    partial void OnActiveViewChanged(ActiveView value) =>
        OnPropertyChanged(nameof(ActiveViewModel));

    [RelayCommand] private void ShowChat() => ActiveView = ActiveView.Chat;
    [RelayCommand] private void ShowConsole() => ActiveView = ActiveView.Console;
    [RelayCommand] private void ShowTasks() => ActiveView = ActiveView.Tasks;
    [RelayCommand] private void ShowSettings() => ActiveView = ActiveView.Settings;
}
