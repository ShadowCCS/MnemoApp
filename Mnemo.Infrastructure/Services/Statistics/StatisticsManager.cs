using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Models.Statistics;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.Statistics;

/// <summary>
/// Default <see cref="IStatisticsManager"/> implementation. Owns identity validation, schema
/// enforcement (when registered), and optimistic concurrency. Persistence is delegated to an
/// <see cref="IStatisticsStore"/>.
/// </summary>
public sealed class StatisticsManager : IStatisticsManager
{
    private readonly IStatisticsStore _store;
    private readonly StatisticsSchemaRegistry _schemas;
    private readonly ILoggerService _logger;
    private readonly Func<DateTimeOffset> _now;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    /// <summary>Production constructor used by DI. Uses Sqlite storage.</summary>
    public StatisticsManager(ILoggerService logger)
        : this(new SqliteStatisticsStore(logger), new StatisticsSchemaRegistry(), logger, () => DateTimeOffset.UtcNow)
    {
    }

    /// <summary>Test/extension constructor allowing the store, schemas, and clock to be injected.</summary>
    internal StatisticsManager(
        IStatisticsStore store,
        StatisticsSchemaRegistry schemas,
        ILoggerService logger,
        Func<DateTimeOffset> now)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _schemas = schemas ?? throw new ArgumentNullException(nameof(schemas));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Test factory: builds a manager with an in-memory store and a fixed clock.</summary>
    internal static StatisticsManager CreateInMemory(ILoggerService logger, Func<DateTimeOffset>? now = null)
        => new(new InMemoryStatisticsStore(), new StatisticsSchemaRegistry(), logger, now ?? (() => DateTimeOffset.UtcNow));

    /// <inheritdoc />
    public async Task<Result<StatisticsRecord>> CreateAsync(StatisticsRecordWrite write, CancellationToken cancellationToken = default)
    {
        if (write == null) return Result<StatisticsRecord>.Failure("write is required.");
        var idErr = ValidateIdentity(write.Namespace, write.Kind, write.Key);
        if (idErr != null) return Result<StatisticsRecord>.Failure(idErr);

        var schema = _schemas.Get(write.Namespace, write.Kind);
        var sourceErr = ValidateSourceModule(schema, write.SourceModule);
        if (sourceErr != null) return Result<StatisticsRecord>.Failure(sourceErr);

        if (!ValidateMetadata(schema, write.MetadataJson, out var metaErr))
            return Result<StatisticsRecord>.Failure(metaErr!);

        if (!TryBuildFields(schema, write.Fields, isCreate: true, out var fields, out var fieldErr))
            return Result<StatisticsRecord>.Failure(fieldErr!);

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (await _store.ExistsAsync(write.Namespace, write.Kind, write.Key, cancellationToken).ConfigureAwait(false))
                return Result<StatisticsRecord>.Failure($"Record already exists at {write.Namespace}/{write.Kind}/{write.Key}.");

            var nowUtc = _now();
            var record = new StatisticsRecord
            {
                Namespace = write.Namespace,
                Kind = write.Kind,
                Key = write.Key,
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc,
                Version = 1,
                SourceModule = write.SourceModule ?? string.Empty,
                Fields = fields,
                MetadataJson = write.MetadataJson
            };
            var inserted = await _store.InsertAsync(record, cancellationToken).ConfigureAwait(false);
            return Result<StatisticsRecord>.Success(inserted);
        }
        catch (Exception ex)
        {
            _logger.Error("Statistics", $"CreateAsync failed for {write.Namespace}/{write.Kind}/{write.Key}.", ex);
            return Result<StatisticsRecord>.Failure(ex.Message, ex);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<Result<StatisticsRecord>> UpdateAsync(
        StatisticsRecordWrite write,
        StatisticsFieldMergeMode mergeMode = StatisticsFieldMergeMode.Merge,
        long? expectedVersion = null,
        CancellationToken cancellationToken = default)
    {
        if (write == null) return Result<StatisticsRecord>.Failure("write is required.");
        var idErr = ValidateIdentity(write.Namespace, write.Kind, write.Key);
        if (idErr != null) return Result<StatisticsRecord>.Failure(idErr);

        var schema = _schemas.Get(write.Namespace, write.Kind);
        var sourceErr = ValidateSourceModule(schema, write.SourceModule);
        if (sourceErr != null) return Result<StatisticsRecord>.Failure(sourceErr);

        if (!ValidateMetadata(schema, write.MetadataJson, out var metaErr))
            return Result<StatisticsRecord>.Failure(metaErr!);

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = await _store.GetAsync(write.Namespace, write.Kind, write.Key, cancellationToken).ConfigureAwait(false);
            if (existing == null)
                return Result<StatisticsRecord>.Failure($"Record not found at {write.Namespace}/{write.Kind}/{write.Key}.");

            if (expectedVersion.HasValue && existing.Version != expectedVersion.Value)
                return Result<StatisticsRecord>.Failure(
                    $"Version conflict (expected {expectedVersion.Value}, current {existing.Version}).");

            var merged = MergeFields(existing.Fields, write.Fields, mergeMode);
            if (!TryBuildFields(schema, merged, isCreate: false, out var validated, out var fieldErr))
                return Result<StatisticsRecord>.Failure(fieldErr!);

            var record = new StatisticsRecord
            {
                Namespace = existing.Namespace,
                Kind = existing.Kind,
                Key = existing.Key,
                CreatedAt = existing.CreatedAt,
                UpdatedAt = _now(),
                Version = existing.Version + 1,
                SourceModule = string.IsNullOrEmpty(write.SourceModule) ? existing.SourceModule : write.SourceModule,
                Fields = validated,
                MetadataJson = write.MetadataJson ?? existing.MetadataJson
            };

            var updated = await _store.UpdateAsync(record, expectedVersion ?? existing.Version, cancellationToken).ConfigureAwait(false);
            if (updated == null)
                return Result<StatisticsRecord>.Failure("Update rejected (concurrent modification).");
            return Result<StatisticsRecord>.Success(updated);
        }
        catch (Exception ex)
        {
            _logger.Error("Statistics", $"UpdateAsync failed for {write.Namespace}/{write.Kind}/{write.Key}.", ex);
            return Result<StatisticsRecord>.Failure(ex.Message, ex);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<Result<StatisticsRecord>> UpsertAsync(
        StatisticsRecordWrite write,
        StatisticsFieldMergeMode mergeMode = StatisticsFieldMergeMode.Merge,
        CancellationToken cancellationToken = default)
    {
        if (write == null) return Result<StatisticsRecord>.Failure("write is required.");
        var existsResult = await ExistsAsync(write.Namespace, write.Kind, write.Key, cancellationToken).ConfigureAwait(false);
        if (!existsResult.IsSuccess) return Result<StatisticsRecord>.Failure(existsResult.ErrorMessage ?? "Existence check failed.");

        return existsResult.Value
            ? await UpdateAsync(write, mergeMode, expectedVersion: null, cancellationToken).ConfigureAwait(false)
            : await CreateAsync(write, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<bool>> ExistsAsync(string ns, string kind, string key, CancellationToken cancellationToken = default)
    {
        var idErr = ValidateIdentity(ns, kind, key);
        if (idErr != null) return Result<bool>.Failure(idErr);
        try
        {
            return Result<bool>.Success(await _store.ExistsAsync(ns, kind, key, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            _logger.Error("Statistics", $"ExistsAsync failed for {ns}/{kind}/{key}.", ex);
            return Result<bool>.Failure(ex.Message, ex);
        }
    }

    /// <inheritdoc />
    public async Task<Result<StatisticsRecord?>> GetAsync(string ns, string kind, string key, CancellationToken cancellationToken = default)
    {
        var idErr = ValidateIdentity(ns, kind, key);
        if (idErr != null) return Result<StatisticsRecord?>.Failure(idErr);
        try
        {
            var rec = await _store.GetAsync(ns, kind, key, cancellationToken).ConfigureAwait(false);
            return Result<StatisticsRecord?>.Success(rec);
        }
        catch (Exception ex)
        {
            _logger.Error("Statistics", $"GetAsync failed for {ns}/{kind}/{key}.", ex);
            return Result<StatisticsRecord?>.Failure(ex.Message, ex);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyDictionary<string, StatValue>?>> GetFieldsAsync(
        string ns,
        string kind,
        string key,
        IReadOnlyList<string> fieldNames,
        CancellationToken cancellationToken = default)
    {
        var idErr = ValidateIdentity(ns, kind, key);
        if (idErr != null) return Result<IReadOnlyDictionary<string, StatValue>?>.Failure(idErr);
        if (fieldNames == null || fieldNames.Count == 0)
            return Result<IReadOnlyDictionary<string, StatValue>?>.Failure("fieldNames is required.");

        try
        {
            var rec = await _store.GetAsync(ns, kind, key, cancellationToken).ConfigureAwait(false);
            if (rec == null) return Result<IReadOnlyDictionary<string, StatValue>?>.Success(null);

            var result = new Dictionary<string, StatValue>(StringComparer.Ordinal);
            foreach (var name in fieldNames)
            {
                if (string.IsNullOrEmpty(name)) continue;
                if (rec.Fields.TryGetValue(name, out var v))
                    result[name] = v;
            }
            return Result<IReadOnlyDictionary<string, StatValue>?>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.Error("Statistics", $"GetFieldsAsync failed for {ns}/{kind}/{key}.", ex);
            return Result<IReadOnlyDictionary<string, StatValue>?>.Failure(ex.Message, ex);
        }
    }

    /// <inheritdoc />
    public async Task<Result> DeleteAsync(string ns, string kind, string key, CancellationToken cancellationToken = default)
    {
        var idErr = ValidateIdentity(ns, kind, key);
        if (idErr != null) return Result.Failure(idErr);
        try
        {
            await _store.DeleteAsync(ns, kind, key, cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.Error("Statistics", $"DeleteAsync failed for {ns}/{kind}/{key}.", ex);
            return Result.Failure(ex.Message, ex);
        }
    }

    /// <inheritdoc />
    public async Task<Result<long>> IncrementAsync(
        string ns,
        string kind,
        string key,
        string fieldName,
        long delta,
        string sourceModule,
        CancellationToken cancellationToken = default)
    {
        var idErr = ValidateIdentity(ns, kind, key);
        if (idErr != null) return Result<long>.Failure(idErr);
        if (string.IsNullOrEmpty(fieldName))
            return Result<long>.Failure("fieldName is required.");

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = await _store.GetAsync(ns, kind, key, cancellationToken).ConfigureAwait(false);
            var nowUtc = _now();

            long newValue;
            if (existing == null)
            {
                newValue = delta;
                var fields = new Dictionary<string, StatValue>(StringComparer.Ordinal)
                {
                    [fieldName] = StatValue.FromInt(newValue)
                };
                var schema = _schemas.Get(ns, kind);
                if (!TryBuildFields(schema, fields, isCreate: true, out var validated, out var fieldErr))
                    return Result<long>.Failure(fieldErr!);

                await _store.InsertAsync(new StatisticsRecord
                {
                    Namespace = ns,
                    Kind = kind,
                    Key = key,
                    CreatedAt = nowUtc,
                    UpdatedAt = nowUtc,
                    Version = 1,
                    SourceModule = sourceModule ?? string.Empty,
                    Fields = validated,
                    MetadataJson = null
                }, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                long current = 0;
                if (existing.Fields.TryGetValue(fieldName, out var cur) && cur.Type == StatValueType.Integer)
                    current = cur.AsInt();
                newValue = current + delta;

                var fields = new Dictionary<string, StatValue>(existing.Fields, StringComparer.Ordinal)
                {
                    [fieldName] = StatValue.FromInt(newValue)
                };

                var schema = _schemas.Get(ns, kind);
                if (!TryBuildFields(schema, fields, isCreate: false, out var validated, out var fieldErr))
                    return Result<long>.Failure(fieldErr!);

                var record = new StatisticsRecord
                {
                    Namespace = existing.Namespace,
                    Kind = existing.Kind,
                    Key = existing.Key,
                    CreatedAt = existing.CreatedAt,
                    UpdatedAt = nowUtc,
                    Version = existing.Version + 1,
                    SourceModule = string.IsNullOrEmpty(sourceModule) ? existing.SourceModule : sourceModule,
                    Fields = validated,
                    MetadataJson = existing.MetadataJson
                };
                var updated = await _store.UpdateAsync(record, existing.Version, cancellationToken).ConfigureAwait(false);
                if (updated == null)
                    return Result<long>.Failure("Increment rejected (concurrent modification).");
            }
            return Result<long>.Success(newValue);
        }
        catch (Exception ex)
        {
            _logger.Error("Statistics", $"IncrementAsync failed for {ns}/{kind}/{key}.", ex);
            return Result<long>.Failure(ex.Message, ex);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<StatisticsRecord>>> QueryAsync(StatisticsQuery query, CancellationToken cancellationToken = default)
    {
        if (query == null) return Result<IReadOnlyList<StatisticsRecord>>.Failure("query is required.");
        if (string.IsNullOrEmpty(query.Namespace))
            return Result<IReadOnlyList<StatisticsRecord>>.Failure("query.Namespace is required.");

        try
        {
            var list = await _store.QueryAsync(query, cancellationToken).ConfigureAwait(false);
            return Result<IReadOnlyList<StatisticsRecord>>.Success(list);
        }
        catch (Exception ex)
        {
            _logger.Error("Statistics", $"QueryAsync failed for {query.Namespace}/{query.Kind}.", ex);
            return Result<IReadOnlyList<StatisticsRecord>>.Failure(ex.Message, ex);
        }
    }

    /// <inheritdoc />
    public Task<Result> RegisterSchemaAsync(StatisticsSchema schema, CancellationToken cancellationToken = default)
    {
        if (schema == null)
            return Task.FromResult(Result.Failure("schema is required."));
        var idErr = ValidateIdentity(schema.Namespace, schema.Kind, "schema");
        if (idErr != null) return Task.FromResult(Result.Failure(idErr));

        try
        {
            _schemas.Register(schema);
            return Task.FromResult(Result.Success());
        }
        catch (Exception ex)
        {
            _logger.Error("Statistics", $"RegisterSchemaAsync failed for {schema.Namespace}/{schema.Kind}.", ex);
            return Task.FromResult(Result.Failure(ex.Message, ex));
        }
    }

    /// <inheritdoc />
    public Task<Result<StatisticsSchema?>> GetSchemaAsync(string ns, string kind, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(ns) || string.IsNullOrEmpty(kind))
            return Task.FromResult(Result<StatisticsSchema?>.Failure("namespace and kind are required."));
        return Task.FromResult(Result<StatisticsSchema?>.Success(_schemas.Get(ns, kind)));
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<StatisticsSchema>>> ListSchemasAsync(string? ns = null, CancellationToken cancellationToken = default)
        => Task.FromResult(Result<IReadOnlyList<StatisticsSchema>>.Success(_schemas.List(ns)));

    private static string? ValidateIdentity(string ns, string kind, string key)
    {
        if (string.IsNullOrWhiteSpace(ns) || ContainsInvalid(ns))
            return "namespace is required and may not contain control or whitespace characters.";
        if (string.IsNullOrWhiteSpace(kind) || ContainsInvalid(kind))
            return "kind is required and may not contain control or whitespace characters.";
        if (string.IsNullOrWhiteSpace(key) || ContainsInvalidKey(key))
            return "key is required and may not contain control characters.";
        return null;
    }

    private static bool ContainsInvalid(string value)
    {
        foreach (var c in value)
        {
            if (char.IsControl(c) || c == ' ' || c == '\t' || c == '\n' || c == '\r')
                return true;
        }
        return false;
    }

    private static bool ContainsInvalidKey(string value)
    {
        foreach (var c in value)
        {
            if (char.IsControl(c)) return true;
        }
        return false;
    }

    private static string? ValidateSourceModule(StatisticsSchema? schema, string? sourceModule)
    {
        if (schema == null || schema.AllowedSourceModules.Count == 0)
            return null;
        if (string.IsNullOrEmpty(sourceModule))
            return $"source module is required for {schema.Namespace}/{schema.Kind}.";
        foreach (var allowed in schema.AllowedSourceModules)
        {
            if (string.Equals(allowed, sourceModule, StringComparison.Ordinal))
                return null;
        }
        return $"source module '{sourceModule}' is not allowed to write {schema.Namespace}/{schema.Kind}.";
    }

    private static bool ValidateMetadata(StatisticsSchema? schema, string? metadataJson, out string? error)
    {
        error = null;
        if (string.IsNullOrEmpty(metadataJson))
            return true;
        var max = schema?.MaxMetadataBytes ?? 0;
        if (max > 0 && Encoding.UTF8.GetByteCount(metadataJson) > max)
        {
            error = $"metadata exceeds the {max}-byte limit.";
            return false;
        }
        try
        {
            using var _ = System.Text.Json.JsonDocument.Parse(metadataJson);
            return true;
        }
        catch (Exception ex)
        {
            error = $"metadata is not valid JSON: {ex.Message}";
            return false;
        }
    }

    private static IReadOnlyDictionary<string, StatValue> MergeFields(
        IReadOnlyDictionary<string, StatValue> existing,
        IReadOnlyDictionary<string, StatValue> incoming,
        StatisticsFieldMergeMode mode)
    {
        if (mode == StatisticsFieldMergeMode.Replace)
        {
            return new Dictionary<string, StatValue>(incoming, StringComparer.Ordinal);
        }
        var merged = new Dictionary<string, StatValue>(existing, StringComparer.Ordinal);
        foreach (var kv in incoming)
            merged[kv.Key] = kv.Value;
        return merged;
    }

    private static bool TryBuildFields(
        StatisticsSchema? schema,
        IReadOnlyDictionary<string, StatValue> incoming,
        bool isCreate,
        out IReadOnlyDictionary<string, StatValue> result,
        out string? error)
    {
        error = null;
        var fields = new Dictionary<string, StatValue>(incoming, StringComparer.Ordinal);

        if (schema == null)
        {
            result = fields;
            return true;
        }

        var declared = schema.Fields.ToDictionary(f => f.Name, StringComparer.Ordinal);

        if (!schema.AllowAdditionalFields)
        {
            foreach (var name in fields.Keys)
            {
                if (!declared.ContainsKey(name))
                {
                    result = fields;
                    error = $"field '{name}' is not declared on schema {schema.Namespace}/{schema.Kind}.";
                    return false;
                }
            }
        }

        foreach (var def in schema.Fields)
        {
            if (!fields.TryGetValue(def.Name, out var value))
            {
                if (isCreate && def.DefaultValue.HasValue)
                {
                    fields[def.Name] = def.DefaultValue.Value;
                }
                else if (isCreate && def.Required)
                {
                    result = fields;
                    error = $"field '{def.Name}' is required.";
                    return false;
                }
                continue;
            }

            if (value.Type != def.Type)
            {
                if (def.Type == StatValueType.Decimal && value.Type == StatValueType.Integer)
                {
                    fields[def.Name] = StatValue.FromDecimal(value.AsInt());
                }
                else
                {
                    result = fields;
                    error = $"field '{def.Name}' expected {def.Type} but got {value.Type}.";
                    return false;
                }
            }

            if (!ValidateRange(def, fields[def.Name], out var rangeErr))
            {
                result = fields;
                error = rangeErr;
                return false;
            }

            if (def.Type == StatValueType.String && def.AllowedStringValues is { Count: > 0 })
            {
                var s = fields[def.Name].AsString();
                var ok = false;
                foreach (var allowed in def.AllowedStringValues)
                {
                    if (string.Equals(allowed, s, StringComparison.Ordinal)) { ok = true; break; }
                }
                if (!ok)
                {
                    result = fields;
                    error = $"field '{def.Name}' value '{s}' is not in the allowed list.";
                    return false;
                }
            }
        }

        result = fields;
        return true;
    }

    private static bool ValidateRange(StatisticsFieldDefinition def, StatValue value, out string? error)
    {
        error = null;
        if (def.MinValue is { } min)
        {
            if (def.Type == StatValueType.Integer && value.AsInt() < min.AsInt())
            {
                error = $"field '{def.Name}' must be >= {min}.";
                return false;
            }
            if (def.Type == StatValueType.Decimal && value.AsDecimal() < min.AsDecimal())
            {
                error = $"field '{def.Name}' must be >= {min}.";
                return false;
            }
        }
        if (def.MaxValue is { } max)
        {
            if (def.Type == StatValueType.Integer && value.AsInt() > max.AsInt())
            {
                error = $"field '{def.Name}' must be <= {max}.";
                return false;
            }
            if (def.Type == StatValueType.Decimal && value.AsDecimal() > max.AsDecimal())
            {
                error = $"field '{def.Name}' must be <= {max}.";
                return false;
            }
        }
        return true;
    }
}
