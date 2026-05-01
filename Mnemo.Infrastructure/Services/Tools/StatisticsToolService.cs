using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Mnemo.Core.Models.Statistics;
using Mnemo.Core.Models.Tools;
using Mnemo.Core.Models.Tools.Statistics;
using Mnemo.Core.Services;
using Mnemo.Infrastructure.Services.Statistics;

namespace Mnemo.Infrastructure.Services.Tools;

/// <summary>
/// Bridge between the function/tool registry and <see cref="IStatisticsManager"/>. Lets extensions
/// (and AI tooling) call into the statistics API without taking a direct dependency on Mnemo.Core.
/// Identity rules and a per-call default extension namespace prefix keep extension callers from
/// trampling internal-module data.
/// </summary>
public sealed class StatisticsToolService
{
    private readonly IStatisticsManager _stats;

    public StatisticsToolService(IStatisticsManager stats)
    {
        _stats = stats ?? throw new ArgumentNullException(nameof(stats));
    }

    /// <summary>Creates a new record. Equivalent to <see cref="IStatisticsManager.CreateAsync"/>.</summary>
    public async Task<ToolInvocationResult> CreateRecordAsync(StatsWriteRecordParameters p)
    {
        var (write, err) = await BuildWriteAsync(p, requireFields: true).ConfigureAwait(false);
        if (err != null) return err;

        var result = await _stats.CreateAsync(write!).ConfigureAwait(false);
        return result.IsSuccess
            ? ToolInvocationResult.Success("Record created.", RecordToDto(result.Value!))
            : ToolInvocationResult.Failure(MapErrorCode(result.ErrorMessage), result.ErrorMessage ?? "Create failed.");
    }

    /// <summary>Updates an existing record. Equivalent to <see cref="IStatisticsManager.UpdateAsync"/>.</summary>
    public async Task<ToolInvocationResult> UpdateRecordAsync(StatsWriteRecordParameters p)
    {
        var (write, err) = await BuildWriteAsync(p, requireFields: false).ConfigureAwait(false);
        if (err != null) return err;

        var mode = ParseMergeMode(p.MergeMode);
        var result = await _stats.UpdateAsync(write!, mode, p.ExpectedVersion).ConfigureAwait(false);
        return result.IsSuccess
            ? ToolInvocationResult.Success("Record updated.", RecordToDto(result.Value!))
            : ToolInvocationResult.Failure(MapErrorCode(result.ErrorMessage), result.ErrorMessage ?? "Update failed.");
    }

    /// <summary>Creates the record if missing or updates it otherwise.</summary>
    public async Task<ToolInvocationResult> UpsertRecordAsync(StatsWriteRecordParameters p)
    {
        var (write, err) = await BuildWriteAsync(p, requireFields: false).ConfigureAwait(false);
        if (err != null) return err;

        var mode = ParseMergeMode(p.MergeMode);
        var result = await _stats.UpsertAsync(write!, mode).ConfigureAwait(false);
        return result.IsSuccess
            ? ToolInvocationResult.Success("Record upserted.", RecordToDto(result.Value!))
            : ToolInvocationResult.Failure(MapErrorCode(result.ErrorMessage), result.ErrorMessage ?? "Upsert failed.");
    }

    /// <summary>Returns whether a record exists.</summary>
    public async Task<ToolInvocationResult> ExistsAsync(StatsRecordIdentityParameters p)
    {
        var idErr = ValidateIdentity(p.Namespace, p.Kind, p.Key);
        if (idErr != null) return idErr;

        var result = await _stats.ExistsAsync(p.Namespace.Trim(), p.Kind.Trim(), p.Key.Trim()).ConfigureAwait(false);
        if (!result.IsSuccess)
            return ToolInvocationResult.Failure(MapErrorCode(result.ErrorMessage), result.ErrorMessage ?? "Exists check failed.");

        return ToolInvocationResult.Success(
            result.Value ? "Record exists." : "Record does not exist.",
            new { exists = result.Value, @namespace = p.Namespace.Trim(), kind = p.Kind.Trim(), key = p.Key.Trim() });
    }

    /// <summary>Returns the full record.</summary>
    public async Task<ToolInvocationResult> GetRecordAsync(StatsRecordIdentityParameters p)
    {
        var idErr = ValidateIdentity(p.Namespace, p.Kind, p.Key);
        if (idErr != null) return idErr;

        var result = await _stats.GetAsync(p.Namespace.Trim(), p.Kind.Trim(), p.Key.Trim()).ConfigureAwait(false);
        if (!result.IsSuccess)
            return ToolInvocationResult.Failure(MapErrorCode(result.ErrorMessage), result.ErrorMessage ?? "Read failed.");
        if (result.Value == null)
            return ToolInvocationResult.Failure(ToolResultCodes.NotFound, "Record not found.");

        return ToolInvocationResult.Success("OK", RecordToDto(result.Value));
    }

    /// <summary>Returns the requested fields from a record.</summary>
    public async Task<ToolInvocationResult> ReadFieldsAsync(StatsReadFieldsParameters p)
    {
        var idErr = ValidateIdentity(p.Namespace, p.Kind, p.Key);
        if (idErr != null) return idErr;
        if (p.Fields is not { Count: > 0 })
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, "fields is required.");

        var result = await _stats
            .GetFieldsAsync(p.Namespace.Trim(), p.Kind.Trim(), p.Key.Trim(), p.Fields.ToArray())
            .ConfigureAwait(false);
        if (!result.IsSuccess)
            return ToolInvocationResult.Failure(MapErrorCode(result.ErrorMessage), result.ErrorMessage ?? "Read failed.");
        if (result.Value == null)
            return ToolInvocationResult.Failure(ToolResultCodes.NotFound, "Record not found.");

        return ToolInvocationResult.Success("OK", new { fields = ToBoxedDictionary(result.Value) });
    }

    /// <summary>Atomically increments an Integer field on a record.</summary>
    public async Task<ToolInvocationResult> IncrementFieldAsync(StatsIncrementFieldParameters p)
    {
        var idErr = ValidateIdentity(p.Namespace, p.Kind, p.Key);
        if (idErr != null) return idErr;
        if (string.IsNullOrWhiteSpace(p.Field))
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, "field is required.");

        var source = string.IsNullOrWhiteSpace(p.SourceModule) ? p.Namespace.Trim() : p.SourceModule!.Trim();
        var result = await _stats.IncrementAsync(p.Namespace.Trim(), p.Kind.Trim(), p.Key.Trim(),
            p.Field.Trim(), p.Delta, source).ConfigureAwait(false);

        return result.IsSuccess
            ? ToolInvocationResult.Success("Field incremented.", new { value = result.Value })
            : ToolInvocationResult.Failure(MapErrorCode(result.ErrorMessage), result.ErrorMessage ?? "Increment failed.");
    }

    /// <summary>Deletes a record.</summary>
    public async Task<ToolInvocationResult> DeleteRecordAsync(StatsRecordIdentityParameters p)
    {
        var idErr = ValidateIdentity(p.Namespace, p.Kind, p.Key);
        if (idErr != null) return idErr;

        var result = await _stats.DeleteAsync(p.Namespace.Trim(), p.Kind.Trim(), p.Key.Trim()).ConfigureAwait(false);
        return result.IsSuccess
            ? ToolInvocationResult.Success("Record deleted.")
            : ToolInvocationResult.Failure(MapErrorCode(result.ErrorMessage), result.ErrorMessage ?? "Delete failed.");
    }

    /// <summary>Lists records matching the supplied filter (namespace required).</summary>
    public async Task<ToolInvocationResult> QueryAsync(StatsQueryParameters p)
    {
        if (string.IsNullOrWhiteSpace(p.Namespace))
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, "namespace is required.");

        DateTimeOffset? after = null, before = null;
        if (!string.IsNullOrEmpty(p.UpdatedAfter) &&
            !DateTimeOffset.TryParse(p.UpdatedAfter, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var a))
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, "updated_after is not a valid ISO datetime.");
        else if (!string.IsNullOrEmpty(p.UpdatedAfter))
            after = DateTimeOffset.Parse(p.UpdatedAfter!, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

        if (!string.IsNullOrEmpty(p.UpdatedBefore) &&
            !DateTimeOffset.TryParse(p.UpdatedBefore, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var b))
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, "updated_before is not a valid ISO datetime.");
        else if (!string.IsNullOrEmpty(p.UpdatedBefore))
            before = DateTimeOffset.Parse(p.UpdatedBefore!, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

        var query = new StatisticsQuery
        {
            Namespace = p.Namespace.Trim(),
            Kind = string.IsNullOrWhiteSpace(p.Kind) ? null : p.Kind!.Trim(),
            KeyPrefix = string.IsNullOrWhiteSpace(p.KeyPrefix) ? null : p.KeyPrefix!.Trim(),
            UpdatedAfter = after,
            UpdatedBefore = before,
            Limit = p.Limit ?? 0,
            OrderByUpdatedDescending = true
        };

        var result = await _stats.QueryAsync(query).ConfigureAwait(false);
        if (!result.IsSuccess)
            return ToolInvocationResult.Failure(ToolResultCodes.InternalError, result.ErrorMessage ?? "Query failed.");

        var records = result.Value!.Select(RecordToDto).ToList();
        return ToolInvocationResult.Success($"Returned {records.Count} record(s).", new { records });
    }

    /// <summary>Lists registered schemas, optionally scoped to a namespace.</summary>
    public async Task<ToolInvocationResult> ListSchemasAsync(StatsListSchemasParameters p)
    {
        var ns = string.IsNullOrWhiteSpace(p.Namespace) ? null : p.Namespace!.Trim();
        var result = await _stats.ListSchemasAsync(ns).ConfigureAwait(false);
        if (!result.IsSuccess)
            return ToolInvocationResult.Failure(ToolResultCodes.InternalError, result.ErrorMessage ?? "List failed.");

        var schemas = result.Value!.Select(SchemaToDto).ToList();
        return ToolInvocationResult.Success($"Returned {schemas.Count} schema(s).", new { schemas });
    }

    private async Task<(StatisticsRecordWrite? write, ToolInvocationResult? error)> BuildWriteAsync(
        StatsWriteRecordParameters p, bool requireFields)
    {
        var idErr = ValidateIdentity(p.Namespace, p.Kind, p.Key);
        if (idErr != null) return (null, idErr);

        if (requireFields && (p.Fields == null || p.Fields.Count == 0))
            return (null, ToolInvocationResult.Failure(ToolResultCodes.ValidationError, "fields is required."));

        var schema = (await _stats.GetSchemaAsync(p.Namespace.Trim(), p.Kind.Trim()).ConfigureAwait(false)).Value;
        var declared = schema?.Fields.ToDictionary(f => f.Name, StringComparer.Ordinal);

        var fields = new Dictionary<string, StatValue>(StringComparer.Ordinal);
        if (p.Fields != null)
        {
            foreach (var kv in p.Fields)
            {
                if (string.IsNullOrEmpty(kv.Key))
                    return (null, ToolInvocationResult.Failure(ToolResultCodes.ValidationError, "field name cannot be empty."));

                StatValueType? expected = null;
                if (declared != null && declared.TryGetValue(kv.Key, out var def))
                    expected = def.Type;

                if (!StatValueJson.TryCoerce(kv.Value, expected, out var coerced, out var coerceErr))
                    return (null, ToolInvocationResult.Failure(ToolResultCodes.ValidationError,
                        $"field '{kv.Key}': {coerceErr}"));

                fields[kv.Key] = coerced;
            }
        }

        var write = new StatisticsRecordWrite
        {
            Namespace = p.Namespace.Trim(),
            Kind = p.Kind.Trim(),
            Key = p.Key.Trim(),
            SourceModule = string.IsNullOrWhiteSpace(p.SourceModule) ? p.Namespace.Trim() : p.SourceModule!.Trim(),
            Fields = fields,
            MetadataJson = string.IsNullOrEmpty(p.MetadataJson) ? null : p.MetadataJson
        };
        return (write, null);
    }

    private static ToolInvocationResult? ValidateIdentity(string ns, string kind, string key)
    {
        if (string.IsNullOrWhiteSpace(ns))
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, "namespace is required.");
        if (string.IsNullOrWhiteSpace(kind))
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, "kind is required.");
        if (string.IsNullOrWhiteSpace(key))
            return ToolInvocationResult.Failure(ToolResultCodes.ValidationError, "key is required.");
        return null;
    }

    private static StatisticsFieldMergeMode ParseMergeMode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return StatisticsFieldMergeMode.Merge;
        return raw.Trim().ToLowerInvariant() switch
        {
            "replace" => StatisticsFieldMergeMode.Replace,
            _ => StatisticsFieldMergeMode.Merge
        };
    }

    private static string MapErrorCode(string? message)
    {
        if (string.IsNullOrEmpty(message)) return ToolResultCodes.InternalError;
        if (message.Contains("not found", StringComparison.OrdinalIgnoreCase)) return ToolResultCodes.NotFound;
        if (message.Contains("already exists", StringComparison.OrdinalIgnoreCase)) return ToolResultCodes.Conflict;
        if (message.Contains("conflict", StringComparison.OrdinalIgnoreCase)) return ToolResultCodes.Conflict;
        if (message.Contains("required", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("invalid", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("expected", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("must be", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("not allowed", StringComparison.OrdinalIgnoreCase))
            return ToolResultCodes.ValidationError;
        return ToolResultCodes.InternalError;
    }

    private static object RecordToDto(StatisticsRecord record) => new
    {
        @namespace = record.Namespace,
        kind = record.Kind,
        key = record.Key,
        created_at = record.CreatedAt,
        updated_at = record.UpdatedAt,
        version = record.Version,
        source_module = record.SourceModule,
        fields = ToBoxedDictionary(record.Fields),
        metadata_json = record.MetadataJson
    };

    private static object SchemaToDto(StatisticsSchema schema) => new
    {
        @namespace = schema.Namespace,
        kind = schema.Kind,
        description = schema.Description,
        allow_additional_fields = schema.AllowAdditionalFields,
        max_metadata_bytes = schema.MaxMetadataBytes,
        allowed_source_modules = schema.AllowedSourceModules,
        fields = schema.Fields.Select(f => new
        {
            name = f.Name,
            type = f.Type.ToString(),
            required = f.Required,
            default_value = f.DefaultValue?.ToBoxed(),
            min_value = f.MinValue?.ToBoxed(),
            max_value = f.MaxValue?.ToBoxed(),
            allowed_string_values = f.AllowedStringValues
        }).ToList()
    };

    private static IReadOnlyDictionary<string, object> ToBoxedDictionary(IReadOnlyDictionary<string, StatValue> fields)
    {
        var d = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var kv in fields)
            d[kv.Key] = kv.Value.ToBoxed();
        return d;
    }
}
