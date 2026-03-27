using System;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Modules.Settings.ViewModels;

/// <summary>A settings row with a button that runs an async action and shows a status/result message.</summary>
public partial class AsyncActionSettingViewModel : ViewModelBase
{
    [ObservableProperty] private string _title;
    [ObservableProperty] private string _description;
    [ObservableProperty] private string _actionText;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _isRunning;

    public ICommand ActionCommand { get; }

    public AsyncActionSettingViewModel(
        string title,
        string description,
        string actionText,
        Func<AsyncActionSettingViewModel, Task> onAction)
    {
        _title = title;
        _description = description;
        _actionText = actionText;
        ActionCommand = new AsyncRelayCommand(
            () => RunAsync(onAction),
            () => !IsRunning);
    }

    private async Task RunAsync(Func<AsyncActionSettingViewModel, Task> action)
    {
        IsRunning = true;
        StatusText = string.Empty;
        ((AsyncRelayCommand)ActionCommand).NotifyCanExecuteChanged();
        try
        {
            await action(this);
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
            ((AsyncRelayCommand)ActionCommand).NotifyCanExecuteChanged();
        }
    }
}
