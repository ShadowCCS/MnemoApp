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
    private bool _isInitialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public SqliteVectorStore(ILoggerService logger)
    {
        _logger = logger;
        var dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "knowledge.db");
        _connectionString = $"Data Source={dbPath}";
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_isInitialized) return;

        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_isInitialized) return;

            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(ct).ConfigureAwait(false);
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
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            _isInitialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task SaveChunksAsync(IEnumerable<KnowledgeChunk> chunks, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();

        try
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
                INSERT OR REPLACE INTO KnowledgeChunks (Id, Content, SourceId, Metadata, Embedding)
                VALUES ($id, $content, $sourceId, $metadata, $embedding)
            ";

            var idParam = command.Parameters.Add("$id", SqliteType.Text);
            var contentParam = command.Parameters.Add("$content", SqliteType.Text);
            var sourceIdParam = command.Parameters.Add("$sourceId", SqliteType.Text);
            var metadataParam = command.Parameters.Add("$metadata", SqliteType.Text);
            var embeddingParam = command.Parameters.Add("$embedding", SqliteType.Blob);

            foreach (var chunk in chunks)
            {
                if (ct.IsCancellationRequested) break;

                idParam.Value = chunk.Id;
                contentParam.Value = chunk.Content;
                sourceIdParam.Value = chunk.SourceId;
                metadataParam.Value = JsonSerializer.Serialize(chunk.Metadata);
                
                var embeddingBytes = new byte[chunk.Embedding!.Length * sizeof(float)];
                Buffer.BlockCopy(chunk.Embedding, 0, embeddingBytes, 0, embeddingBytes.Length);
                embeddingParam.Value = embeddingBytes;

                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            _logger.Error("VectorStore", "Failed to save chunks", ex);
            throw;
        }
    }

    public async Task<IEnumerable<KnowledgeChunk>> SearchAsync(float[] queryVector, int limit = 5, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        var topChunks = new List<KnowledgeChunk>();

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Content, SourceId, Metadata, Embedding FROM KnowledgeChunks";

        using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var embeddingBytes = (byte[])reader["Embedding"];
            var embedding = new float[embeddingBytes.Length / sizeof(float)];
            Buffer.BlockCopy(embeddingBytes, 0, embedding, 0, embeddingBytes.Length);

            // Since vectors are normalized by IEmbeddingService, we can use Dot Product as Cosine Similarity
            var score = DotProduct(queryVector, embedding);
            
            if (topChunks.Count < limit || score > topChunks[^1].RelevanceScore)
            {
                var chunk = new KnowledgeChunk
                {
                    Id = reader.GetString(0),
                    Content = reader.GetString(1),
                    SourceId = reader.GetString(2),
                    Metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(reader.GetString(3)) ?? new(),
                    Embedding = embedding,
                    RelevanceScore = score
                };

                // Maintain a sorted list of top-k chunks
                int index = topChunks.BinarySearch(chunk, RelevanceComparer.Instance);
                if (index < 0) index = ~index;
                topChunks.Insert(index, chunk);

                if (topChunks.Count > limit)
                {
                    topChunks.RemoveAt(topChunks.Count - 1);
                }
            }
        }

        return topChunks;
    }

    public async Task DeleteBySourceAsync(string sourceId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM KnowledgeChunks WHERE SourceId = $sourceId";
        command.Parameters.AddWithValue("$sourceId", sourceId);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private float DotProduct(float[] v1, float[] v2)
    {
        // Safety: use minimum length to avoid index out of bounds
        int len = Math.Min(v1.Length, v2.Length);
        if (len == 0) return 0f;
        
        float dot = 0.0f;
        for (int i = 0; i < len; i++)
        {
            dot += v1[i] * v2[i];
        }
        return dot;
    }

    private class RelevanceComparer : IComparer<KnowledgeChunk>
    {
        public static readonly RelevanceComparer Instance = new();
        public int Compare(KnowledgeChunk? x, KnowledgeChunk? y) => (y?.RelevanceScore ?? 0).CompareTo(x?.RelevanceScore ?? 0);
    }
}
