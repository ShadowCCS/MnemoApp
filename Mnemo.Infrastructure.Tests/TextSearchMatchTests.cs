using Mnemo.Infrastructure.Services.Tools;

namespace Mnemo.Infrastructure.Tests;

public class TextSearchMatchTests
{
    [Fact]
    public void MatchTokens_MultiKeyword_Or_MatchesAny()
    {
        var tokens = TextSearchMatch.ResolveSearchTokens("Lenin, Germany, Russia");
        Assert.True(TextSearchMatch.MatchTokens("Russia and France", tokens, matchAll: false, fuzzy: false));
        Assert.False(TextSearchMatch.MatchTokens("France only", tokens, matchAll: false, fuzzy: false));
    }

    [Fact]
    public void MatchTokens_MultiKeyword_And_RequiresAll()
    {
        var tokens = TextSearchMatch.ResolveSearchTokens("Lenin Russia");
        Assert.True(TextSearchMatch.MatchTokens("Lenin wrote about Russia", tokens, matchAll: true, fuzzy: false));
        Assert.False(TextSearchMatch.MatchTokens("Lenin only", tokens, matchAll: true, fuzzy: false));
    }

    [Fact]
    public void MatchTokens_Fuzzy_Typo()
    {
        var tokens = TextSearchMatch.ResolveSearchTokens("Germany");
        Assert.True(TextSearchMatch.MatchTokens("Trip to Gemany in 1920", tokens, matchAll: true, fuzzy: true));
        Assert.False(TextSearchMatch.MatchTokens("Trip to Gemany in 1920", tokens, matchAll: true, fuzzy: false));
    }

    [Fact]
    public void TryGetSnippetSpan_FindsTypoWord()
    {
        var tokens = TextSearchMatch.ResolveSearchTokens("Germany");
        var text = "We visited Gemany last year.";
        Assert.True(TextSearchMatch.TryGetSnippetSpan(text, tokens, fuzzy: true, out var start, out var len));
        Assert.Contains("Gemany", text.AsSpan(start, len).ToString());
    }
}
