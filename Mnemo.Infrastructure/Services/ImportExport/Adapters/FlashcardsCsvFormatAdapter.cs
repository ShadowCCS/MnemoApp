using System.Text;
using Mnemo.Core.Models;
using Mnemo.Core.Models.Flashcards;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.ImportExport.Adapters;

public sealed class FlashcardsCsvFormatAdapter : IContentFormatAdapter
{
    private readonly IFlashcardDeckService _deckService;

    public FlashcardsCsvFormatAdapter(IFlashcardDeckService deckService)
    {
        _deckService = deckService;
    }

    public string ContentType => "flashcards";
    public string FormatId => "flashcards.csv";
    public string DisplayName => "CSV (.csv)";
    public IReadOnlyList<string> Extensions => [".csv"];
    public bool SupportsImport => true;
    public bool SupportsExport => true;

    public Task<ImportExportPreview> PreviewImportAsync(ImportExportRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ImportExportPreview
        {
            CanImport = true,
            ContentType = ContentType,
            FormatId = FormatId,
            DiscoveredCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["flashcards"] = 1 }
        });
    }

    public async Task<ImportExportResult> ImportAsync(ImportExportRequest request, CancellationToken cancellationToken = default)
    {
        var lines = await File.ReadAllLinesAsync(request.FilePath, cancellationToken).ConfigureAwait(false);
        if (lines.Length == 0)
        {
            return new ImportExportResult
            {
                Success = false,
                ContentType = ContentType,
                FormatId = FormatId,
                ErrorMessage = "CSV file is empty."
            };
        }

        var cards = new List<Flashcard>();
        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
                continue;
            var parts = ParseCsvLine(lines[i]);
            if (parts.Count < 2)
                continue;

            cards.Add(new Flashcard(
                Id: Guid.NewGuid().ToString(),
                DeckId: string.Empty,
                Front: parts[0],
                Back: parts[1],
                Type: FlashcardType.Classic,
                Tags: Array.Empty<string>(),
                DueDate: DateTimeOffset.UtcNow,
                Stability: null,
                Difficulty: null,
                Retrievability: null));
        }

        var deck = new FlashcardDeck(
            Id: Guid.NewGuid().ToString(),
            Name: Path.GetFileNameWithoutExtension(request.FilePath),
            FolderId: null,
            Description: "Imported from CSV",
            Tags: Array.Empty<string>(),
            LastStudied: null,
            RetentionScore: 0,
            Cards: cards,
            SchedulingAlgorithm: FlashcardSchedulingAlgorithm.Fsrs);

        var rebasedCards = cards.Select(c => c with { DeckId = deck.Id }).ToArray();
        deck = deck with { Cards = rebasedCards };
        await _deckService.SaveDeckAsync(deck, cancellationToken).ConfigureAwait(false);

        return new ImportExportResult
        {
            Success = true,
            ContentType = ContentType,
            FormatId = FormatId,
            ProcessedCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["decks"] = 1,
                ["flashcards"] = rebasedCards.Length
            }
        };
    }

    public async Task<ImportExportResult> ExportAsync(ImportExportRequest request, CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        int exportedCards;
        if (request.Payload is FlashcardDeck deck)
        {
            sb.AppendLine("front,back");
            foreach (var card in deck.Cards)
                sb.AppendLine($"{EscapeCsv(card.Front)},{EscapeCsv(card.Back)}");
            exportedCards = deck.Cards.Count;
        }
        else
        {
            sb.AppendLine("deck,front,back");
            var decks = await _deckService.ListDecksAsync(cancellationToken).ConfigureAwait(false);
            exportedCards = 0;
            foreach (var currentDeck in decks)
            {
                foreach (var card in currentDeck.Cards)
                {
                    sb.AppendLine($"{EscapeCsv(currentDeck.Name)},{EscapeCsv(card.Front)},{EscapeCsv(card.Back)}");
                    exportedCards++;
                }
            }
        }

        await File.WriteAllTextAsync(request.FilePath, sb.ToString(), Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        return new ImportExportResult
        {
            Success = true,
            ContentType = ContentType,
            FormatId = FormatId,
            ProcessedCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["flashcards"] = exportedCards }
        };
    }

    private static string EscapeCsv(string value)
    {
        var escaped = value.Replace("\"", "\"\"", StringComparison.Ordinal);
        return $"\"{escaped}\"";
    }

    private static List<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"')
            {
                sb.Append('"');
                i++;
                continue;
            }

            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (ch == ',' && !inQuotes)
            {
                values.Add(sb.ToString());
                sb.Clear();
                continue;
            }

            sb.Append(ch);
        }

        values.Add(sb.ToString());
        return values;
    }
}
