using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Mnemo.Core.Models.Statistics;
using Mnemo.Core.Services;
using Mnemo.Infrastructure.Common;

namespace Mnemo.Infrastructure.Services.Statistics;

/// <summary>
/// Sqlite-backed implementation of <see cref="IStatisticsStore"/>. Records are stored in a
/// dedicated table with a composite primary key (<c>Namespace</c>, <c>Kind</c>, <c>Key</c>) so
/// reads scoped to a namespace/kind/key prefix remain efficient as the dataset grows.
/// </summary>
internal sealed class SqliteStatisticsStore : IStatisticsStore
{
    private const string TableName = "Statistics";
    private readonly string _connectionString;
    private readonly ILoggerService _logger;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public SqliteStatisticsStore(ILoggerService logger)
    {
        _logger = logger;
        var dbPath = MnemoAppPaths.GetLocalUserDataFile("mnemo.db");
        var dbDir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrWhiteSpace(dbDir))
            Directory.CreateDirectory(dbDir);
        _connectionString = $"Data Source={dbPath}";
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $@"CREATE TABLE IF NOT EXISTS {TableName} (
            Namespace   TEXT NOT NULL,
            Kind        TEXT NOT NULL,
            Key         TEXT NOT NULL,
            CreatedAt   TEXT NOT NULL,
            UpdatedAt   TEXT NOT NULL,
            Version     INTEGER NOT NULL,
            SourceModule TEXT NOT NULL,
            FieldsJson  TEXT NOT NULL,
            MetadataJson TEXT,
            PRIMARY KEY (Namespace, Kind, Key)
        );";
        command.ExecuteNonQuery();

        using var indexCmd = connection.CreateCommand();
        indexCmd.CommandText =
            $"CREATE INDEX IF NOT EXISTS IX_{TableName}_Updated ON {TableName} (Namespace, Kind, UpdatedAt DESC);";
        indexCmd.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public async Task<StatisticsRecord?> GetAsync(string ns, string kind, string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $@"SELECT Namespace, Kind, Key, CreatedAt, UpdatedAt, Version, SourceModule, FieldsJson, MetadataJson
                FROM {TableName} WHERE Namespace = $ns AND Kind = $kind AND Key = $k LIMIT 1;";
            cmd.Parameters.AddWithValue("$ns", ns);
            cmd.Parameters.AddWithValue("$kind", kind);
            cmd.Parameters.AddWithValue("$k", key);
            using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                return ReadRecord(reader);
            return null;
        }
        catch (Exception ex)
        {
            _logger.Error("Statistics", $"GetAsync failed for {ns}/{kind}/{key}.", ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string ns, string kind, string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT 1 FROM {TableName} WHERE Namespace = $ns AND Kind = $kind AND Key = $k LIMIT 1;";
            cmd.Parameters.AddWithValue("$ns", ns);
            cmd.Parameters.AddWithValue("$kind", kind);
            cmd.Parameters.AddWithValue("$k", key);
            var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result != null && result != DBNull.Value;
        }
        catch (Exception ex)
        {
            _logger.Error("Statistics", $"ExistsAsync failed for {ns}/{kind}/{key}.", ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<StatisticsRecord> InsertAsync(StatisticsRecord record, CancellationToken cancellationToken = default)
    {
        if (record == null) throw new ArgumentNullException(nameof(record));
        cancellationToken.ThrowIfCancellationRequested();

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $@"INSERT INTO {TableName}
                (Namespace, Kind, Key, CreatedAt, UpdatedAt, Version, SourceModule, FieldsJson, MetadataJson)
                VALUES ($ns, $kind, $k, $created, $updated, $version, $source, $fields, $meta);";
            BindRecord(cmd, record);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return record;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<StatisticsRecord?> UpdateAsync(StatisticsRecord record, long? expectedVersion, CancellationToken cancellationToken = default)
    {
        if (record == null) throw new ArgumentNullException(nameof(record));
        cancellationToken.ThrowIfCancellationRequested();

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            using var cmd = connection.CreateCommand();
            if (expectedVersion.HasValue)
            {
                cmd.CommandText = $@"UPDATE {TableName}
                    SET UpdatedAt = $updated, Version = $version, SourceModule = $source,
                        FieldsJson = $fields, MetadataJson = $meta
                    WHERE Namespace = $ns AND Kind = $kind AND Key = $k AND Version = $expected;";
                cmd.Parameters.AddWithValue("$expected", expectedVersion.Value);
            }
            else
            {
                cmd.CommandText = $@"UPDATE {TableName}
                    SET UpdatedAt = $updated, Version = $version, SourceModule = $source,
                        FieldsJson = $fields, MetadataJson = $meta
                    WHERE Namespace = $ns AND Kind = $kind AND Key = $k;";
            }
            BindRecord(cmd, record, includeCreated: false);

            var rows = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return rows == 1 ? record : null;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string ns, string kind, string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"DELETE FROM {TableName} WHERE Namespace = $ns AND Kind = $kind AND Key = $k;";
            cmd.Parameters.AddWithValue("$ns", ns);
            cmd.Parameters.AddWithValue("$kind", kind);
            cmd.Parameters.AddWithValue("$k", key);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StatisticsRecord>> QueryAsync(StatisticsQuery query, CancellationToken cancellationToken = default)
    {
        if (query == null) throw new ArgumentNullException(nameof(query));
        cancellationToken.ThrowIfCancellationRequested();

        var limit = query.Limit > 0 ? Math.Min(query.Limit, 1000) : 100;

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = connection.CreateCommand();
        var sql = $@"SELECT Namespace, Kind, Key, CreatedAt, UpdatedAt, Version, SourceModule, FieldsJson, MetadataJson
            FROM {TableName} WHERE Namespace = $ns";

        cmd.Parameters.AddWithValue("$ns", query.Namespace);

        if (!string.IsNullOrEmpty(query.Kind))
        {
            sql += " AND Kind = $kind";
            cmd.Parameters.AddWithValue("$kind", query.Kind);
        }

        if (!string.IsNullOrEmpty(query.KeyPrefix))
        {
            sql += " AND Key LIKE $prefix";
            cmd.Parameters.AddWithValue("$prefix", query.KeyPrefix + "%");
        }

        if (query.UpdatedAfter.HasValue)
        {
            sql += " AND UpdatedAt >= $after";
            cmd.Parameters.AddWithValue("$after", query.UpdatedAfter.Value.ToString("O"));
        }

        if (query.UpdatedBefore.HasValue)
        {
            sql += " AND UpdatedAt <= $before";
            cmd.Parameters.AddWithValue("$before", query.UpdatedBefore.Value.ToString("O"));
        }

        sql += query.OrderByUpdatedDescending
            ? " ORDER BY UpdatedAt DESC LIMIT $limit;"
            : " ORDER BY UpdatedAt ASC LIMIT $limit;";
        cmd.Parameters.AddWithValue("$limit", limit);

        cmd.CommandText = sql;

        var list = new List<StatisticsRecord>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            list.Add(ReadRecord(reader));
        return list;
    }

    private static void BindRecord(SqliteCommand cmd, StatisticsRecord record, bool includeCreated = true)
    {
        cmd.Parameters.AddWithValue("$ns", record.Namespace);
        cmd.Parameters.AddWithValue("$kind", record.Kind);
        cmd.Parameters.AddWithValue("$k", record.Key);
        if (includeCreated)
            cmd.Parameters.AddWithValue("$created", record.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$updated", record.UpdatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$version", record.Version);
        cmd.Parameters.AddWithValue("$source", record.SourceModule);
        cmd.Parameters.AddWithValue("$fields", SerializeFields(record.Fields));
        cmd.Parameters.AddWithValue("$meta", (object?)record.MetadataJson ?? DBNull.Value);
    }

    private static StatisticsRecord ReadRecord(SqliteDataReader reader)
    {
        var ns = reader.GetString(0);
        var kind = reader.GetString(1);
        var key = reader.GetString(2);
        var created = DateTimeOffset.Parse(reader.GetString(3));
        var updated = DateTimeOffset.Parse(reader.GetString(4));
        var version = reader.GetInt64(5);
        var source = reader.GetString(6);
        var fieldsJson = reader.GetString(7);
        var metadata = reader.IsDBNull(8) ? null : reader.GetString(8);

        return new StatisticsRecord
        {
            Namespace = ns,
            Kind = kind,
            Key = key,
            CreatedAt = created,
            UpdatedAt = updated,
            Version = version,
            SourceModule = source,
            Fields = DeserializeFields(fieldsJson),
            MetadataJson = metadata
        };
    }

    private static string SerializeFields(IReadOnlyDictionary<string, StatValue> fields)
    {
        var obj = new JsonObject();
        foreach (var kv in fields)
            obj[kv.Key] = StatValueJson.ToTagged(kv.Value);
        return obj.ToJsonString();
    }

    private static IReadOnlyDictionary<string, StatValue> DeserializeFields(string json)
    {
        var result = new Dictionary<string, StatValue>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json))
            return result;

        var node = JsonNode.Parse(json);
        if (node is not JsonObject obj)
            return result;

        foreach (var kv in obj)
        {
            if (kv.Value == null) continue;
            try
            {
                result[kv.Key] = StatValueJson.FromTagged(kv.Value);
            }
            catch
            {
                // Skip fields that cannot be parsed; they are effectively dropped on read so they
                // do not block widget queries when storage shape evolves.
            }
        }
        return result;
    }
}
