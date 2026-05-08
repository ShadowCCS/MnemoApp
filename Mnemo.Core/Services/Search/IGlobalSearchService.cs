using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mnemo.Core.Services.Search;

public interface IGlobalSearchService
{
    Task<GlobalSearchResponse> SearchAsync(SearchQuery query, CancellationToken cancellationToken);
}
