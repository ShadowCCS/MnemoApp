using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

/// <summary>
/// Stages manager and chat payloads by <see cref="ChatDatasetLoggingScope"/> turn id, then commits one JSONL line.
/// </summary>
public interface IChatDatasetLogger
{
    Task StageManagerAsync(string turnId, ChatDatasetManagerSection manager, CancellationToken ct = default);
    Task StageChatAsync(string turnId, ChatDatasetChatSection chat, CancellationToken ct = default);
    Task CommitTurnAsync(ChatDatasetCommitRequest request, CancellationToken ct = default);
    void ClearTurn(string turnId);
}
