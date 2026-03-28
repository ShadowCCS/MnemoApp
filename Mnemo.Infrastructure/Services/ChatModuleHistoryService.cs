using System;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services;

public sealed class ChatModuleHistoryService : IChatModuleHistoryService
{
    private const string StorageKey = "chat_module_history";

    private readonly IStorageProvider _storage;
    private readonly ILoggerService _logger;

    public ChatModuleHistoryService(IStorageProvider storage, ILoggerService logger)
    {
        _storage = storage;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<ChatModuleHistoryDocument>> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var load = await _storage.LoadAsync<ChatModuleHistoryDocument>(StorageKey).ConfigureAwait(false);
            ChatModuleHistoryDocument doc;
            if (load.IsSuccess && load.Value != null)
                doc = load.Value;
            else if (!load.IsSuccess && string.Equals(load.ErrorMessage, "Key not found", StringComparison.Ordinal))
                doc = new ChatModuleHistoryDocument();
            else if (!load.IsSuccess)
                return Result<ChatModuleHistoryDocument>.Failure(load.ErrorMessage ?? "Failed to load chat history", load.Exception);
            else
                doc = new ChatModuleHistoryDocument();
            if (doc.Conversations == null)
                doc.Conversations = new System.Collections.Generic.List<ChatModulePersistedConversation>();
            if (doc.Version < 1)
                doc.Version = 1;

            var pruned = PruneExpiredConversations(doc);
            if (pruned > 0)
            {
                var rewrite = await SaveAsync(doc, cancellationToken).ConfigureAwait(false);
                if (!rewrite.IsSuccess)
                    _logger.Error("ChatModuleHistory", $"Retention prune could not be persisted: {rewrite.ErrorMessage}");
            }

            return Result<ChatModuleHistoryDocument>.Success(doc);
        }
        catch (Exception ex)
        {
            _logger.Error("ChatModuleHistory", "Load failed", ex);
            return Result<ChatModuleHistoryDocument>.Failure("Failed to load chat history", ex);
        }
    }

    /// <inheritdoc />
    public async Task<Result> SaveAsync(ChatModuleHistoryDocument document, CancellationToken cancellationToken = default)
    {
        try
        {
            PruneExpiredConversations(document);
            var result = await _storage.SaveAsync(StorageKey, document).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
            _logger.Error("ChatModuleHistory", "Save failed", ex);
            return Result.Failure("Failed to save chat history", ex);
        }
    }

    private static int PruneExpiredConversations(ChatModuleHistoryDocument document)
    {
        var cutoff = DateTime.UtcNow.AddDays(-ChatModuleHistoryRetention.Days);
        var before = document.Conversations.Count;
        document.Conversations.RemoveAll(c => c.LastActivityUtc < cutoff);
        return before - document.Conversations.Count;
    }
}
