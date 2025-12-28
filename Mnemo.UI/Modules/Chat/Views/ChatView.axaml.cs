using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Mnemo.UI.Modules.Chat.ViewModels;

namespace Mnemo.UI.Modules.Chat.Views;

public partial class ChatView : UserControl
{
    private TextBox? _inputBox;

    public ChatView()
    {
        InitializeComponent();
        _inputBox = this.FindControl<TextBox>("InputBox");
    }

    private void InputBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
        {
            var vm = DataContext as ChatViewModel;
            if (vm != null && vm.SendMessageCommand.CanExecute(null))
            {
                vm.SendMessageCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

