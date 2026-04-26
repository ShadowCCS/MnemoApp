using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.Input;
using Mnemo.Core.Models;

namespace Mnemo.UI.Components.Overlays;

public sealed class TransferOverlayResult
{
    public required bool IsImport { get; init; }

    public required ImportExportCapability Format { get; init; }

    public bool DuplicateOnConflict { get; init; }

    public bool StrictUnknownPayloads { get; init; }
}

public partial class TransferOverlay : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<TransferOverlay, string>(nameof(Title), "Transfer");

    public static readonly StyledProperty<string> DescriptionProperty =
        AvaloniaProperty.Register<TransferOverlay, string>(nameof(Description), string.Empty);

    public static readonly StyledProperty<string> ConfirmTextProperty =
        AvaloniaProperty.Register<TransferOverlay, string>(nameof(ConfirmText), "Continue");

    public static readonly StyledProperty<string> CancelTextProperty =
        AvaloniaProperty.Register<TransferOverlay, string>(nameof(CancelText), "Cancel");

    public static readonly StyledProperty<bool> IsImportModeProperty =
        AvaloniaProperty.Register<TransferOverlay, bool>(nameof(IsImportMode), true);

    public static readonly StyledProperty<bool> IsExportModeProperty =
        AvaloniaProperty.Register<TransferOverlay, bool>(nameof(IsExportMode), false);

    public static readonly StyledProperty<bool> DuplicateOnConflictProperty =
        AvaloniaProperty.Register<TransferOverlay, bool>(nameof(DuplicateOnConflict), true);

    public static readonly StyledProperty<bool> StrictUnknownPayloadsProperty =
        AvaloniaProperty.Register<TransferOverlay, bool>(nameof(StrictUnknownPayloads), false);

    public static readonly StyledProperty<ImportExportCapability?> SelectedFormatProperty =
        AvaloniaProperty.Register<TransferOverlay, ImportExportCapability?>(nameof(SelectedFormat));

    private readonly List<ImportExportCapability> _allCapabilities = new();

    public TransferOverlay()
    {
        ConfirmCommand = new RelayCommand(OnConfirm);
        CancelCommand = new RelayCommand(() => OnResult?.Invoke(null));
        SelectImportCommand = new RelayCommand(() => IsImportMode = true);
        SelectExportCommand = new RelayCommand(() => IsImportMode = false);
        InitializeComponent();
        DataContext = this;
        PropertyChanged += OnOverlayPropertyChanged;
    }

    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    public string Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }

    public string ConfirmText { get => GetValue(ConfirmTextProperty); set => SetValue(ConfirmTextProperty, value); }

    public string CancelText { get => GetValue(CancelTextProperty); set => SetValue(CancelTextProperty, value); }

    public bool IsImportMode { get => GetValue(IsImportModeProperty); set => SetValue(IsImportModeProperty, value); }

    public bool IsExportMode { get => GetValue(IsExportModeProperty); private set => SetValue(IsExportModeProperty, value); }

    public bool DuplicateOnConflict { get => GetValue(DuplicateOnConflictProperty); set => SetValue(DuplicateOnConflictProperty, value); }

    public bool StrictUnknownPayloads { get => GetValue(StrictUnknownPayloadsProperty); set => SetValue(StrictUnknownPayloadsProperty, value); }

    public ImportExportCapability? SelectedFormat
    {
        get => GetValue(SelectedFormatProperty);
        set => SetValue(SelectedFormatProperty, value);
    }

    public ObservableCollection<ImportExportCapability> AvailableFormats { get; } = new();

    public ICommand ConfirmCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand SelectImportCommand { get; }

    public ICommand SelectExportCommand { get; }

    public Action<TransferOverlayResult?>? OnResult { get; set; }

    public void Initialize(IReadOnlyList<ImportExportCapability> capabilities, bool defaultImport = true)
    {
        _allCapabilities.Clear();
        _allCapabilities.AddRange(capabilities);
        IsImportMode = defaultImport;
        IsExportMode = !IsImportMode;
        RebuildFormats();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void RebuildFormats()
    {
        AvailableFormats.Clear();
        foreach (var capability in _allCapabilities.Where(c => IsImportMode ? c.SupportsImport : c.SupportsExport))
            AvailableFormats.Add(capability);

        if (SelectedFormat == null || !AvailableFormats.Contains(SelectedFormat))
            SelectedFormat = AvailableFormats.FirstOrDefault();
    }

    private void OnConfirm()
    {
        if (SelectedFormat == null)
            return;

        OnResult?.Invoke(new TransferOverlayResult
        {
            IsImport = IsImportMode,
            Format = SelectedFormat,
            DuplicateOnConflict = DuplicateOnConflict,
            StrictUnknownPayloads = StrictUnknownPayloads
        });
    }

    private void OnOverlayPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsImportModeProperty)
        {
            IsExportMode = !IsImportMode;
            RebuildFormats();
        }
    }
}
