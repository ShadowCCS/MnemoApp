using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.Input;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

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
    public static readonly StyledProperty<string> ImportTabTextProperty =
        AvaloniaProperty.Register<TransferOverlay, string>(nameof(ImportTabText), "Import");

    public static readonly StyledProperty<string> ExportTabTextProperty =
        AvaloniaProperty.Register<TransferOverlay, string>(nameof(ExportTabText), "Export");

    public static readonly StyledProperty<string> FileFormatTitleProperty =
        AvaloniaProperty.Register<TransferOverlay, string>(nameof(FileFormatTitle), "File format");

    public static readonly StyledProperty<string> FileFormatDescriptionProperty =
        AvaloniaProperty.Register<TransferOverlay, string>(nameof(FileFormatDescription), string.Empty);

    public static readonly StyledProperty<string> ImportBodyHintProperty =
        AvaloniaProperty.Register<TransferOverlay, string>(nameof(ImportBodyHint), string.Empty);

    public static readonly StyledProperty<string> ExportBodyHintProperty =
        AvaloniaProperty.Register<TransferOverlay, string>(nameof(ExportBodyHint), string.Empty);

    public static readonly StyledProperty<string> ImportSettingsTitleProperty =
        AvaloniaProperty.Register<TransferOverlay, string>(nameof(ImportSettingsTitle), "Import settings");

    public static readonly StyledProperty<string> ImportSettingsDescriptionProperty =
        AvaloniaProperty.Register<TransferOverlay, string>(nameof(ImportSettingsDescription), string.Empty);

    public static readonly StyledProperty<string> DuplicateOnConflictLabelProperty =
        AvaloniaProperty.Register<TransferOverlay, string>(nameof(DuplicateOnConflictLabel), "Duplicate on conflict");

    public static readonly StyledProperty<string> StrictUnknownPayloadsLabelProperty =
        AvaloniaProperty.Register<TransferOverlay, string>(nameof(StrictUnknownPayloadsLabel), "Strict unknown payload handling");

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
    private ILocalizationService? _localization;

    private (string Key, string Ns)? _localizedTitle;
    private (string Key, string Ns)? _localizedDescription;
    private (string Key, string Ns)? _localizedConfirm;
    private (string Key, string Ns)? _localizedCancel;

    /// <summary>
    /// Binds header and footer strings to localization keys so they stay correct when the user changes language while the overlay is open.
    /// </summary>
    public void SetLocalizedChrome(
        string? titleKey,
        string? titleNs,
        string? descriptionKey,
        string? descriptionNs,
        string? confirmKey,
        string? confirmNs,
        string? cancelKey,
        string? cancelNs)
    {
        _localizedTitle = NonEmptyPair(titleKey, titleNs);
        _localizedDescription = NonEmptyPair(descriptionKey, descriptionNs);
        _localizedConfirm = NonEmptyPair(confirmKey, confirmNs);
        _localizedCancel = NonEmptyPair(cancelKey, cancelNs);
        ApplyTransferChromeStrings();
    }

    private static (string Key, string Ns)? NonEmptyPair(string? key, string? ns) =>
        string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(ns) ? null : (key!, ns!);

    public TransferOverlay()
    {
        ConfirmCommand = new RelayCommand(OnConfirm);
        CancelCommand = new RelayCommand(() => OnResult?.Invoke(null));
        SelectImportCommand = new RelayCommand(() => IsImportMode = true);
        SelectExportCommand = new RelayCommand(() => IsImportMode = false);
        InitializeComponent();
        DataContext = this;
        PropertyChanged += OnOverlayPropertyChanged;
        Loaded += OnLoaded;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    public string ImportTabText { get => GetValue(ImportTabTextProperty); set => SetValue(ImportTabTextProperty, value); }

    public string ExportTabText { get => GetValue(ExportTabTextProperty); set => SetValue(ExportTabTextProperty, value); }

    public string FileFormatTitle { get => GetValue(FileFormatTitleProperty); set => SetValue(FileFormatTitleProperty, value); }

    public string FileFormatDescription { get => GetValue(FileFormatDescriptionProperty); set => SetValue(FileFormatDescriptionProperty, value); }

    public string ImportBodyHint { get => GetValue(ImportBodyHintProperty); set => SetValue(ImportBodyHintProperty, value); }

    public string ExportBodyHint { get => GetValue(ExportBodyHintProperty); set => SetValue(ExportBodyHintProperty, value); }

    public string ImportSettingsTitle { get => GetValue(ImportSettingsTitleProperty); set => SetValue(ImportSettingsTitleProperty, value); }

    public string ImportSettingsDescription { get => GetValue(ImportSettingsDescriptionProperty); set => SetValue(ImportSettingsDescriptionProperty, value); }

    public string DuplicateOnConflictLabel { get => GetValue(DuplicateOnConflictLabelProperty); set => SetValue(DuplicateOnConflictLabelProperty, value); }

    public string StrictUnknownPayloadsLabel { get => GetValue(StrictUnknownPayloadsLabelProperty); set => SetValue(StrictUnknownPayloadsLabelProperty, value); }
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

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        AttachLocalization();
        ApplyTransferChromeStrings();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_localization != null)
            _localization.LanguageChanged -= OnLanguageChanged;
        _localization = null;
        _localizedTitle = _localizedDescription = _localizedConfirm = _localizedCancel = null;
    }

    private void AttachLocalization()
    {
        if (Application.Current is not App app)
            return;
        if (_localization != null)
        {
            ApplyTransferChromeStrings();
            return;
        }
        _localization = app.Services?.GetService(typeof(ILocalizationService)) as ILocalizationService;
        if (_localization != null)
            _localization.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => ApplyTransferChromeStrings();

    private void ApplyTransferChromeStrings()
    {
        if (Application.Current is not App app)
            return;
        var loc = _localization ?? app.Services?.GetService(typeof(ILocalizationService)) as ILocalizationService;
        if (loc == null)
            return;

        if (_localizedTitle is var (tk, tns))
            Title = loc.T(tk, tns);
        if (_localizedDescription is var (dk, dns))
            Description = loc.T(dk, dns);
        if (_localizedConfirm is var (ck, cns))
            ConfirmText = loc.T(ck, cns);
        if (_localizedCancel is var (cnk, cnns))
            CancelText = loc.T(cnk, cnns);

        ImportTabText = loc.T("TransferImportTab", "Common");
        ExportTabText = loc.T("TransferExportTab", "Common");
        FileFormatTitle = loc.T("TransferFileFormatTitle", "Common");
        FileFormatDescription = loc.T("TransferFileFormatDescription", "Common");
        ImportBodyHint = loc.T("TransferImportBodyHint", "Common");
        ExportBodyHint = loc.T("TransferExportBodyHint", "Common");
        ImportSettingsTitle = loc.T("TransferImportSettingsTitle", "Common");
        ImportSettingsDescription = loc.T("TransferImportSettingsDescription", "Common");
        DuplicateOnConflictLabel = loc.T("TransferDuplicateOnConflict", "Common");
        StrictUnknownPayloadsLabel = loc.T("TransferStrictUnknownPayloads", "Common");

        if (_localizedCancel == null && (string.IsNullOrEmpty(CancelText) || CancelText == "Cancel"))
            CancelText = loc.T("Cancel", "Common");
        if (_localizedConfirm == null && (string.IsNullOrEmpty(ConfirmText) || ConfirmText == "Continue"))
            ConfirmText = loc.T("Continue", "Common");
    }

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
