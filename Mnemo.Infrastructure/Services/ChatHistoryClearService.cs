using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using Mnemo.Infrastructure.Services.AI;

namespace Mnemo.Infrastructure.Services;

public sealed class ChatHistoryClearService : IChatHistoryClearService
{
    private readonly IChatModuleHistoryService _chatHistoryService;
    private readonly IVectorStore _vectorStore;
    private readonly IConversationMemoryStore _memoryStore;
    private readonly IRoutingToolHintStore _routingToolHintStore;
    private readonly ILoggerService _logger;

    public ChatHistoryClearService(
        IChatModuleHistoryService chatHistoryService,
        IVectorStore vectorStore,
        IConversationMemoryStore memoryStore,
        IRoutingToolHintStore routingToolHintStore,
        ILoggerService logger)
    {
        _chatHistoryService = chatHistoryService;
        _vectorStore = vectorStore;
        _memoryStore = memoryStore;
        _routingToolHintStore = routingToolHintStore;
        _logger = logger;
    }

    public event EventHandler? Cleared;

    public async Task<Result> ClearAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var load = await _chatHistoryService.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (!load.IsSuccess || load.Value == null)
                return Result.Failure(load.ErrorMessage ?? "Failed to load chat history", load.Exception);

            var ids = load.Value.Conversations
                .Select(c => c.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            foreach (var id in ids)
            {
                var scope = ConversationMemoryInjector.ConversationMemoryScopeId(id);
                await _vectorStore.DeleteByScopeAsync(scope, cancellationToken).ConfigureAwait(false);
            }

            _memoryStore.EvictAll();
            _routingToolHintStore.ClearAll();

            var empty = new ChatModuleHistoryDocument { Version = 1, Conversations = new() };
            var save = await _chatHistoryService.SaveAsync(empty, cancellationToken).ConfigureAwait(false);
            if (!save.IsSuccess)
                return save;

            Cleared?.Invoke(this, EventArgs.Empty);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.Error("ChatHistoryClear", "Clear all failed", ex);
            return Result.Failure(ex.Message, ex);
        }
    }
}
