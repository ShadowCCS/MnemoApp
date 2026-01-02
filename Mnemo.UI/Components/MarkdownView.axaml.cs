using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Mnemo.Core.Services;
using Mnemo.UI.Services;

namespace Mnemo.UI.Components;

/// <summary>
/// A view that renders Markdown content using <see cref="IMarkdownRenderer"/> and <see cref="IMarkdownProcessor"/>.
/// </summary>
public partial class MarkdownView : UserControl
{
    private readonly IMarkdownProcessor _markdownProcessor;
    private readonly IMarkdownRenderer _markdownRenderer;

    public static readonly StyledProperty<string?> SourceProperty =
        AvaloniaProperty.Register<MarkdownView, string?>(nameof(Source));

    public string? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    private ContentControl? _contentHost;
    private bool _isRendering = false;
    private bool _renderRequested = false;

    public MarkdownView()
    {
        var sp = ((App)Application.Current!).Services!;
        _markdownProcessor = sp.GetRequiredService<IMarkdownProcessor>();
        _markdownRenderer = sp.GetRequiredService<IMarkdownRenderer>();
        
        var settings = sp.GetRequiredService<ISettingsService>();
        settings.SettingChanged += OnSettingChanged;

        InitializeComponent();
        _contentHost = this.FindControl<ContentControl>("ContentHost");
    }

    private void OnSettingChanged(object? sender, string key)
    {
        if (key.StartsWith("Markdown."))
        {
            _ = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(RenderAsync);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _ = RenderAsync();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        var sp = ((App)Application.Current!).Services!;
        var settings = sp.GetRequiredService<ISettingsService>();
        settings.SettingChanged -= OnSettingChanged;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SourceProperty || 
            change.Property == ForegroundProperty || 
            change.Property == IsVisibleProperty)
        {
            _ = RenderAsync();
        }
    }

    private async Task RenderAsync()
    {
        if (_contentHost == null) return;

        // If we are not visible, we can delay rendering until we are, 
        // unless we already have content and just need to clear it.
        var currentSource = Source;
        if (!IsVisible && !string.IsNullOrWhiteSpace(currentSource)) return;

        if (string.IsNullOrWhiteSpace(currentSource))
        {
            _contentHost.Content = null;
            return;
        }

        if (_isRendering)
        {
            _renderRequested = true;
            return;
        }

        _isRendering = true;

        try
        {
            do
            {
                _renderRequested = false;
                currentSource = Source;
                
                if (string.IsNullOrEmpty(currentSource)) break;

                // Process special inlines (LaTeX, etc.) on a background thread
                var (processedSource, specialInlines) = await Task.Run(() => 
                    _markdownProcessor.ExtractSpecialInlines(currentSource));

                // Render the processed markdown - must be on UI thread
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    try 
                    {
                        var renderedControl = await _markdownRenderer.RenderAsync(processedSource, specialInlines, Foreground);
                        _contentHost.Content = renderedControl;
                    }
                    catch (Exception ex)
                    {
                        _contentHost.Content = new TextBlock
                        {
                            Text = $"Error rendering markdown: {ex.Message}",
                            Foreground = Brushes.Red,
                            TextWrapping = TextWrapping.Wrap
                        };
                    }
                });

            } while (_renderRequested);
        }
        catch (Exception ex)
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                _contentHost.Content = new TextBlock
                {
                    Text = $"Error processing content: {ex.Message}",
                    Foreground = (IBrush?)Application.Current?.FindResource("SystemErrorBackgroundBrush") ?? Brushes.Red,
                    TextWrapping = TextWrapping.Wrap
                };
            });
        }
        finally
        {
            _isRendering = false;
        }
    }
}
