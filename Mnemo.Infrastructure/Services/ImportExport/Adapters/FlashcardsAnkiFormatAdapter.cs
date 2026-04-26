using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Mnemo.Core.Models;
using Mnemo.Core.Models.Flashcards;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.ImportExport.Adapters;

/// <summary>
/// Imports and exports flashcards in Anki package format (.apkg).
/// </summary>
public sealed class FlashcardsAnkiFormatAdapter : IContentFormatAdapter
{
    private const char UnitSeparator = '\u001f';
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly Regex ClozeRegex = new(@"\{\{c\d+::", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ImageTagRegex = new(@"<img\s+[^>]*src\s*=\s*['""](?<src>[^'""]+)['""][^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BreakRegex = new(@"<\s*br\s*/?\s*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DivCloseRegex = new(@"<\s*/\s*div\s*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex AllTagsRegex = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex InlineTagRegex = new(@"</?(b|strong|i|em|u|s|strike)>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IFlashcardDeckService _deckService;
    private readonly IImageAssetService _imageAssetService;

    public FlashcardsAnkiFormatAdapter(IFlashcardDeckService deckService, IImageAssetService imageAssetService)
    {
        _deckService = deckService;
        _imageAssetService = imageAssetService;
    }

    public string ContentType => "flashcards";
    public string FormatId => "flashcards.anki";
    public string DisplayName => "Anki Package (.apkg)";
    public IReadOnlyList<string> Extensions => [".apkg"];
    public bool SupportsImport => true;
    public bool SupportsExport => true;

    public async Task<ImportExportPreview> PreviewImportAsync(ImportExportRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var opened = await OpenApkgAsync(request.FilePath, cancellationToken).ConfigureAwait(false);
            var cardCount = await CountAsync(opened.Connection, "cards", cancellationToken).ConfigureAwait(false);
            var noteCount = await CountAsync(opened.Connection, "notes", cancellationToken).ConfigureAwait(false);

            return new ImportExportPreview
            {
                CanImport = cardCount > 0,
                ContentType = ContentType,
                FormatId = FormatId,
                DiscoveredCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["flashcards"] = cardCount,
                    ["notes"] = noteCount
                }
            };
        }
        catch (Exception ex)
        {
            return new ImportExportPreview
            {
                CanImport = false,
                ContentType = ContentType,
                FormatId = FormatId,
                Warnings = { $"Unable to read Anki package: {ex.Message}" }
            };
        }
    }

    public async Task<ImportExportResult> ImportAsync(ImportExportRequest request, CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();
        var importedDecks = 0;
        var importedCards = 0;

        try
        {
            await using var opened = await OpenApkgAsync(request.FilePath, cancellationToken).ConfigureAwait(false);
            var collectionInfo = await ReadCollectionInfoAsync(opened.Connection, cancellationToken).ConfigureAwait(false);
            var mediaMap = await ReadMediaMapAsync(opened.TempDirectory, cancellationToken).ConfigureAwait(false);
            var notes = await ReadNotesAsync(opened.Connection, cancellationToken).ConfigureAwait(false);
            var cards = await ReadCardsAsync(opened.Connection, cancellationToken).ConfigureAwait(false);
            var revlog = await ReadRevlogStatsAsync(opened.Connection, cancellationToken).ConfigureAwait(false);
            foreach (var note in notes.Values)
            {
                if (collectionInfo.Models.TryGetValue(note.ModelId, out var modelName))
                    note.ModelName = modelName;
            }

            var decksByDid = cards
                .GroupBy(c => c.DeckId)
                .OrderBy(g => g.Key)
                .ToArray();

            foreach (var deckGroup in decksByDid)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var deckName = collectionInfo.Decks.TryGetValue(deckGroup.Key, out var n) && !string.IsNullOrWhiteSpace(n)
                    ? n
                    : $"Imported Deck {deckGroup.Key}";
                var deckId = Guid.NewGuid().ToString();
                var deckCards = new List<Flashcard>();

                foreach (var cardRow in deckGroup)
                {
                    if (!notes.TryGetValue(cardRow.NoteId, out var note))
                        continue;

                    var fields = note.Fields;
                    var frontHtml = fields.Length > 0 ? fields[0] : string.Empty;
                    var backHtml = fields.Length > 1 ? fields[1] : string.Empty;

                    var frontBlocks = await ConvertHtmlToBlocksAsync(frontHtml, opened.TempDirectory, mediaMap, warnings, cancellationToken).ConfigureAwait(false);
                    var backBlocks = await ConvertHtmlToBlocksAsync(backHtml, opened.TempDirectory, mediaMap, warnings, cancellationToken).ConfigureAwait(false);
                    var frontText = ToPlainText(frontHtml);
                    var backText = ToPlainText(backHtml);

                    var reviewCount = cardRow.Reps;
                    if (revlog.TryGetValue(cardRow.Id, out var revlogStats))
                    {
                        reviewCount = Math.Max(reviewCount, revlogStats.ReviewCount);
                    }

                    var flashcard = new Flashcard(
                        Id: Guid.NewGuid().ToString(),
                        DeckId: deckId,
                        Front: frontText,
                        Back: backText,
                        Type: DetectType(frontText, frontHtml, note.ModelName),
                        Tags: ParseTags(note.Tags),
                        DueDate: ComputeDueDate(collectionInfo.CollectionCreatedAt, cardRow),
                        Stability: cardRow.IntervalDays > 0 ? cardRow.IntervalDays : null,
                        Difficulty: FactorToDifficulty(cardRow.Factor),
                        Retrievability: null,
                        SourceInfo: null,
                        FrontBlocks: frontBlocks,
                        BackBlocks: backBlocks,
                        ReviewCount: reviewCount,
                        LapseCount: cardRow.Lapses,
                        LeitnerBox: null,
                        LastReviewedAt: cardRow.LastModifiedAt,
                        FsrsState: null);

                    deckCards.Add(flashcard);
                }

                if (deckCards.Count == 0)
                    continue;

                var deck = new FlashcardDeck(
                    Id: deckId,
                    Name: deckName,
                    FolderId: null,
                    Description: "Imported from Anki package",
                    Tags: Array.Empty<string>(),
                    LastStudied: deckCards
                        .Where(c => c.LastReviewedAt.HasValue)
                        .OrderByDescending(c => c.LastReviewedAt)
                        .Select(c => c.LastReviewedAt)
                        .FirstOrDefault(),
                    RetentionScore: 0,
                    Cards: deckCards,
                    SchedulingAlgorithm: FlashcardSchedulingAlgorithm.Fsrs);

                await _deckService.SaveDeckAsync(deck, cancellationToken).ConfigureAwait(false);
                importedDecks++;
                importedCards += deckCards.Count;
            }

            return new ImportExportResult
            {
                Success = importedCards > 0,
                ContentType = ContentType,
                FormatId = FormatId,
                ProcessedCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["decks"] = importedDecks,
                    ["flashcards"] = importedCards
                },
                Warnings = warnings,
                ErrorMessage = importedCards > 0 ? null : "No importable cards were found in the package."
            };
        }
        catch (Exception ex)
        {
            return new ImportExportResult
            {
                Success = false,
                ContentType = ContentType,
                FormatId = FormatId,
                Warnings = warnings,
                ErrorMessage = $"Failed to import Anki package: {ex.Message}"
            };
        }
    }

    public async Task<ImportExportResult> ExportAsync(ImportExportRequest request, CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();
        try
        {
            var decksToExport = await ResolveDecksToExportAsync(request, cancellationToken).ConfigureAwait(false);
            if (decksToExport.Count == 0)
            {
                return new ImportExportResult
                {
                    Success = false,
                    ContentType = ContentType,
                    FormatId = FormatId,
                    ErrorMessage = "No flashcard decks available to export."
                };
            }

            var tempRoot = Path.Combine(Path.GetTempPath(), $"mnemo-anki-export-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempRoot);

            try
            {
                var dbPath = Path.Combine(tempRoot, "collection.anki2");
                var mediaMap = new Dictionary<string, string>(StringComparer.Ordinal);
                var mediaCounter = 0;
                var exportedCards = 0;
                var now = DateTimeOffset.UtcNow;
                var nowMs = now.ToUnixTimeMilliseconds();
                var nowSec = now.ToUnixTimeSeconds();
                var crt = (long)Math.Floor(now.ToUnixTimeSeconds() / 86400d);

                await CreateSchemaAsync(dbPath, cancellationToken).ConfigureAwait(false);
                {
                    await using var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False");
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                    var deckJson = BuildDeckJson(decksToExport, nowMs);
                    var dconfJson = BuildDeckConfigJson(nowMs);
                    var modelJson = BuildModelJson(nowMs);
                    await InsertColAsync(connection, crt, nowSec, nowSec, deckJson, dconfJson, modelJson, cancellationToken).ConfigureAwait(false);

                    foreach (var deck in decksToExport)
                    {
                        var did = StableAnkiId($"deck:{deck.Id}:{deck.Name}");
                        foreach (var card in deck.Cards)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            var nid = StableAnkiId($"note:{card.Id}");
                            var cid = StableAnkiId($"card:{card.Id}");
                            var guid = BuildGuid(card.Id);
                            var modelId = BasicModelId;
                            var mod = nowSec;
                            var tags = card.Tags.Count > 0 ? $" {string.Join(' ', card.Tags)} " : string.Empty;

                            var frontHtml = BuildFieldHtml(card.Front, card.FrontBlocks, tempRoot, mediaMap, ref mediaCounter, warnings);
                            var backHtml = BuildFieldHtml(card.Back, card.BackBlocks, tempRoot, mediaMap, ref mediaCounter, warnings);
                            var flds = $"{frontHtml}{UnitSeparator}{backHtml}";
                            var sfld = card.Front;
                            var csum = ComputeChecksum(card.Front);
                            var dueData = ToAnkiScheduling(card, crt);

                            await InsertNoteAsync(connection, nid, guid, modelId, mod, tags, flds, sfld, csum, cancellationToken).ConfigureAwait(false);
                            await InsertCardAsync(connection, cid, nid, did, mod, dueData, cancellationToken).ConfigureAwait(false);
                            exportedCards++;
                        }
                    }
                }

                var mediaJsonPath = Path.Combine(tempRoot, "media");
                await File.WriteAllTextAsync(mediaJsonPath, JsonSerializer.Serialize(mediaMap), Utf8WithoutBom, cancellationToken).ConfigureAwait(false);

                if (File.Exists(request.FilePath))
                    File.Delete(request.FilePath);

                ZipFile.CreateFromDirectory(tempRoot, request.FilePath, CompressionLevel.Optimal, includeBaseDirectory: false);

                if (exportedCards == 0)
                {
                    return new ImportExportResult
                    {
                        Success = false,
                        ContentType = ContentType,
                        FormatId = FormatId,
                        ProcessedCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["decks"] = decksToExport.Count,
                            ["flashcards"] = 0
                        },
                        Warnings = warnings,
                        ErrorMessage = "No cards were exported to the Anki package."
                    };
                }

                return new ImportExportResult
                {
                    Success = true,
                    ContentType = ContentType,
                    FormatId = FormatId,
                    ProcessedCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["decks"] = decksToExport.Count,
                        ["flashcards"] = exportedCards
                    },
                    Warnings = warnings
                };
            }
            finally
            {
                await TryDeleteDirectoryWithRetriesAsync(tempRoot).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            return new ImportExportResult
            {
                Success = false,
                ContentType = ContentType,
                FormatId = FormatId,
                Warnings = warnings,
                ErrorMessage = $"Failed to export Anki package: {ex.Message}"
            };
        }
    }

    private static long BasicModelId => 1_608_194_021_001L;

    private static async Task<OpenedApkg> OpenApkgAsync(string apkgPath, CancellationToken cancellationToken)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"mnemo-anki-import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        ZipFile.ExtractToDirectory(apkgPath, tempDirectory);

        var dbPath = Path.Combine(tempDirectory, "collection.anki21");
        if (!File.Exists(dbPath))
            dbPath = Path.Combine(tempDirectory, "collection.anki2");
        if (!File.Exists(dbPath))
            throw new InvalidOperationException("Package does not contain collection.anki21 or collection.anki2.");

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString();
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return new OpenedApkg(tempDirectory, connection);
    }

    private static async Task<int> CountAsync(SqliteConnection connection, string tableName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(1) FROM {tableName}";
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static async Task<CollectionInfo> ReadCollectionInfoAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT crt, decks, models FROM col LIMIT 1";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return new CollectionInfo(DateTimeOffset.UtcNow, new Dictionary<long, string>(), new Dictionary<long, string>());

        var crt = reader.IsDBNull(0) ? 0L : reader.GetInt64(0);
        var decksJson = reader.IsDBNull(1) ? "{}" : reader.GetString(1);
        var modelsJson = reader.IsDBNull(2) ? "{}" : reader.GetString(2);

        var createdAt = ParseCollectionCreatedAt(crt);
        var deckNames = ParseNameMap(decksJson);
        var modelNames = ParseNameMap(modelsJson);
        return new CollectionInfo(createdAt, deckNames, modelNames);
    }

    private static Dictionary<long, string> ParseNameMap(string json)
    {
        var map = new Dictionary<long, string>();
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (!long.TryParse(prop.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                continue;
            if (prop.Value.TryGetProperty("name", out var nameElement))
                map[id] = nameElement.GetString() ?? string.Empty;
        }

        return map;
    }

    private static async Task<Dictionary<string, string>> ReadMediaMapAsync(string tempDirectory, CancellationToken cancellationToken)
    {
        var mediaPath = Path.Combine(tempDirectory, "media");
        if (!File.Exists(mediaPath))
            return new Dictionary<string, string>(StringComparer.Ordinal);
        var json = await File.ReadAllTextAsync(mediaPath, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private static async Task<Dictionary<long, NoteRow>> ReadNotesAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var result = new Dictionary<long, NoteRow>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, tags, flds, mid FROM notes";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var id = reader.GetInt64(0);
            var tags = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            var flds = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
            var modelId = reader.IsDBNull(3) ? 0L : reader.GetInt64(3);
            var fields = flds.Split(UnitSeparator);
            result[id] = new NoteRow(id, tags, fields, modelId);
        }

        return result;
    }

    private static async Task<List<CardRow>> ReadCardsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var cards = new List<CardRow>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, nid, did, type, queue, due, ivl, factor, reps, lapses, mod FROM cards";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            cards.Add(new CardRow(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                reader.IsDBNull(5) ? 0 : reader.GetInt64(5),
                reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                reader.IsDBNull(7) ? 2500 : reader.GetInt32(7),
                reader.IsDBNull(8) ? 0 : reader.GetInt32(8),
                reader.IsDBNull(9) ? 0 : reader.GetInt32(9),
                reader.IsDBNull(10) ? DateTimeOffset.UtcNow : ParseUnixTimestamp(reader.GetInt64(10))));
        }

        return cards;
    }

    private static async Task<Dictionary<long, RevlogStats>> ReadRevlogStatsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var stats = new Dictionary<long, RevlogStats>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT cid, COUNT(1), SUM(CASE WHEN ease = 1 THEN 1 ELSE 0 END) FROM revlog GROUP BY cid";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var cardId = reader.GetInt64(0);
            var reviewCount = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
            var againCount = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
            stats[cardId] = new RevlogStats(reviewCount, againCount);
        }

        return stats;
    }

    private async Task<IReadOnlyList<Block>> ConvertHtmlToBlocksAsync(
        string html,
        string tempDirectory,
        IReadOnlyDictionary<string, string> mediaMap,
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        var blocks = new List<Block>();
        var normalized = NormalizeHtmlLineBreaks(html);
        foreach (var line in normalized.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            var textWithoutImages = ImageTagRegex.Replace(trimmed, string.Empty);
            var spans = ParseInlineSpans(textWithoutImages);
            if (spans.Count > 0)
            {
                blocks.Add(new Block
                {
                    Id = Guid.NewGuid().ToString(),
                    Type = BlockType.Text,
                    Spans = spans,
                    Order = blocks.Count
                });
            }

            foreach (Match match in ImageTagRegex.Matches(trimmed))
            {
                var src = match.Groups["src"].Value.Trim();
                if (string.IsNullOrWhiteSpace(src))
                    continue;

                var resolvedMediaPath = ResolveMediaPath(src, tempDirectory, mediaMap);
                if (resolvedMediaPath == null)
                {
                    warnings.Add($"Referenced media '{src}' was not found in package.");
                    continue;
                }

                var blockId = Guid.NewGuid().ToString("N");
                var copied = await _imageAssetService.ImportAndCopyAsync(resolvedMediaPath, blockId, cancellationToken).ConfigureAwait(false);
                if (!copied.IsSuccess || string.IsNullOrWhiteSpace(copied.Value))
                {
                    warnings.Add($"Failed to import media '{src}': {copied.ErrorMessage ?? "unknown error"}");
                    continue;
                }

                blocks.Add(new Block
                {
                    Id = blockId,
                    Type = BlockType.Image,
                    Payload = new ImagePayload(copied.Value),
                    Spans = new List<InlineSpan> { InlineSpan.Plain(string.Empty) },
                    Order = blocks.Count
                });
            }
        }

        if (blocks.Count == 0)
        {
            blocks.Add(new Block
            {
                Id = Guid.NewGuid().ToString(),
                Type = BlockType.Text,
                Spans = new List<InlineSpan> { InlineSpan.Plain(ToPlainText(html)) },
                Order = 0
            });
        }

        return blocks;
    }

    private static List<InlineSpan> ParseInlineSpans(string htmlText)
    {
        var clean = NormalizeHtmlLineBreaks(htmlText);
        var spans = new List<InlineSpan>();
        var style = TextStyle.Default;
        var index = 0;
        var matches = InlineTagRegex.Matches(clean);
        foreach (Match match in matches)
        {
            if (match.Index > index)
            {
                var plainSegment = clean[index..match.Index];
                var decoded = WebUtility.HtmlDecode(AllTagsRegex.Replace(plainSegment, string.Empty));
                if (!string.IsNullOrEmpty(decoded))
                    spans.Add(new TextSpan(decoded, style));
            }

            var token = match.Value.ToLowerInvariant();
            style = token switch
            {
                "<b>" or "<strong>" => style with { Bold = true },
                "</b>" or "</strong>" => style with { Bold = false },
                "<i>" or "<em>" => style with { Italic = true },
                "</i>" or "</em>" => style with { Italic = false },
                "<u>" => style with { Underline = true },
                "</u>" => style with { Underline = false },
                "<s>" or "<strike>" => style with { Strikethrough = true },
                "</s>" or "</strike>" => style with { Strikethrough = false },
                _ => style
            };

            index = match.Index + match.Length;
        }

        if (index < clean.Length)
        {
            var tail = clean[index..];
            var decodedTail = WebUtility.HtmlDecode(AllTagsRegex.Replace(tail, string.Empty));
            if (!string.IsNullOrEmpty(decodedTail))
                spans.Add(new TextSpan(decodedTail, style));
        }

        if (spans.Count == 0)
            spans.Add(InlineSpan.Plain(string.Empty));

        return spans;
    }

    private static string? ResolveMediaPath(string src, string tempDirectory, IReadOnlyDictionary<string, string> mediaMap)
    {
        var directPath = Path.Combine(tempDirectory, src);
        if (File.Exists(directPath))
            return directPath;

        var mediaNumber = mediaMap.FirstOrDefault(kv => string.Equals(kv.Value, src, StringComparison.OrdinalIgnoreCase)).Key;
        if (!string.IsNullOrWhiteSpace(mediaNumber))
        {
            var mappedPath = Path.Combine(tempDirectory, mediaNumber);
            if (File.Exists(mappedPath))
                return mappedPath;
        }

        return null;
    }

    private static string NormalizeHtmlLineBreaks(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;
        var normalized = html;
        normalized = BreakRegex.Replace(normalized, "\n");
        normalized = DivCloseRegex.Replace(normalized, "\n");
        return normalized;
    }

    private static string ToPlainText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;
        var normalized = NormalizeHtmlLineBreaks(html);
        var stripped = AllTagsRegex.Replace(normalized, string.Empty);
        return WebUtility.HtmlDecode(stripped).Trim();
    }

    private static IReadOnlyList<string> ParseTags(string rawTags)
    {
        return rawTags
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static FlashcardType DetectType(string frontText, string frontHtml, string? modelName)
    {
        if (ClozeRegex.IsMatch(frontText) || ClozeRegex.IsMatch(frontHtml))
            return FlashcardType.Cloze;
        if (!string.IsNullOrWhiteSpace(modelName) && modelName.Contains("cloze", StringComparison.OrdinalIgnoreCase))
            return FlashcardType.Cloze;
        return FlashcardType.Classic;
    }

    private static DateTimeOffset ComputeDueDate(DateTimeOffset collectionCreatedAt, CardRow card)
    {
        if (card.Type == 2 && card.IntervalDays > 0)
            return collectionCreatedAt.Date.AddDays(card.Due);
        if (card.Type == 1 || card.Type == 3)
            return DateTimeOffset.UtcNow;
        if (card.Type == 0)
            return DateTimeOffset.UtcNow;
        return DateTimeOffset.UtcNow;
    }

    private static double? FactorToDifficulty(int factor)
    {
        if (factor <= 0)
            return null;
        var clamped = Math.Clamp(factor, 1300, 3000);
        return (3000d - clamped) / 1700d;
    }

    private async Task<List<FlashcardDeck>> ResolveDecksToExportAsync(ImportExportRequest request, CancellationToken cancellationToken)
    {
        if (request.Payload is FlashcardDeck singleDeck)
            return [singleDeck];
        if (request.Payload is string deckId && !string.IsNullOrWhiteSpace(deckId))
        {
            var found = await _deckService.GetDeckByIdAsync(deckId, cancellationToken).ConfigureAwait(false);
            return found is null ? new List<FlashcardDeck>() : [found];
        }
        if (request.Payload is IEnumerable<string> deckIds)
        {
            var idSet = new HashSet<string>(
                deckIds.Where(id => !string.IsNullOrWhiteSpace(id)),
                StringComparer.Ordinal);
            if (idSet.Count > 0)
            {
                var allDecks = await _deckService.ListDecksAsync(cancellationToken).ConfigureAwait(false);
                return allDecks.Where(deck => idSet.Contains(deck.Id)).ToList();
            }
        }

        var decks = await _deckService.ListDecksAsync(cancellationToken).ConfigureAwait(false);
        return decks.ToList();
    }

    private static async Task CreateSchemaAsync(string dbPath, CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var schemaSql = """
                        CREATE TABLE col (
                            id INTEGER PRIMARY KEY,
                            crt INTEGER NOT NULL,
                            mod INTEGER NOT NULL,
                            scm INTEGER NOT NULL,
                            ver INTEGER NOT NULL,
                            dty INTEGER NOT NULL,
                            usn INTEGER NOT NULL,
                            ls INTEGER NOT NULL,
                            conf TEXT NOT NULL,
                            models TEXT NOT NULL,
                            decks TEXT NOT NULL,
                            dconf TEXT NOT NULL,
                            tags TEXT NOT NULL
                        );
                        CREATE TABLE notes (
                            id INTEGER PRIMARY KEY,
                            guid TEXT NOT NULL,
                            mid INTEGER NOT NULL,
                            mod INTEGER NOT NULL,
                            usn INTEGER NOT NULL,
                            tags TEXT NOT NULL,
                            flds TEXT NOT NULL,
                            sfld INTEGER NOT NULL,
                            csum INTEGER NOT NULL,
                            flags INTEGER NOT NULL,
                            data TEXT NOT NULL
                        );
                        CREATE TABLE cards (
                            id INTEGER PRIMARY KEY,
                            nid INTEGER NOT NULL,
                            did INTEGER NOT NULL,
                            ord INTEGER NOT NULL,
                            mod INTEGER NOT NULL,
                            usn INTEGER NOT NULL,
                            type INTEGER NOT NULL,
                            queue INTEGER NOT NULL,
                            due INTEGER NOT NULL,
                            ivl INTEGER NOT NULL,
                            factor INTEGER NOT NULL,
                            reps INTEGER NOT NULL,
                            lapses INTEGER NOT NULL,
                            left INTEGER NOT NULL,
                            odue INTEGER NOT NULL,
                            odid INTEGER NOT NULL,
                            flags INTEGER NOT NULL,
                            data TEXT NOT NULL
                        );
                        CREATE TABLE revlog (
                            id INTEGER PRIMARY KEY,
                            cid INTEGER NOT NULL,
                            usn INTEGER NOT NULL,
                            ease INTEGER NOT NULL,
                            ivl INTEGER NOT NULL,
                            lastIvl INTEGER NOT NULL,
                            factor INTEGER NOT NULL,
                            time INTEGER NOT NULL,
                            type INTEGER NOT NULL
                        );
                        CREATE TABLE graves (
                            usn INTEGER NOT NULL,
                            oid INTEGER NOT NULL,
                            type INTEGER NOT NULL
                        );
                        """;
        await using var command = connection.CreateCommand();
        command.CommandText = schemaSql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string BuildDeckJson(IReadOnlyList<FlashcardDeck> decks, long nowMs)
    {
        var mod = nowMs / 1000;
        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var deck in decks)
        {
            var did = StableAnkiId($"deck:{deck.Id}:{deck.Name}");
            map[did.ToString(CultureInfo.InvariantCulture)] = new Dictionary<string, object?>
            {
                ["id"] = did,
                ["name"] = deck.Name,
                ["mod"] = mod,
                ["usn"] = 0,
                ["desc"] = deck.Description ?? string.Empty,
                ["dyn"] = 0,
                ["conf"] = 1,
                ["extendNew"] = 0,
                ["extendRev"] = 0,
                ["newToday"] = new object[] { 0, 0 },
                ["revToday"] = new object[] { 0, 0 },
                ["lrnToday"] = new object[] { 0, 0 },
                ["timeToday"] = new object[] { 0, 0 },
                ["collapsed"] = false,
                ["browserCollapsed"] = false
            };
        }

        return JsonSerializer.Serialize(map);
    }

    private static string BuildDeckConfigJson(long nowMs)
    {
        var mod = nowMs / 1000;
        var dconf = new Dictionary<string, object?>
        {
            ["1"] = new Dictionary<string, object?>
            {
                ["id"] = 1,
                ["name"] = "Default",
                ["mod"] = mod,
                ["usn"] = 0,
                ["maxTaken"] = 60,
                ["timer"] = 0,
                ["autoplay"] = true,
                ["replayq"] = true,
                ["new"] = new Dictionary<string, object?>
                {
                    ["bury"] = true,
                    ["delays"] = new object[] { 1, 10 },
                    ["initialFactor"] = 2500,
                    ["ints"] = new object[] { 1, 4, 7 },
                    ["order"] = 1,
                    ["perDay"] = 20
                },
                ["rev"] = new Dictionary<string, object?>
                {
                    ["bury"] = true,
                    ["ease4"] = 1.3,
                    ["fuzz"] = 0.05,
                    ["ivlFct"] = 1,
                    ["maxIvl"] = 36500,
                    ["perDay"] = 200
                },
                ["lapse"] = new Dictionary<string, object?>
                {
                    ["delays"] = new object[] { 10 },
                    ["leechAction"] = 0,
                    ["leechFails"] = 8,
                    ["minInt"] = 1,
                    ["mult"] = 0
                }
            }
        };

        return JsonSerializer.Serialize(dconf);
    }

    private static string BuildModelJson(long nowMs)
    {
        var mod = nowMs / 1000;
        var model = new Dictionary<string, object?>
        {
            [BasicModelId.ToString(CultureInfo.InvariantCulture)] = new Dictionary<string, object?>
            {
                ["id"] = BasicModelId,
                ["name"] = "Mnemo Basic",
                ["type"] = 0,
                ["mod"] = mod,
                ["usn"] = 0,
                ["vers"] = Array.Empty<object>(),
                ["tags"] = Array.Empty<object>(),
                ["sortf"] = 0,
                ["did"] = 1,
                ["req"] = new object[]
                {
                    new object[] { 0, "all", new object[] { 0 } }
                },
                ["flds"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["name"] = "Front",
                        ["ord"] = 0,
                        ["media"] = Array.Empty<object>(),
                        ["sticky"] = false,
                        ["rtl"] = false,
                        ["font"] = "Arial",
                        ["size"] = 20,
                        ["description"] = string.Empty
                    },
                    new Dictionary<string, object?>
                    {
                        ["name"] = "Back",
                        ["ord"] = 1,
                        ["media"] = Array.Empty<object>(),
                        ["sticky"] = false,
                        ["rtl"] = false,
                        ["font"] = "Arial",
                        ["size"] = 20,
                        ["description"] = string.Empty
                    }
                },
                ["tmpls"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["name"] = "Card 1",
                        ["ord"] = 0,
                        ["qfmt"] = "{{Front}}",
                        ["afmt"] = "{{FrontSide}}\n<hr id=answer>\n{{Back}}",
                        ["bqfmt"] = string.Empty,
                        ["bafmt"] = string.Empty,
                        ["did"] = null,
                        ["bfont"] = "Arial",
                        ["bsize"] = 20
                    }
                },
                ["css"] = ".card { font-family: arial; font-size: 20px; text-align: center; color: black; background-color: white; }",
                ["latexPre"] = "\\documentclass[12pt]{article}\n\\special{papersize=3in,5in}\n\\usepackage[utf8]{inputenc}\n\\usepackage{amssymb,amsmath}\n\\pagestyle{empty}\n\\setlength{\\parindent}{0in}\n\\begin{document}\n",
                ["latexPost"] = "\\end{document}"
            }
        };

        return JsonSerializer.Serialize(model);
    }

    private static async Task InsertColAsync(
        SqliteConnection connection,
        long crt,
        long mod,
        long scm,
        string decks,
        string dconf,
        string models,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              INSERT INTO col(id, crt, mod, scm, ver, dty, usn, ls, conf, models, decks, dconf, tags)
                              VALUES(1, @crt, @mod, @scm, 11, 0, 0, 0, '{}', @models, @decks, @dconf, '{}')
                              """;
        command.Parameters.AddWithValue("@crt", crt);
        command.Parameters.AddWithValue("@mod", mod);
        command.Parameters.AddWithValue("@scm", scm);
        command.Parameters.AddWithValue("@models", models);
        command.Parameters.AddWithValue("@decks", decks);
        command.Parameters.AddWithValue("@dconf", dconf);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertNoteAsync(
        SqliteConnection connection,
        long id,
        string guid,
        long modelId,
        long mod,
        string tags,
        string flds,
        string sfld,
        long csum,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              INSERT OR REPLACE INTO notes(id, guid, mid, mod, usn, tags, flds, sfld, csum, flags, data)
                              VALUES(@id, @guid, @mid, @mod, 0, @tags, @flds, @sfld, @csum, 0, '')
                              """;
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@guid", guid);
        command.Parameters.AddWithValue("@mid", modelId);
        command.Parameters.AddWithValue("@mod", mod);
        command.Parameters.AddWithValue("@tags", tags);
        command.Parameters.AddWithValue("@flds", flds);
        command.Parameters.AddWithValue("@sfld", sfld);
        command.Parameters.AddWithValue("@csum", csum);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertCardAsync(
        SqliteConnection connection,
        long id,
        long noteId,
        long deckId,
        long mod,
        AnkiDueData dueData,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              INSERT INTO cards(id, nid, did, ord, mod, usn, type, queue, due, ivl, factor, reps, lapses, left, odue, odid, flags, data)
                              VALUES(@id, @nid, @did, 0, @mod, 0, @type, @queue, @due, @ivl, @factor, @reps, @lapses, 0, 0, 0, 0, '')
                              """;
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@nid", noteId);
        command.Parameters.AddWithValue("@did", deckId);
        command.Parameters.AddWithValue("@mod", mod);
        command.Parameters.AddWithValue("@type", dueData.Type);
        command.Parameters.AddWithValue("@queue", dueData.Queue);
        command.Parameters.AddWithValue("@due", dueData.Due);
        command.Parameters.AddWithValue("@ivl", dueData.Interval);
        command.Parameters.AddWithValue("@factor", dueData.Factor);
        command.Parameters.AddWithValue("@reps", dueData.Reps);
        command.Parameters.AddWithValue("@lapses", dueData.Lapses);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static AnkiDueData ToAnkiScheduling(Flashcard card, long collectionCrtDay)
    {
        var factor = card.Difficulty is double d
            ? (int)Math.Round(3000d - Math.Clamp(d, 0d, 1d) * 1700d, MidpointRounding.AwayFromZero)
            : 2500;
        var reps = Math.Max(0, card.ReviewCount ?? 0);
        var lapses = Math.Max(0, card.LapseCount ?? 0);
        if (reps == 0)
            return new AnkiDueData(Type: 0, Queue: 0, Due: 0, Interval: 0, Factor: factor, Reps: reps, Lapses: lapses);

        var intervalDays = Math.Max(1, (int)Math.Round(card.Stability ?? Math.Max(1d, (card.DueDate - DateTimeOffset.UtcNow).TotalDays), MidpointRounding.AwayFromZero));
        var collectionCreatedAt = ParseCollectionCreatedAt(collectionCrtDay);
        var dueDaysFromCrt = (int)Math.Round((card.DueDate.UtcDateTime.Date - collectionCreatedAt.UtcDateTime.Date).TotalDays, MidpointRounding.AwayFromZero);
        return new AnkiDueData(Type: 2, Queue: 2, Due: dueDaysFromCrt, Interval: intervalDays, Factor: factor, Reps: reps, Lapses: lapses);
    }

    private static string BuildFieldHtml(
        string plain,
        IReadOnlyList<Block>? blocks,
        string tempRoot,
        IDictionary<string, string> mediaMap,
        ref int mediaCounter,
        ICollection<string> warnings)
    {
        if (blocks is null || blocks.Count == 0)
            return WebUtility.HtmlEncode(plain ?? string.Empty);

        var fragments = new List<string>(blocks.Count);
        foreach (var block in blocks.OrderBy(b => b.Order))
        {
            if (block.Type == BlockType.Image && block.Payload is ImagePayload imagePayload)
            {
                var copiedFilename = TryCopyMedia(imagePayload.Path, tempRoot, mediaMap, ref mediaCounter, warnings);
                if (copiedFilename != null)
                    fragments.Add($"<img src=\"{WebUtility.HtmlEncode(copiedFilename)}\">");
                continue;
            }

            var text = block.Spans is { Count: > 0 } ? SerializeSpansToHtml(block.Spans) : WebUtility.HtmlEncode(block.Content);
            fragments.Add(text);
        }

        return string.Join("<br>", fragments.Where(f => !string.IsNullOrWhiteSpace(f)));
    }

    private static string? TryCopyMedia(
        string sourcePath,
        string tempRoot,
        IDictionary<string, string> mediaMap,
        ref int mediaCounter,
        ICollection<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            warnings.Add($"Image asset not found for export: {sourcePath}");
            return null;
        }

        var originalName = Path.GetFileName(sourcePath);
        var existing = mediaMap.FirstOrDefault(kv => string.Equals(kv.Value, originalName, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(existing.Key))
            return originalName;

        var slot = mediaCounter.ToString(CultureInfo.InvariantCulture);
        mediaCounter++;
        var dest = Path.Combine(tempRoot, slot);
        File.Copy(sourcePath, dest, overwrite: true);
        mediaMap[slot] = originalName;
        return originalName;
    }

    private static string SerializeSpansToHtml(IReadOnlyList<InlineSpan> spans)
    {
        var sb = new StringBuilder();
        foreach (var span in spans)
        {
            if (span is not TextSpan textSpan)
                continue;
            var segment = WebUtility.HtmlEncode(textSpan.Text);
            if (textSpan.Style.Bold)
                segment = $"<b>{segment}</b>";
            if (textSpan.Style.Italic)
                segment = $"<i>{segment}</i>";
            if (textSpan.Style.Underline)
                segment = $"<u>{segment}</u>";
            if (textSpan.Style.Strikethrough)
                segment = $"<s>{segment}</s>";
            sb.Append(segment);
        }

        return sb.ToString();
    }

    private static long ComputeChecksum(string value)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty));
        return ((long)bytes[0] << 24) | ((long)bytes[1] << 16) | ((long)bytes[2] << 8) | bytes[3];
    }

    private static long StableAnkiId(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var longBytes = hash[..8];
        var raw = BitConverter.ToInt64(longBytes, 0);
        var positive = Math.Abs(raw);
        return positive < 1_000_000_000_000L ? positive + 1_000_000_000_000L : positive;
    }

    private static string BuildGuid(string source)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return Convert.ToBase64String(hash[..10]).Replace('+', 'A').Replace('/', 'B');
    }

    private static DateTimeOffset ParseUnixTimestamp(long value)
    {
        if (value <= 0)
            return DateTimeOffset.UtcNow;

        // Anki datasets in the wild may use either seconds or milliseconds.
        // Values beyond Unix-seconds max range are interpreted as milliseconds.
        const long maxUnixSeconds = 253402300799L; // 9999-12-31T23:59:59Z
        if (value > maxUnixSeconds)
            return DateTimeOffset.FromUnixTimeMilliseconds(value);

        return DateTimeOffset.FromUnixTimeSeconds(value);
    }

    private static DateTimeOffset ParseCollectionCreatedAt(long crtRaw)
    {
        if (crtRaw <= 0)
            return DateTimeOffset.UtcNow.Date;

        // Canonical Anki value: days since Unix epoch.
        // Some packages may contain seconds or milliseconds instead.
        const long maxReasonableAnkiDays = 3_652_059; // up to year 9999
        if (crtRaw <= maxReasonableAnkiDays)
            return DateTimeOffset.FromUnixTimeSeconds(crtRaw * 86400L);

        return ParseUnixTimestamp(crtRaw);
    }

    private sealed record OpenedApkg(string TempDirectory, SqliteConnection Connection) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Connection.DisposeAsync().ConfigureAwait(false);
            await TryDeleteDirectoryWithRetriesAsync(TempDirectory).ConfigureAwait(false);
        }
    }

    private static async Task TryDeleteDirectoryWithRetriesAsync(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
            return;

        // SQLite/file-indexer locks on Windows can lag briefly after disposal.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(directoryPath, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                await Task.Delay(150).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                await Task.Delay(150).ConfigureAwait(false);
            }
        }
    }

    private sealed record CollectionInfo(
        DateTimeOffset CollectionCreatedAt,
        IReadOnlyDictionary<long, string> Decks,
        IReadOnlyDictionary<long, string> Models);

    private sealed record NoteRow(long Id, string Tags, string[] Fields, long ModelId)
    {
        public string? ModelName { get; set; }
    }

    private sealed record CardRow(
        long Id,
        long NoteId,
        long DeckId,
        int Type,
        int Queue,
        long Due,
        int IntervalDays,
        int Factor,
        int Reps,
        int Lapses,
        DateTimeOffset LastModifiedAt);

    private sealed record RevlogStats(int ReviewCount, int AgainCount);

    private sealed record AnkiDueData(
        int Type,
        int Queue,
        int Due,
        int Interval,
        int Factor,
        int Reps,
        int Lapses);
}
