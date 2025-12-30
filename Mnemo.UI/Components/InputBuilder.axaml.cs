using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Mnemo.UI.Components;

public partial class InputBuilder : UserControl
{
    public static readonly DirectProperty<InputBuilder, string> TextProperty =
        AvaloniaProperty.RegisterDirect<InputBuilder, string>(
            nameof(Text),
            o => o.Text,
            (o, v) => o.Text = v);

    private string _text = string.Empty;
    public string Text
    {
        get => _text;
        set => SetAndRaise(TextProperty, ref _text, value);
    }

    public ObservableCollection<string> Files { get; } = new();

    public event EventHandler<(string text, string[] files)>? GenerateRequested;

    public InputBuilder()
    {
        InitializeComponent();
        FilesItemsControl.ItemsSource = Files;
        
        InputTextBox.TextChanged += (s, e) => Text = InputTextBox.Text ?? string.Empty;
    }

    private async void AddFile_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Source Materials",
            AllowMultiple = true
        });

        if (files != null)
        {
            foreach (var file in files)
            {
                var path = file.Path.LocalPath;
                if (!Files.Contains(path))
                {
                    Files.Add(path);
                }
            }
        }
    }

    private void RemoveFile_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is string path)
        {
            Files.Remove(path);
        }
    }

    private void Generate_Click(object? sender, RoutedEventArgs e)
    {
        GenerateRequested?.Invoke(this, (Text, Files.ToArray()));
    }
}



