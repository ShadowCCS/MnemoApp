using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Models.Statistics;

namespace Mnemo.Core.Services;

/// <summary>
/// Central hub for user-behavior statistics. Designed for both internal modules (called via DI)
/// and external extensions (called via the function/tool registry adapter).
/// </summary>
/// <remarks>
/// Records are uniquely identified by <c>(Namespace, Kind, Key)</c>. Fields are typed via
/// <see cref="StatValue"/>. Validation is enforced against optional schemas registered with
/// <see cref="RegisterSchemaAsync"/>; in the absence of a schema, writes are accepted as long as
/// they satisfy structural rules (non-empty identity, JSON metadata size limit, etc).
/// </remarks>
public interface IStatisticsManager
{
    /// <summary>
    /// Creates a new record. Fails with a <see cref="Mnemo.Core.Models.Tools.ToolResultCodes.Conflict"/>
    /// equivalent error message when the record already exists. Use <see cref="UpsertAsync"/> to
    /// avoid the existence check.
    /// </summary>
    Task<Result<StatisticsRecord>> CreateAsync(StatisticsRecordWrite write, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing record. When <paramref name="expectedVersion"/> is non-null the update
    /// is rejected if the persisted version differs (optimistic concurrency).
    /// </summary>
    Task<Result<StatisticsRecord>> UpdateAsync(
        StatisticsRecordWrite write,
        StatisticsFieldMergeMode mergeMode = StatisticsFieldMergeMode.Merge,
        long? expectedVersion = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the record if missing or updates it if present. Convenience for telemetry-style flows
    /// where existence is not relevant to the caller.
    /// </summary>
    Task<Result<StatisticsRecord>> UpsertAsync(
        StatisticsRecordWrite write,
        StatisticsFieldMergeMode mergeMode = StatisticsFieldMergeMode.Merge,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns whether a record with the given identity exists.
    /// </summary>
    Task<Result<bool>> ExistsAsync(string ns, string kind, string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the full record, or <c>null</c> when no record matches.
    /// </summary>
    Task<Result<StatisticsRecord?>> GetAsync(string ns, string kind, string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns only the requested fields. When the record exists but a requested field is missing,
    /// it is omitted from the result map. When the record itself is missing, the result is <c>null</c>.
    /// </summary>
    Task<Result<IReadOnlyDictionary<string, StatValue>?>> GetFieldsAsync(
        string ns,
        string kind,
        string key,
        IReadOnlyList<string> fieldNames,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a record. Returns success even when the record does not exist (idempotent).
    /// </summary>
    Task<Result> DeleteAsync(string ns, string kind, string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically increments a numeric (Integer) field on a record. Creates the record/field if missing.
    /// </summary>
    Task<Result<long>> IncrementAsync(
        string ns,
        string kind,
        string key,
        string fieldName,
        long delta,
        string sourceModule,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns records matching the supplied filter.
    /// </summary>
    Task<Result<IReadOnlyList<StatisticsRecord>>> QueryAsync(StatisticsQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers (or replaces) a schema. Internal modules typically register schemas at startup so
    /// later writes from any caller (including extensions) are validated against them.
    /// </summary>
    Task<Result> RegisterSchemaAsync(StatisticsSchema schema, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the schema registered for a (Namespace, Kind) pair, or <c>null</c> when none is registered.
    /// </summary>
    Task<Result<StatisticsSchema?>> GetSchemaAsync(string ns, string kind, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists registered schemas, optionally filtered by namespace.
    /// </summary>
    Task<Result<IReadOnlyList<StatisticsSchema>>> ListSchemasAsync(string? ns = null, CancellationToken cancellationToken = default);
}
