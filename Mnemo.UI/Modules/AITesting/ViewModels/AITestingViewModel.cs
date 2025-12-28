using System;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Mnemo.Core.Services;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Modules.AITesting.ViewModels;

public class AITestingViewModel : ViewModelBase
{
    private readonly IAIOrchestrator _orchestrator;
    private readonly ILoggerService _logger;

    private string _userInput = string.Empty;
    public string UserInput
    {
        get => _userInput;
        set => SetProperty(ref _userInput, value);
    }

    private string _aiResponse = string.Empty;
    public string AIResponse
    {
        get => _aiResponse;
        set => SetProperty(ref _aiResponse, value);
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public ICommand SendPromptCommand { get; }

    public AITestingViewModel(IAIOrchestrator orchestrator, ILoggerService logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;

        SendPromptCommand = new AsyncRelayCommand(SendPromptAsync);
    }

    private async Task SendPromptAsync()
    {
        if (string.IsNullOrWhiteSpace(UserInput)) return;

        IsBusy = true;
        AIResponse = "Thinking...";

        try
        {
            var result = await _orchestrator.PromptAsync("You are a helpful assistant.", UserInput);
            if (result.IsSuccess)
            {
                AIResponse = result.Value ?? "No response received";
            }
            else
            {
                AIResponse = $"Error: {result.ErrorMessage ?? "Unknown error"}";
                _logger.Error("AITesting", $"Prompt failed: {result.ErrorMessage ?? "Unknown error"}");
            }
        }
        catch (Exception ex)
        {
            AIResponse = $"Exception: {ex.Message}";
            _logger.Error("AITesting", "Unexpected error during prompt", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }
}

