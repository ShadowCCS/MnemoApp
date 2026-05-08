using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models.Mindmap;
using Mnemo.Core.Services;
using Mnemo.Core.Services.Search;
using Mnemo.Infrastructure.Services.Tools;

namespace Mnemo.Infrastructure.Services.Search;

public sealed class MindmapsSearchProvider : ISearchProvider
{
    private readonly IMindmapService _mindmapService;

    public MindmapsSearchProvider(IMindmapService mindmapService)
    {
        _mindmapService = mindmapService;
    }

    public string ProviderId => "mindmaps";
    public string GroupKey => "mindmaps";
    public string GroupDisplayName => "Mindmaps";
    public int GroupOrder => 4;

    public async Task<IReadOnlyList<SearchResultItem>> SearchAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        var mindmapsResult = await _mindmapService.GetAllMindmapsAsync().ConfigureAwait(false);
        if (!mindmapsResult.IsSuccess || mindmapsResult.Value == null)
        {
            return [];
        }

        var results = new List<SearchResultItem>();
        foreach (var mindmap in mindmapsResult.Value)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var nodeText = string.Join(
                '\n',
                mindmap.Nodes
                    .Select(node => node.Content)
                    .OfType<TextNodeContent>()
                    .Select(content => content.Text)
                    .Where(text => !string.IsNullOrWhiteSpace(text)));

            var haystack = $"{mindmap.Title}\n{nodeText}";
            if (!TextSearchMatch.MatchTokens(haystack, query.Tokens, query.MatchAllTokens, query.Fuzzy))
            {
                continue;
            }

            var score = SimpleSearchScorer.Compute(mindmap.Title, null, nodeText, query.Tokens, query.Fuzzy, query.MatchAllTokens);
            var snippet = BuildSnippet(nodeText, query.Tokens, query.Fuzzy);

            results.Add(new SearchResultItem
            {
                Id = mindmap.Id,
                Type = SearchResultType.Mindmap,
                ProviderId = ProviderId,
                Title = mindmap.Title,
                Preview = snippet,
                GroupName = GroupDisplayName,
                GroupId = mindmap.Id,
                Score = score,
                NavigationTarget = new SearchNavigationTarget
                {
                    Route = "mindmap-detail",
                    Parameter = mindmap.Id,
                    Href = "mindmap-detail"
                },
                Href = "mindmap-detail",
                Payload = mindmap.Id
            });
        }

        return results;
    }

    private static string? BuildSnippet(string text, IReadOnlyList<string> tokens, bool fuzzy)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (!TextSearchMatch.TryGetSnippetSpan(text, tokens, fuzzy, out var start, out var length))
        {
            return text.Length <= 120 ? text : $"{text[..120]}...";
        }

        var snippet = text.Substring(start, length).Trim();
        return snippet.Length <= 120 ? snippet : $"{snippet[..120]}...";
    }
}
