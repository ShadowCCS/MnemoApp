using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.Input;
using Mnemo.Core.Models.Flashcards;

namespace Mnemo.UI.Components.Overlays;

public sealed record DeckAlgorithmOption(FlashcardSchedulingAlgorithm Algorithm, string Label);

public sealed record DeckFolderOption(string? FolderId, string Label);

public sealed class FlashcardDeckSettingsResult
{
    public FlashcardSchedulingAlgorithm SchedulingAlgorithm { get; init; }

    public string? FolderId { get; init; }

    public string? Description { get; init; }
}

public partial class FlashcardDeckSettingsOverlay : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<FlashcardDeckSettingsOverlay, string>(nameof(Title), "Deck Settings");

    public static readonly StyledProperty<string> DeckNameProperty =
        AvaloniaProperty.Register<FlashcardDeckSettingsOverlay, string>(nameof(DeckName), string.Empty);

    public static readonly StyledProperty<string> SaveTextProperty =
        AvaloniaProperty.Register<FlashcardDeckSettingsOverlay, string>(nameof(SaveText), "Save");

    public static readonly StyledProperty<string> CancelTextProperty =
        AvaloniaProperty.Register<FlashcardDeckSettingsOverlay, string>(nameof(CancelText), "Cancel");

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<FlashcardDeckSettingsOverlay, string?>(nameof(Description));

    public static readonly StyledProperty<DeckAlgorithmOption?> SelectedAlgorithmOptionProperty =
        AvaloniaProperty.Register<FlashcardDeckSettingsOverlay, DeckAlgorithmOption?>(nameof(SelectedAlgorithmOption));

    public static readonly StyledProperty<DeckFolderOption?> SelectedFolderOptionProperty =
        AvaloniaProperty.Register<FlashcardDeckSettingsOverlay, DeckFolderOption?>(nameof(SelectedFolderOption));

    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string DeckName { get => GetValue(DeckNameProperty); set => SetValue(DeckNameProperty, value); }
    public string SaveText { get => GetValue(SaveTextProperty); set => SetValue(SaveTextProperty, value); }
    public string CancelText { get => GetValue(CancelTextProperty); set => SetValue(CancelTextProperty, value); }
    public string? Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }

    public ObservableCollection<DeckAlgorithmOption> AlgorithmOptions { get; } = new();
    public ObservableCollection<DeckFolderOption> FolderOptions { get; } = new();

    public DeckAlgorithmOption? SelectedAlgorithmOption
    {
        get => GetValue(SelectedAlgorithmOptionProperty);
        set => SetValue(SelectedAlgorithmOptionProperty, value);
    }

    public DeckFolderOption? SelectedFolderOption
    {
        get => GetValue(SelectedFolderOptionProperty);
        set => SetValue(SelectedFolderOptionProperty, value);
    }

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    public Action<FlashcardDeckSettingsResult?>? OnResult { get; set; }

    public FlashcardDeckSettingsOverlay()
    {
        SaveCommand = new RelayCommand(OnSave);
        CancelCommand = new RelayCommand(() => OnResult?.Invoke(null));
        InitializeComponent();
        DataContext = this;
    }

    public void Initialize(
        string deckName,
        FlashcardSchedulingAlgorithm algorithm,
        string? folderId,
        string? description,
        IReadOnlyList<FlashcardFolder> folders)
    {
        DeckName = deckName;
        Description = description;

        AlgorithmOptions.Clear();
        AlgorithmOptions.Add(new DeckAlgorithmOption(FlashcardSchedulingAlgorithm.Fsrs, "Smart (FSRS, Anki-style)"));
        AlgorithmOptions.Add(new DeckAlgorithmOption(FlashcardSchedulingAlgorithm.Sm2, "Classic (SM-2, Anki legacy)"));
        AlgorithmOptions.Add(new DeckAlgorithmOption(FlashcardSchedulingAlgorithm.Leitner, "Boxed (Leitner, Quizlet-style)"));
        AlgorithmOptions.Add(new DeckAlgorithmOption(FlashcardSchedulingAlgorithm.Baseline, "Simple (fixed intervals)"));
        SelectedAlgorithmOption = AlgorithmOptions.FirstOrDefault(a => a.Algorithm == algorithm) ?? AlgorithmOptions.First();

        FolderOptions.Clear();
        FolderOptions.Add(new DeckFolderOption(null, "No folder"));
        foreach (var folder in folders.OrderBy(f => f.Order).ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase))
            FolderOptions.Add(new DeckFolderOption(folder.Id, folder.Name));
        SelectedFolderOption = FolderOptions.FirstOrDefault(f => string.Equals(f.FolderId, folderId, StringComparison.Ordinal))
                               ?? FolderOptions.First();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnSave()
    {
        var selectedAlgorithm = SelectedAlgorithmOption?.Algorithm ?? FlashcardSchedulingAlgorithm.Fsrs;
        var selectedFolder = SelectedFolderOption?.FolderId;
        var cleanedDescription = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
        OnResult?.Invoke(new FlashcardDeckSettingsResult
        {
            SchedulingAlgorithm = selectedAlgorithm,
            FolderId = selectedFolder,
            Description = cleanedDescription
        });
    }
}
