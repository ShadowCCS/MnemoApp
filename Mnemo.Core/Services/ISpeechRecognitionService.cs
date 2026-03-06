using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

/// <summary>
/// Defines a cross-platform speech-to-text service for recording and transcribing audio.
/// </summary>
public interface ISpeechRecognitionService : IAsyncDisposable
{
    /// <summary>
    /// Starts recording from the default microphone.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="onPartialTranscript">Optional callback for live transcription updates.</param>
    Task StartRecordingAsync(CancellationToken ct = default, Action<string>? onPartialTranscript = null);

    /// <summary>
    /// Stops recording and transcribes the captured audio to text.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Transcribed text on success; failure result with message otherwise.</returns>
    Task<Result<string>> StopAndTranscribeAsync(CancellationToken ct = default);

    /// <summary>
    /// Cancels the current recording without transcribing.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task CancelRecordingAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets whether the service is currently recording.
    /// </summary>
    bool IsRecording { get; }
}
