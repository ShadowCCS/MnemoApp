using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Mnemo.Core.Models;
using Mnemo.UI.Modules.Chat.ViewModels;
using Mnemo.UI.Services;

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

    private async void AddAttachment_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null || DataContext is not ChatViewModel vm) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select files",
            AllowMultiple = true
        }).ConfigureAwait(true);

        foreach (var file in files ?? Enumerable.Empty<IStorageFile>())
        {
            var path = file.Path.LocalPath;
            var kind = ChatViewModel.IsImagePath(path) ? ChatAttachmentKind.Image : ChatAttachmentKind.File;
            vm.AddPendingAttachment(path, kind);
        }
    }

    private async void AddImage_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null || DataContext is not ChatViewModel vm) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select image(s)",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Images")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp", "*.bmp" }
                }
            }
        }).ConfigureAwait(true);

        foreach (var file in files ?? Enumerable.Empty<IStorageFile>())
        {
            var path = file.Path.LocalPath;
            vm.AddPendingAttachment(path, ChatAttachmentKind.Image);
        }
    }

    private void AddScreenshot_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null || DataContext is not ChatViewModel vm) return;

        var path = ScreenshotService.CaptureToTempFile(topLevel);
        if (!string.IsNullOrEmpty(path))
            vm.AddPendingAttachment(path, ChatAttachmentKind.Image, "Screenshot");
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
