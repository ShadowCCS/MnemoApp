using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Mnemo.UI.Modules.Chat.ViewModels;

namespace Mnemo.UI.Modules.Chat.Views;

public partial class ChatView : UserControl
{
    private ScrollViewer? _chatScrollViewer;
    private ChatViewModel? _currentVm;

    public ChatView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _chatScrollViewer = this.FindControl<ScrollViewer>("ChatScrollViewer");
        if (_chatScrollViewer != null)
            _chatScrollViewer.ScrollChanged += OnScrollChanged;
        DataContextChanged += OnDataContextChanged;
        AttachViewModel(DataContext as ChatViewModel);
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        DataContextChanged -= OnDataContextChanged;
        AttachViewModel(null);
        if (_chatScrollViewer != null)
        {
            _chatScrollViewer.ScrollChanged -= OnScrollChanged;
            _chatScrollViewer = null;
        }
        base.OnDetachedFromVisualTree(e);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        AttachViewModel(DataContext as ChatViewModel);
    }

    private void AttachViewModel(ChatViewModel? vm)
    {
        if (_currentVm != null)
        {
            _currentVm.RequestScrollToBottom -= OnRequestScrollToBottom;
            _currentVm = null;
        }
        if (vm != null)
        {
            _currentVm = vm;
            vm.RequestScrollToBottom += OnRequestScrollToBottom;
            OnRequestScrollToBottom(vm, EventArgs.Empty);
        }
    }

    private void OnRequestScrollToBottom(object? sender, EventArgs e)
    {
        _chatScrollViewer?.ScrollToEnd();
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_chatScrollViewer == null || DataContext is not ChatViewModel vm) return;
        vm.NotifyScrollPosition(
            _chatScrollViewer.Offset.Y,
            _chatScrollViewer.Extent.Height,
            _chatScrollViewer.Viewport.Height);
    }

    private void InputBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None) return;
        if (DataContext is not ChatViewModel vm) return;
        if (vm.SendMessageCommand.CanExecute(null))
        {
            vm.SendMessageCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
