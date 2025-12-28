using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.Knowledge;

public class SqliteVectorStore : IVectorStore
{
    private readonly string _connectionString;
    private readonly ILoggerService _logger;

    public SqliteVectorStore(ILoggerService logger)
    {
        _logger = logger;
        var dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "knowledge.db");
        _connectionString = $"Data Source={dbPath}";
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS KnowledgeChunks (
                Id TEXT PRIMARY KEY,
                Content TEXT,
                SourceId TEXT,
                Metadata TEXT,
                Embedding BLOB
            );
            CREATE INDEX IF NOT EXISTS idx_source ON KnowledgeChunks(SourceId);
        ";
        command.ExecuteNonQuery();
    }

    public async Task SaveChunksAsync(IEnumerable<KnowledgeChunk> chunks)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();

        try
        {
            foreach (var chunk in chunks)
            {
                var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
                    INSERT OR REPLACE INTO KnowledgeChunks (Id, Content, SourceId, Metadata, Embedding)
                    VALUES ($id, $content, $sourceId, $metadata, $embedding)
                ";
                command.Parameters.AddWithValue("$id", chunk.Id);
                command.Parameters.AddWithValue("$content", chunk.Content);
                command.Parameters.AddWithValue("$sourceId", chunk.SourceId);
                command.Parameters.AddWithValue("$metadata", JsonSerializer.Serialize(chunk.Metadata));
                
                var embeddingBytes = new byte[chunk.Embedding!.Length * sizeof(float)];
                Buffer.BlockCopy(chunk.Embedding, 0, embeddingBytes, 0, embeddingBytes.Length);
                command.Parameters.AddWithValue("$embedding", embeddingBytes);

                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
            await transaction.CommitAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync().ConfigureAwait(false);
            _logger.Error("VectorStore", "Failed to save chunks", ex);
            throw;
        }
    }

    public async Task<IEnumerable<KnowledgeChunk>> SearchAsync(float[] queryVector, int limit = 5)
    {
        var allChunks = new List<KnowledgeChunk>();

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Content, SourceId, Metadata, Embedding FROM KnowledgeChunks";

        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            var embeddingBytes = (byte[])reader["Embedding"];
            var embedding = new float[embeddingBytes.Length / sizeof(float)];
            Buffer.BlockCopy(embeddingBytes, 0, embedding, 0, embeddingBytes.Length);

            var score = CosineSimilarity(queryVector, embedding);
            
            allChunks.Add(new KnowledgeChunk
            {
                Id = reader.GetString(0),
                Content = reader.GetString(1),
                SourceId = reader.GetString(2),
                Metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(reader.GetString(3)) ?? new(),
                Embedding = embedding,
                RelevanceScore = score
            });
        }

        return allChunks
            .OrderByDescending(c => c.RelevanceScore)
            .Take(limit);
    }

    public async Task DeleteBySourceAsync(string sourceId)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM KnowledgeChunks WHERE SourceId = $sourceId";
        command.Parameters.AddWithValue("$sourceId", sourceId);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private float CosineSimilarity(float[] v1, float[] v2)
    {
        float dot = 0.0f;
        float mag1 = 0.0f;
        float mag2 = 0.0f;
        for (int i = 0; i < v1.Length; i++)
        {
            dot += v1[i] * v2[i];
            mag1 += v1[i] * v1[i];
            mag2 += v2[i] * v2[i];
        }
        return dot / (MathF.Sqrt(mag1) * MathF.Sqrt(mag2));
    }
}
