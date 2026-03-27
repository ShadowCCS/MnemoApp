using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services;

/// <summary>
/// Appends one JSON object per line under <c>%LocalAppData%\mnemo\chat_dataset\conversations.jsonl</c>.
/// </summary>
public sealed class ChatDatasetLogger : IChatDatasetLogger
{
    private readonly ISettingsService _settings;
    private readonly ILoggerService _logger;
    private readonly ConcurrentDictionary<string, Staging> _staging = new();

    private sealed class Staging
    {
        public ChatDatasetManagerSection? Manager { get; set; }
        public ChatDatasetChatSection? Chat { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public ChatDatasetLogger(ISettingsService settings, ILoggerService logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public Task StageManagerAsync(string turnId, ChatDatasetManagerSection manager, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(turnId)) return Task.CompletedTask;
        _staging.AddOrUpdate(
            turnId,
            _ => new Staging { Manager = manager },
            (_, s) =>
            {
                s.Manager = manager;
                return s;
            });
        return Task.CompletedTask;
    }

    public Task StageChatAsync(string turnId, ChatDatasetChatSection chat, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(turnId)) return Task.CompletedTask;
        _staging.AddOrUpdate(
            turnId,
            _ => new Staging { Chat = chat },
            (_, s) =>
            {
                s.Chat = chat;
                return s;
            });
        return Task.CompletedTask;
    }

    public async Task CommitTurnAsync(ChatDatasetCommitRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(request.TurnId)) return;
        if (!await _settings.GetAsync(ChatDatasetSettings.LoggingEnabledKey, false).ConfigureAwait(false))
        {
            ClearTurn(request.TurnId);
            return;
        }

        if (!_staging.TryRemove(request.TurnId, out var staged))
            staged = new Staging();

        var record = new ChatDatasetTurnRecord
        {
            SchemaVersion = 5,
            RecordedAtUtc = DateTime.UtcNow.ToString("O"),
            TurnId = request.TurnId,
            ConversationId = request.ConversationId,
            TurnIndex = request.TurnIndex,
            Source = request.Source,
            AssistantMode = request.AssistantMode,
            LatestUserMessage = request.LatestUserMessage,
            ConversationContext = request.ConversationContext,
            ComposedSystemPrompt = request.ComposedSystemPrompt,
            FinalAssistantResponse = request.FinalAssistantResponse,
            Manager = staged.Manager,
            Chat = staged.Chat,
            Outcome = request.Outcome
        };

        try
        {
            Directory.CreateDirectory(ChatDatasetSettings.LogDirectory);
            var line = JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine;
            await File.AppendAllTextAsync(ChatDatasetSettings.LogFilePath, line, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error("ChatDataset", $"Failed to write dataset log: {ex}");
        }
    }

    public void ClearTurn(string turnId)
    {
        if (string.IsNullOrEmpty(turnId)) return;
        _staging.TryRemove(turnId, out _);
    }
}
