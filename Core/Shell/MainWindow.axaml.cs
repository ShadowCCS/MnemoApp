using Avalonia.Controls;
using Avalonia.Input;
using System.ComponentModel;

namespace MnemoApp.Core.Shell;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        
        // Handle window closing to ensure proper cleanup
        Closing += OnWindowClosing;
    }
    
    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        // Cancel the close to perform cleanup first
        e.Cancel = true;
        
        try
        {
            // Perform clean shutdown of all services
            await ApplicationHost.ShutdownAsync();
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error during window close cleanup: {ex.Message}");
        }
        finally
        {
            // Now actually close the window
            Closing -= OnWindowClosing; // Remove handler to avoid infinite loop
            Close();
        }
    }

    public void app_MouseDown(object sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            this.BeginMoveDrag(e);
        }
    }
}