using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using Mnemo.Core.Services.Search;
using Mnemo.Infrastructure.Services.Tools;

namespace Mnemo.Infrastructure.Services.Search;

public sealed class NotesSearchProvider : ISearchProvider
{
    private readonly INoteService _noteService;

    public NotesSearchProvider(INoteService noteService)
    {
        _noteService = noteService;
    }

    public string ProviderId => "notes";
    public string GroupKey => "notes";
    public string GroupDisplayName => "Notes";
    public int GroupOrder => 3;

    public async Task<IReadOnlyList<SearchResultItem>> SearchAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        var notes = await _noteService.GetAllNotesAsync().ConfigureAwait(false);
        var results = new List<SearchResultItem>();

        foreach (var note in notes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var body = BuildNoteBodyText(note);
            var haystack = $"{note.Title}\n{body}";
            if (!TextSearchMatch.MatchTokens(haystack, query.Tokens, query.MatchAllTokens, query.Fuzzy))
            {
                continue;
            }

            var score = SimpleSearchScorer.Compute(note.Title, note.FolderPath, body, query.Tokens, query.Fuzzy, query.MatchAllTokens);
            var snippet = BuildSnippet(body, query.Tokens, query.Fuzzy);

            results.Add(new SearchResultItem
            {
                Id = note.NoteId,
                Type = SearchResultType.Note,
                ProviderId = ProviderId,
                Title = note.Title,
                Subtitle = string.IsNullOrWhiteSpace(note.FolderPath) ? null : note.FolderPath,
                Preview = snippet,
                GroupName = string.IsNullOrWhiteSpace(note.FolderPath) ? GroupDisplayName : note.FolderPath,
                GroupId = note.FolderId,
                Score = score,
                NavigationTarget = new SearchNavigationTarget
                {
                    Route = "notes",
                    Parameter = note.NoteId,
                    Href = "notes"
                },
                Href = "notes",
                Payload = note.NoteId
            });
        }

        return results;
    }

    private static string BuildNoteBodyText(Note note)
    {
        if (note.Blocks is not { Count: > 0 })
        {
            return note.Content ?? string.Empty;
        }

        var segments = new List<string>();
        AppendBlockTexts(note.Blocks, segments);
        return string.Join('\n', segments.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    private static void AppendBlockTexts(IEnumerable<Block> blocks, ICollection<string> segments)
    {
        foreach (var block in blocks)
        {
            if (!string.IsNullOrWhiteSpace(block.Content))
            {
                segments.Add(block.Content);
            }

            if (block.Children is { Count: > 0 })
            {
                AppendBlockTexts(block.Children, segments);
            }
        }
    }

    private static string? BuildSnippet(string content, IReadOnlyList<string> tokens, bool fuzzy)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        if (!TextSearchMatch.TryGetSnippetSpan(content, tokens, fuzzy, out var start, out var length))
        {
            return content.Length <= 120 ? content : $"{content[..120]}...";
        }

        var snippet = content.Substring(start, length).Trim();
        return snippet.Length <= 120 ? snippet : $"{snippet[..120]}...";
    }
}
