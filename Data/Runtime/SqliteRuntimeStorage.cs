using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using Microsoft.Data.Sqlite;
using System.Threading.Tasks;

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

        public async Task SetPropertyAsync<T>(string key, T value)
        {
            await Task.Run(() => SetProperty(key, value));
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

        public async Task RemovePropertyAsync(string key)
        {
            await Task.Run(() => RemoveProperty(key));
        }

        public void AddProperty<T>(string key, T value)
        {
            if (HasProperty(key))
                throw new InvalidOperationException($"Key already exists: {key}");
            SetProperty(key, value);
        }

        private static readonly System.Text.Json.JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        private static (string typeName, string json) Serialize<T>(T value)
        {
            var type = typeof(T);
            if (value == null)
            {
                return (type.FullName ?? type.Name, "null");
            }

            // Simple primitives stored as JSON string to keep it uniform
            var json = System.Text.Json.JsonSerializer.Serialize(value, _jsonOptions);
            return (type.FullName ?? type.Name, json);
        }

        private static T? Deserialize<T>(string json)
        {
            if (string.Equals(json, "null", StringComparison.Ordinal))
            {
                return default;
            }
            return System.Text.Json.JsonSerializer.Deserialize<T>(json, _jsonOptions);
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

        private MnemoApp.Modules.Notes.Models.NoteData? TryRepairNoteData(string json)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;
                
                var note = new MnemoApp.Modules.Notes.Models.NoteData();
                
                if (root.TryGetProperty("Id", out var idProp) && idProp.ValueKind == System.Text.Json.JsonValueKind.String)
                    note.Id = idProp.GetString() ?? Guid.NewGuid().ToString();
                else
                    note.Id = Guid.NewGuid().ToString();
                
                if (root.TryGetProperty("Title", out var titleProp) && titleProp.ValueKind == System.Text.Json.JsonValueKind.String)
                    note.Title = titleProp.GetString() ?? "Untitled";
                else
                    note.Title = "Untitled";
                
                if (root.TryGetProperty("Blocks", out var blocksProp) && blocksProp.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    try
                    {
                        note.Blocks = System.Text.Json.JsonSerializer.Deserialize<MnemoApp.Modules.Notes.Models.Block[]>(blocksProp.GetRawText(), _jsonOptions) 
                            ?? Array.Empty<MnemoApp.Modules.Notes.Models.Block>();
                    }
                    catch
                    {
                        note.Blocks = Array.Empty<MnemoApp.Modules.Notes.Models.Block>();
                    }
                }
                else
                {
                    note.Blocks = Array.Empty<MnemoApp.Modules.Notes.Models.Block>();
                }
                
                if (root.TryGetProperty("CreatedAt", out var createdAtProp))
                {
                    try
                    {
                        if (createdAtProp.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            if (DateTime.TryParse(createdAtProp.GetString(), out var createdAt))
                                note.CreatedAt = createdAt;
                        }
                        else if (createdAtProp.ValueKind == System.Text.Json.JsonValueKind.Number)
                        {
                            // Handle Unix timestamp
                            if (createdAtProp.TryGetInt64(out var unixTime))
                                note.CreatedAt = DateTimeOffset.FromUnixTimeSeconds(unixTime).DateTime;
                        }
                        else
                        {
                            try { note.CreatedAt = createdAtProp.GetDateTime(); }
                            catch
                            {
                                var dtStr = createdAtProp.GetRawText().Trim('"');
                                if (DateTime.TryParse(dtStr, out var dt))
                                    note.CreatedAt = dt;
                            }
                        }
                    }
                    catch { }
                }
                
                if (root.TryGetProperty("UpdatedAt", out var updatedAtProp))
                {
                    try
                    {
                        if (updatedAtProp.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            if (DateTime.TryParse(updatedAtProp.GetString(), out var updatedAt))
                                note.UpdatedAt = updatedAt;
                        }
                        else if (updatedAtProp.ValueKind == System.Text.Json.JsonValueKind.Number)
                        {
                            // Handle Unix timestamp
                            if (updatedAtProp.TryGetInt64(out var unixTime))
                                note.UpdatedAt = DateTimeOffset.FromUnixTimeSeconds(unixTime).DateTime;
                        }
                        else
                        {
                            try { note.UpdatedAt = updatedAtProp.GetDateTime(); }
                            catch
                            {
                                var dtStr = updatedAtProp.GetRawText().Trim('"');
                                if (DateTime.TryParse(dtStr, out var dt))
                                    note.UpdatedAt = dt;
                            }
                        }
                    }
                    catch { }
                }
                
                if (root.TryGetProperty("Tags", out var tagsProp) && tagsProp.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    try
                    {
                        note.Tags = System.Text.Json.JsonSerializer.Deserialize<string[]>(tagsProp.GetRawText(), _jsonOptions) 
                            ?? Array.Empty<string>();
                    }
                    catch
                    {
                        note.Tags = Array.Empty<string>();
                    }
                }
                else
                {
                    note.Tags = Array.Empty<string>();
                }
                
                return note;
            }
            catch
            {
                return null;
            }
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
                    
                    T? data = default(T);
                    bool wasRepaired = false;
                    try
                    {
                        data = Deserialize<T>(dataJson);
                    }
                    catch (System.Text.Json.JsonException)
                    {
                        // Try to repair corrupted entries for Notes and Folders
                        if (typeof(T) == typeof(MnemoApp.Modules.Notes.Models.NoteData))
                        {
                            var repaired = TryRepairNoteData(dataJson);
                            if (repaired != null)
                            {
                                data = (T)(object)repaired;
                                wasRepaired = true;
                            }
                        }
                    }
                    
                    if (data != null)
                    {
                        results.Add(new ContentItem<T>
                        {
                            ContentId = contentId,
                            Data = data,
                            CreatedAt = createdAt,
                            UpdatedAt = updatedAt
                        });
                        
                        // Save repaired entry back to database
                        if (wasRepaired)
                        {
                            try
                            {
                                string? repairContentType = typeof(T) == typeof(MnemoApp.Modules.Notes.Models.NoteData) ? "Notes" : null;
                                if (!string.IsNullOrEmpty(repairContentType))
                                {
                                    SetContentProperty($"Content/{repairContentType}/{contentId}", data);
                                    System.Diagnostics.Debug.WriteLine($"Repaired and saved content entry {contentId} ({repairContentType})");
                                }
                            }
                            catch (Exception repairEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"Failed to save repaired entry {contentId}: {repairEx.Message}");
                            }
                        }
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


