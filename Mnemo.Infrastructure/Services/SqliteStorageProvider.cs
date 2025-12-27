using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services;

public class SqliteStorageProvider : IStorageProvider
{
    private readonly string _connectionString;
    private readonly ILoggerService _logger;

    public SqliteStorageProvider(ILoggerService logger)
    {
        _logger = logger;
        var dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mnemo.db");
        _connectionString = $"Data Source={dbPath}";
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        
        // Storage table
        command.CommandText = "CREATE TABLE IF NOT EXISTS Storage (Key TEXT PRIMARY KEY, Value TEXT)";
        command.ExecuteNonQuery();

        // Versioning table
        command.CommandText = "CREATE TABLE IF NOT EXISTS DbVersion (Version INTEGER PRIMARY KEY, AppliedAt TEXT)";
        command.ExecuteNonQuery();

        // Check current version
        command.CommandText = "SELECT COUNT(*) FROM DbVersion";
        var count = Convert.ToInt32(command.ExecuteScalar());
        if (count == 0)
        {
            command.CommandText = "INSERT INTO DbVersion (Version, AppliedAt) VALUES (1, $date)";
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$date", DateTime.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }
    }

    public async Task<Result> SaveAsync<T>(string key, T data)
    {
        try
        {
            var json = JsonSerializer.Serialize(data);
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            var command = connection.CreateCommand();
            command.CommandText = "INSERT OR REPLACE INTO Storage (Key, Value) VALUES ($key, $value)";
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", json);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.Error("Storage", $"Failed to save data for key: {key}", ex);
            return Result.Failure($"Failed to save data for key: {key}", ex);
        }
    }

    public async Task<Result<T?>> LoadAsync<T>(string key)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            var command = connection.CreateCommand();
            command.CommandText = "SELECT Value FROM Storage WHERE Key = $key";
            command.Parameters.AddWithValue("$key", key);
            var result = await command.ExecuteScalarAsync().ConfigureAwait(false);
            if (result is string json)
            {
                var data = JsonSerializer.Deserialize<T>(json);
                return Result<T?>.Success(data);
            }
            return Result<T?>.Success(default);
        }
        catch (Exception ex)
        {
            _logger.Error("Storage", $"Failed to load data for key: {key}", ex);
            return Result<T?>.Failure($"Failed to load data for key: {key}", ex);
        }
    }

    public async Task<Result> DeleteAsync(string key)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Storage WHERE Key = $key";
            command.Parameters.AddWithValue("$key", key);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.Error("Storage", $"Failed to delete data for key: {key}", ex);
            return Result.Failure($"Failed to delete data for key: {key}", ex);
        }
    }
}

