using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using Mnemo.UI.Modules.Flashcards.ViewModels;
using System;
using System.ComponentModel;

namespace Mnemo.UI.Modules.Flashcards.Views;

public partial class FlashcardsView : UserControl, INotifyPropertyChanged
{
    public new event PropertyChangedEventHandler? PropertyChanged;

    public IRelayCommand<string?>? SelectFolderCommandProxy => (DataContext as FlashcardsViewModel)?.SelectFolderCommand;
    public IRelayCommand<FlashcardDeckRowViewModel?>? StartQuickSessionCommandProxy => (DataContext as FlashcardsViewModel)?.StartQuickSessionCommand;

    public FlashcardsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectFolderCommandProxy)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StartQuickSessionCommandProxy)));
    }

    private void OnDeckCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border || DataContext is not FlashcardsViewModel vm)
            return;

        if (border.DataContext is not FlashcardDeckRowViewModel row)
            return;

        if (!e.GetCurrentPoint(border).Properties.IsLeftButtonPressed)
            return;
        if (e.Source is StyledElement source)
        {
            StyledElement? current = source;
            while (current is not null)
            {
                if (current is Button)
                    return;
                current = current.Parent as StyledElement;
            }
        }

        if (vm.OpenDeckCommand.CanExecute(row))
            vm.OpenDeckCommand.Execute(row);

        e.Handled = true;
    }
}
