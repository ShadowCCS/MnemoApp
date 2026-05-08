using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mnemo.Core.Services.Search;

public interface ISearchProvider
{
    string ProviderId { get; }
    string GroupKey { get; }
    string GroupDisplayName { get; }
    int GroupOrder { get; }
    Task<IReadOnlyList<SearchResultItem>> SearchAsync(SearchQuery query, CancellationToken cancellationToken);
}
