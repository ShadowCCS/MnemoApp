namespace Mnemo.UI.ViewModels;

public enum ChatProcessPhaseKind
{
    Routing,
    Model,
    Generating,
    Tool,
    Continuing
}

/// <summary>One line in the assistant message process thread (routing → model → tools → …).</summary>
public class ChatProcessStepViewModel : ViewModelBase
{
    private string _label = string.Empty;
    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value);
    }

    private string? _detail;
    /// <summary>Optional sub-label (e.g. tool name).</summary>
    public string? Detail
    {
        get => _detail;
        set => SetProperty(ref _detail, value);
    }

    private bool _isComplete;
    public bool IsComplete
    {
        get => _isComplete;
        set => SetProperty(ref _isComplete, value);
    }

    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public ChatProcessPhaseKind PhaseKind { get; set; }
}
