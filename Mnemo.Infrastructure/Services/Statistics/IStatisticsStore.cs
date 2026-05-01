using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models.Statistics;

namespace Mnemo.Infrastructure.Services.Statistics;

/// <summary>
/// Storage backend for <see cref="Mnemo.Core.Services.IStatisticsManager"/>. Splitting the manager
/// (validation/policy) from the store (persistence) keeps the manager easy to unit test and lets
/// us swap the backend (Sqlite, in-memory, future remote sync) without changing the public API.
/// </summary>
internal interface IStatisticsStore
{
    Task<StatisticsRecord?> GetAsync(string ns, string kind, string key, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string ns, string kind, string key, CancellationToken cancellationToken = default);

    Task<StatisticsRecord> InsertAsync(StatisticsRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the stored record with <paramref name="record"/>. When <paramref name="expectedVersion"/>
    /// is non-null the operation is rejected when the persisted version differs (returns null).
    /// </summary>
    Task<StatisticsRecord?> UpdateAsync(StatisticsRecord record, long? expectedVersion, CancellationToken cancellationToken = default);

    Task DeleteAsync(string ns, string kind, string key, CancellationToken cancellationToken = default);

    Task<System.Collections.Generic.IReadOnlyList<StatisticsRecord>> QueryAsync(StatisticsQuery query, CancellationToken cancellationToken = default);
}
