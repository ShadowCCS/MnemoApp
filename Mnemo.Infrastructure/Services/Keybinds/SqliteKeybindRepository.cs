using System.Text.Json;
using Microsoft.Data.Sqlite;
using Mnemo.Core.Models.Keybinds;
using Mnemo.Core.Services;
using Mnemo.Infrastructure.Common;

namespace Mnemo.Infrastructure.Services.Keybinds;

public sealed class SqliteKeybindRepository : IKeybindRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly string _connectionString;
    private readonly ILoggerService _logger;
    private readonly Lazy<Task> _init;

    /// <param name="databasePath">Optional absolute DB path (e.g. tests). Defaults to app user data <c>mnemo.db</c>.</param>
    public SqliteKeybindRepository(ILoggerService logger, string? databasePath = null)
    {
        _logger = logger;
        var dbPath = databasePath ?? MnemoAppPaths.GetLocalUserDataFile("mnemo.db");
        var dbDir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrWhiteSpace(dbDir))
            Directory.CreateDirectory(dbDir);
        _connectionString = $"Data Source={dbPath}";
        _init = new Lazy<Task>(() => EnsureSchemaAsync());
    }

    private async Task EnsureInitializedAsync() => await _init.Value.ConfigureAwait(false);

    private async Task EnsureSchemaAsync()
    {
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText =
                """
                CREATE TABLE IF NOT EXISTS keybind_overrides (
                    action_id TEXT PRIMARY KEY NOT NULL,
                    value TEXT NOT NULL
                );
                """;
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error("Keybinds", "Failed to initialize keybind_overrides table.", ex);
            throw;
        }
    }

    public async Task<IReadOnlyDictionary<string, KeybindOverrideDocument>> LoadOverridesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        var dict = new Dictionary<string, KeybindOverrideDocument>(StringComparer.Ordinal);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT action_id, value FROM keybind_overrides";
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var id = reader.GetString(0);
            var json = reader.GetString(1);
            try
            {
                var doc = JsonSerializer.Deserialize<KeybindOverrideDocument>(json, JsonOptions);
                if (doc != null)
                    dict[id] = doc;
            }
            catch (Exception ex)
            {
                _logger.Warning("Keybinds", $"Invalid keybind override JSON for '{id}': {ex.Message}");
            }
        }

        return dict;
    }

    public async Task SaveOverrideAsync(string actionId, KeybindOverrideDocument document, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        var json = JsonSerializer.Serialize(document, JsonOptions);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO keybind_overrides (action_id, value) VALUES ($id, $val)";
        cmd.Parameters.AddWithValue("$id", actionId);
        cmd.Parameters.AddWithValue("$val", json);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteOverrideAsync(string actionId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM keybind_overrides WHERE action_id = $id";
        cmd.Parameters.AddWithValue("$id", actionId);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ClearAllOverridesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM keybind_overrides";
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
