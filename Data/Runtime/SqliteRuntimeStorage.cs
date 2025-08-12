using System;
using System.Data;
using System.IO;
using Microsoft.Data.Sqlite;

namespace MnemoApp.Data.Runtime
{
    /// <summary>
    /// SQLite-backed key-value store for runtime settings and app state.
    /// Schema: table Settings(key TEXT PRIMARY KEY, type TEXT, value TEXT)
    /// </summary>
    public class SqliteRuntimeStorage : IRuntimeStorage, IDisposable
    {
        private readonly string _dbPath;
        private readonly SqliteConnection _connection;

        public SqliteRuntimeStorage(string baseDirectory)
        {
            Directory.CreateDirectory(baseDirectory);
            _dbPath = Path.Combine(baseDirectory, "runtime.db");
            _connection = new SqliteConnection($"Data Source={_dbPath}");
            _connection.Open();
            EnsureSchema();
        }

        private void EnsureSchema()
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Settings (
                    key TEXT PRIMARY KEY,
                    type TEXT NOT NULL,
                    value TEXT NULL
                );
            ";
            cmd.ExecuteNonQuery();
        }

        public T? GetProperty<T>(string key)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT value FROM Settings WHERE key = @key LIMIT 1";
            cmd.Parameters.AddWithValue("@key", key);
            var result = cmd.ExecuteScalar();
            if (result == null || result is DBNull)
            {
                return default;
            }

            var str = Convert.ToString(result);
            if (str == null)
            {
                return default;
            }

            return Deserialize<T>(str);
        }

        public void SetProperty<T>(string key, T value)
        {
            var (typeName, serialized) = Serialize(value);
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Settings(key, type, value) VALUES(@key, @type, @value)
                ON CONFLICT(key) DO UPDATE SET type = excluded.type, value = excluded.value;
            ";
            cmd.Parameters.AddWithValue("@key", key);
            cmd.Parameters.AddWithValue("@type", typeName);
            cmd.Parameters.AddWithValue("@value", serialized);
            cmd.ExecuteNonQuery();
        }

        public bool HasProperty(string key)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM Settings WHERE key = @key LIMIT 1";
            cmd.Parameters.AddWithValue("@key", key);
            using var reader = cmd.ExecuteReader(CommandBehavior.SingleRow);
            return reader.Read();
        }

        public void RemoveProperty(string key)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM Settings WHERE key = @key";
            cmd.Parameters.AddWithValue("@key", key);
            cmd.ExecuteNonQuery();
        }

        public void AddProperty<T>(string key, T value)
        {
            if (HasProperty(key))
                throw new InvalidOperationException($"Key already exists: {key}");
            SetProperty(key, value);
        }

        private static (string typeName, string json) Serialize<T>(T value)
        {
            var type = typeof(T);
            if (value == null)
            {
                return (type.FullName ?? type.Name, "null");
            }

            // Simple primitives stored as JSON string to keep it uniform
            var json = System.Text.Json.JsonSerializer.Serialize(value);
            return (type.FullName ?? type.Name, json);
        }

        private static T? Deserialize<T>(string json)
        {
            if (string.Equals(json, "null", StringComparison.Ordinal))
            {
                return default;
            }
            return System.Text.Json.JsonSerializer.Deserialize<T>(json);
        }

        public void Dispose()
        {
            _connection.Dispose();
        }
    }
}


