using System;
using System.Collections.Generic;
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
        private readonly object _schemaLock = new object();
        private bool _schemaInitialized;

        public SqliteRuntimeStorage(string baseDirectory)
        {
            Directory.CreateDirectory(baseDirectory);
            _dbPath = Path.Combine(baseDirectory, "runtime.db");
            EnsureSchema();
        }

        private SqliteConnection CreateConnection()
        {
            var conn = new SqliteConnection($"Data Source={_dbPath};Cache=Shared");
            conn.Open();
            
            // Configure pragmas for better concurrency and performance
            using var pragma = conn.CreateCommand();
            pragma.CommandText = @"
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=NORMAL;
                PRAGMA foreign_keys=ON;
                PRAGMA busy_timeout=5000;
            ";
            pragma.ExecuteNonQuery();
            
            return conn;
        }

        private void EnsureSchema()
        {
            lock (_schemaLock)
            {
                if (_schemaInitialized) return;
                
                using var conn = CreateConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Settings (
                        key TEXT PRIMARY KEY,
                        type TEXT NOT NULL,
                        value TEXT NULL
                    );
                    CREATE TABLE IF NOT EXISTS Content (
                        content_type TEXT NOT NULL,
                        content_id TEXT NOT NULL,
                        metadata TEXT NULL,
                        data TEXT NULL,
                        created_at TEXT NOT NULL,
                        updated_at TEXT NOT NULL,
                        PRIMARY KEY (content_type, content_id)
                    );
                    CREATE INDEX IF NOT EXISTS idx_content_type ON Content(content_type);
                    CREATE INDEX IF NOT EXISTS idx_content_created ON Content(created_at);
                    CREATE INDEX IF NOT EXISTS idx_content_updated ON Content(updated_at);
                ";
                cmd.ExecuteNonQuery();
                
                _schemaInitialized = true;
            }
        }

        private static (string table, string pureKey) ResolveTableAndKey(string key)
        {
            // All content goes to Content table with type-based routing
            if (key.StartsWith("Content/", StringComparison.Ordinal))
            {
                return ("Content", key);
            }
            // Legacy support for old key patterns
            if (key.StartsWith("Tables/Content/", StringComparison.Ordinal))
            {
                return ("Content", key);
            }
            return ("Settings", key);
        }

        public T? GetProperty<T>(string key)
        {
            var (table, pureKey) = ResolveTableAndKey(key);
            
            if (table == "Content")
            {
                return GetContentProperty<T>(pureKey);
            }
            
            using var conn = CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT value FROM {table} WHERE key = @key LIMIT 1";
            cmd.Parameters.AddWithValue("@key", pureKey);
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
            var (table, pureKey) = ResolveTableAndKey(key);
            
            if (table == "Content")
            {
                SetContentProperty(pureKey, value);
                return;
            }
            
            var (typeName, serialized) = Serialize(value);
            using var conn = CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                INSERT INTO {table}(key, type, value) VALUES(@key, @type, @value)
                ON CONFLICT(key) DO UPDATE SET type = excluded.type, value = excluded.value;
            ";
            cmd.Parameters.AddWithValue("@key", pureKey);
            cmd.Parameters.AddWithValue("@type", typeName);
            cmd.Parameters.AddWithValue("@value", serialized);
            cmd.ExecuteNonQuery();
        }

        public bool HasProperty(string key)
        {
            var (table, pureKey) = ResolveTableAndKey(key);
            
            if (table == "Content")
            {
                return HasContentProperty(pureKey);
            }
            
            using var conn = CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT 1 FROM {table} WHERE key = @key LIMIT 1";
            cmd.Parameters.AddWithValue("@key", pureKey);
            using var reader = cmd.ExecuteReader(CommandBehavior.SingleRow);
            return reader.Read();
        }

        public void RemoveProperty(string key)
        {
            var (table, pureKey) = ResolveTableAndKey(key);
            
            if (table == "Content")
            {
                RemoveContentProperty(pureKey);
                return;
            }
            
            using var conn = CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"DELETE FROM {table} WHERE key = @key";
            cmd.Parameters.AddWithValue("@key", pureKey);
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

        // Content table specific methods
        private T? GetContentProperty<T>(string key)
        {
            var (contentType, contentId) = ParseContentKey(key);
            using var conn = CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT data FROM Content WHERE content_type = @type AND content_id = @id LIMIT 1";
            cmd.Parameters.AddWithValue("@type", contentType);
            cmd.Parameters.AddWithValue("@id", contentId);
            var result = cmd.ExecuteScalar();
            if (result == null || result is DBNull)
            {
                return default;
            }
            var str = Convert.ToString(result);
            return str == null ? default : Deserialize<T>(str);
        }

        private void SetContentProperty<T>(string key, T value)
        {
            var (contentType, contentId) = ParseContentKey(key);
            var (typeName, serialized) = Serialize(value);
            var now = DateTime.UtcNow.ToString("O");
            
            using var conn = CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Content(content_type, content_id, metadata, data, created_at, updated_at) 
                VALUES(@type, @id, @meta, @data, @created, @updated)
                ON CONFLICT(content_type, content_id) DO UPDATE SET 
                    metadata = excluded.metadata,
                    data = excluded.data,
                    updated_at = excluded.updated_at;
            ";
            cmd.Parameters.AddWithValue("@type", contentType);
            cmd.Parameters.AddWithValue("@id", contentId);
            cmd.Parameters.AddWithValue("@meta", typeName);
            cmd.Parameters.AddWithValue("@data", serialized);
            cmd.Parameters.AddWithValue("@created", now);
            cmd.Parameters.AddWithValue("@updated", now);
            cmd.ExecuteNonQuery();
        }

        private bool HasContentProperty(string key)
        {
            var (contentType, contentId) = ParseContentKey(key);
            using var conn = CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM Content WHERE content_type = @type AND content_id = @id LIMIT 1";
            cmd.Parameters.AddWithValue("@type", contentType);
            cmd.Parameters.AddWithValue("@id", contentId);
            using var reader = cmd.ExecuteReader(CommandBehavior.SingleRow);
            return reader.Read();
        }

        private void RemoveContentProperty(string key)
        {
            var (contentType, contentId) = ParseContentKey(key);
            using var conn = CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Content WHERE content_type = @type AND content_id = @id";
            cmd.Parameters.AddWithValue("@type", contentType);
            cmd.Parameters.AddWithValue("@id", contentId);
            cmd.ExecuteNonQuery();
        }

        private static (string contentType, string contentId) ParseContentKey(string key)
        {
            // Parse keys like "Content/Paths/{id}" or "Content/Flashcards/{id}"
            var parts = key.Split('/', 3);
            if (parts.Length >= 3 && parts[0] == "Content")
            {
                return (parts[1], parts[2]);
            }
            // Legacy support for old hierarchical keys
            if (key.StartsWith("Tables/Content/"))
            {
                var legacyParts = key.Substring("Tables/Content/".Length).Split('/', 2);
                return (legacyParts[0], legacyParts.Length > 1 ? legacyParts[1] : "");
            }
            throw new ArgumentException($"Invalid content key format: {key}");
        }

        public IEnumerable<ContentItem<T>> ListContent<T>(string contentType)
        {
            var results = new List<ContentItem<T>>();
            
            using var conn = CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT content_id, data, created_at, updated_at 
                FROM Content 
                WHERE content_type = @type AND content_id != 'list'
                ORDER BY updated_at DESC
            ";
            cmd.Parameters.AddWithValue("@type", contentType);
            
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string? contentId = null;
                try
                {
                    contentId = reader.GetString(0);
                    var dataJson = reader.IsDBNull(1) ? null : reader.GetString(1);
                    
                    if (string.IsNullOrEmpty(dataJson))
                        continue;
                    
                    var createdAtStr = reader.IsDBNull(2) ? DateTime.UtcNow.ToString("O") : reader.GetString(2);
                    var updatedAtStr = reader.IsDBNull(3) ? DateTime.UtcNow.ToString("O") : reader.GetString(3);
                    
                    DateTime createdAt;
                    DateTime updatedAt;
                    
                    try
                    {
                        createdAt = DateTime.Parse(createdAtStr);
                    }
                    catch
                    {
                        createdAt = DateTime.UtcNow;
                    }
                    
                    try
                    {
                        updatedAt = DateTime.Parse(updatedAtStr);
                    }
                    catch
                    {
                        updatedAt = DateTime.UtcNow;
                    }
                    
                    var data = Deserialize<T>(dataJson);
                    if (data != null)
                    {
                        results.Add(new ContentItem<T>
                        {
                            ContentId = contentId,
                            Data = data,
                            CreatedAt = createdAt,
                            UpdatedAt = updatedAt
                        });
                    }
                }
                catch (System.Text.Json.JsonException ex)
                {
                    // Skip entries that can't be deserialized (likely wrong type or corrupted data)
                    System.Diagnostics.Debug.WriteLine($"Skipping invalid content entry {contentId ?? "unknown"}: {ex.Message}");
                    continue;
                }
                catch (Exception ex)
                {
                    // Skip entries with other errors
                    System.Diagnostics.Debug.WriteLine($"Error processing content entry {contentId ?? "unknown"}: {ex.Message}");
                    continue;
                }
            }
            
            return results;
        }

        public void Dispose()
        {
            // No persistent connection to dispose; connections are per-operation
        }
    }
}


