using System;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Services;

namespace Mnemo.UI.Services;

/// <summary>
/// After a typing pause, runs orchestration routing and warms the predicted model. Uses the same draft text
/// the user will send so <see cref="IAIOrchestrator.PrefetchRoutingAndWarmupAsync"/> can cache the routing result.
/// </summary>
public sealed class ChatTypingPrefetchHelper
{
    private readonly IAIOrchestrator _orchestrator;
    private readonly ChatPauseToSendEstimator _pauseEstimator;
    private readonly ILoggerService _logger;
    private readonly Func<string> _getInputText;

    private CancellationTokenSource? _idleCts;
    private DateTime _lastKeystrokeUtc = DateTime.UtcNow;

    public ChatTypingPrefetchHelper(
        IAIOrchestrator orchestrator,
        ChatPauseToSendEstimator pauseEstimator,
        ILoggerService logger,
        Func<string> getInputText)
    {
        _orchestrator = orchestrator;
        _pauseEstimator = pauseEstimator;
        _logger = logger;
        _getInputText = getInputText;
    }

    /// <summary>Call when chat input text changes.</summary>
    public void NotifyInputChanged(bool isBusy, bool isRecording)
    {
        _lastKeystrokeUtc = DateTime.UtcNow;
        _idleCts?.Cancel();
        _idleCts?.Dispose();
        _idleCts = null;

        var text = _getInputText();
        if (isBusy || isRecording || string.IsNullOrWhiteSpace(text))
            return;

        _idleCts = new CancellationTokenSource();
        var token = _idleCts.Token;
        _ = RunIdlePrefetchAfterDelayAsync(token);
    }

    /// <summary>Call when the user commits send; records pause-to-send for adaptive idle delay.</summary>
    public async Task RecordSendPauseAsync(CancellationToken ct = default)
    {
        var pauseMs = (DateTime.UtcNow - _lastKeystrokeUtc).TotalMilliseconds;
        await _pauseEstimator.RecordPauseToSendSampleAsync(pauseMs, ct).ConfigureAwait(false);
    }

    private async Task RunIdlePrefetchAfterDelayAsync(CancellationToken idleToken)
    {
        try
        {
            var delayMs = await _pauseEstimator.GetIdleTriggerDelayMsAsync(idleToken).ConfigureAwait(false);
            await Task.Delay(delayMs, idleToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var current = _getInputText();
        if (string.IsNullOrWhiteSpace(current))
            return;

        var draft = current.TrimEnd();
        if (draft.Length == 0)
            return;

        try
        {
            await _orchestrator.PrefetchRoutingAndWarmupAsync(draft, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Debug("ChatPrefetch", $"Prefetch failed: {ex.Message}");
        }
    }
}
