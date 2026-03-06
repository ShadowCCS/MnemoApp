using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using Pv;
using Whisper.net;

namespace Mnemo.Infrastructure.Services.Speech;

/// <summary>
/// Cross-platform speech-to-text implementation using PvRecorder for microphone capture
/// and Whisper.net with ggml-tiny for transcription.
/// </summary>
public class WhisperSpeechRecognitionService : ISpeechRecognitionService
{
    private const string SettingsKeyModelPath = "Speech.WhisperModelPath";
    private const string SettingsKeyMaxDuration = "Speech.MaxRecordingDurationSeconds";
    private const int DefaultMaxDurationSeconds = 300;
    private const int PvRecorderFrameLength = 512;
    private const int SampleRate = 16000;
    private const int Channels = 1;
    private const int BitsPerSample = 16;

    private readonly ISettingsService _settings;
    private readonly ILoggerService _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly object _framesLock = new();
    private readonly object _factoryLock = new();

    private WhisperFactory? _whisperFactory;
    private PvRecorder? _recorder;
    private List<short[]>? _recordedFrames;
    private Task? _recorderTask;
    private CancellationTokenSource? _recordingCts;
    private bool _isRecording;
    private bool _disposed;
    private Action<string>? _partialCallback;
    private volatile bool _isTranscribingPartial;
    private DateTime _lastPartialTranscribeTime;
    private DateTime _recordingStartTime;
    private int _maxDurationSeconds;
    private string? _cachedModelPath;

    public bool IsRecording => !_disposed && _isRecording;

    public WhisperSpeechRecognitionService(ISettingsService settings, ILoggerService logger)
    {
        _settings = settings;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartRecordingAsync(CancellationToken ct = default, Action<string>? onPartialTranscript = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_isRecording)
            {
                _logger.Warning("WhisperSpeechRecognition", "StartRecordingAsync called while already recording.");
                return;
            }

            _cachedModelPath = await GetModelPathAsync().ConfigureAwait(false);
            if (string.IsNullOrEmpty(_cachedModelPath) || !File.Exists(_cachedModelPath))
            {
                _logger.Error("WhisperSpeechRecognition", $"Whisper model not found at: {_cachedModelPath ?? "(null)"}");
                throw new InvalidOperationException("Speech model not found. Please configure Speech.WhisperModelPath or install ggml-tiny.bin.");
            }

            _maxDurationSeconds = await _settings.GetAsync(SettingsKeyMaxDuration, DefaultMaxDurationSeconds).ConfigureAwait(false);
            _recordedFrames = new List<short[]>();
            _recorder = PvRecorder.Create(PvRecorderFrameLength, -1);
            _recorder.Start();
            _isRecording = true;
            _recordingCts = new CancellationTokenSource();
            _partialCallback = onPartialTranscript;
            _isTranscribingPartial = false;
            _lastPartialTranscribeTime = DateTime.UtcNow;
            _recordingStartTime = DateTime.UtcNow;

            _recorderTask = Task.Run(() => RecordLoop(_recordingCts.Token), CancellationToken.None);
        }
        catch (Exception ex)
        {
            _isRecording = false;
            _recorder?.Dispose();
            _recorder = null;
            _logger.Error("WhisperSpeechRecognition", "Failed to start recording.", ex);
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<Result<string>> StopAndTranscribeAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_isRecording || _recorder == null || _recordedFrames == null)
            {
                return Result<string>.Failure("Not recording.");
            }

            _recordingCts?.Cancel();
            _recorder.Stop();
            if (_recorderTask != null)
                await _recorderTask.ConfigureAwait(false);

            _recorder.Dispose();
            _recorder = null;
            _isRecording = false;
            _recordingCts?.Dispose();
            _recordingCts = null;
            _recorderTask = null;
            _partialCallback = null;

            List<short[]> frames;
            lock (_framesLock)
            {
                frames = new List<short[]>(_recordedFrames);
                _recordedFrames = null;
            }

            if (frames.Count == 0)
            {
                return Result<string>.Failure("No audio captured.");
            }

            using var wavStream = BuildWavStream(frames);
            var text = await TranscribeAsync(_cachedModelPath!, wavStream, ct).ConfigureAwait(false);
            return Result<string>.Success(text);
        }
        catch (Exception ex)
        {
            _logger.Error("WhisperSpeechRecognition", "StopAndTranscribeAsync failed.", ex);
            if (_recorder != null)
            {
                _recorder.Dispose();
                _recorder = null;
            }
            _isRecording = false;
            _recordingCts?.Dispose();
            _recordingCts = null;
            _recorderTask = null;
            lock (_framesLock) { _recordedFrames = null; }
            return Result<string>.Failure(ex.Message, ex);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task CancelRecordingAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_isRecording)
                return;

            _recordingCts?.Cancel();
            _recorder?.Stop();
            if (_recorderTask != null)
                await _recorderTask.ConfigureAwait(false);

            _recorder?.Dispose();
            _recorder = null;
            _isRecording = false;
            _recordingCts?.Dispose();
            _recordingCts = null;
            _recorderTask = null;
            _partialCallback = null;
            lock (_framesLock)
            {
                _recordedFrames = null;
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        await CancelRecordingAsync().ConfigureAwait(false);
        _whisperFactory?.Dispose();
        _whisperFactory = null;
        _semaphore.Dispose();
        _disposed = true;
    }

    private void RecordLoop(CancellationToken ct)
    {
        if (_recorder == null || _recordedFrames == null)
            return;

        try
        {
            while (_recorder.IsRecording && !ct.IsCancellationRequested)
            {
                if ((DateTime.UtcNow - _recordingStartTime).TotalSeconds >= _maxDurationSeconds)
                {
                    _logger.Warning("WhisperSpeechRecognition", $"Max recording duration ({_maxDurationSeconds}s) reached.");
                    break;
                }

                try
                {
                    var frame = _recorder.Read();
                    lock (_framesLock)
                    {
                        if (_recordedFrames != null)
                        {
                            var copy = new short[frame.Length];
                            Array.Copy(frame, copy, frame.Length);
                            _recordedFrames.Add(copy);
                        }
                    }

                    // Partial transcription logic
                    if (_partialCallback != null && 
                        !_isTranscribingPartial && 
                        (DateTime.UtcNow - _lastPartialTranscribeTime).TotalMilliseconds > 300)
                    {
                        _isTranscribingPartial = true;
                        _lastPartialTranscribeTime = DateTime.UtcNow;
                        
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                List<short[]> snapshot;
                                lock (_framesLock)
                                {
                                    if (_recordedFrames == null) return;
                                    snapshot = new List<short[]>(_recordedFrames);
                                }
                                
                                if (snapshot.Count > 0 && !string.IsNullOrEmpty(_cachedModelPath) && File.Exists(_cachedModelPath))
                                {
                                    using var ms = BuildWavStream(snapshot);
                                    var text = await TranscribeAsync(_cachedModelPath, ms, CancellationToken.None).ConfigureAwait(false);
                                    var sanitized = SanitizeTranscript(text);
                                    if (!string.IsNullOrWhiteSpace(sanitized))
                                        _partialCallback?.Invoke(sanitized);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.Debug("WhisperSpeechRecognition", $"Partial transcription failed: {ex.Message}");
                            }
                            finally
                            {
                                _isTranscribingPartial = false;
                            }
                        });
                    }
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Debug("WhisperSpeechRecognition", $"Record loop: {ex.Message}");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Debug("WhisperSpeechRecognition", $"Record loop exited: {ex.Message}");
        }
    }

    private async Task<string> GetModelPathAsync()
    {
        var custom = await _settings.GetAsync<string>(SettingsKeyModelPath, null!).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(custom))
            return custom!;

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "mnemo", "models", "audio", "STT", "ggml-tiny.bin");
    }

    private static MemoryStream BuildWavStream(List<short[]> frames)
    {
        var totalSamples = frames.Sum(f => f.Length);
        var dataBytes = totalSamples * sizeof(short);
        const int headerSize = 44;
        var stream = new MemoryStream(headerSize + dataBytes);

        // WAV header for 16-bit mono 16 kHz
        var byteRate = SampleRate * Channels * (BitsPerSample / 8);
        var blockAlign = (short)(Channels * (BitsPerSample / 8));
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataBytes);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1); // PCM
            writer.Write((short)Channels);
            writer.Write(SampleRate);
            writer.Write(byteRate);
            writer.Write(blockAlign);
            writer.Write((short)BitsPerSample);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(dataBytes);
        }

        stream.Position = headerSize;
        foreach (var frame in frames)
        {
            var bytes = new byte[frame.Length * sizeof(short)];
            Buffer.BlockCopy(frame, 0, bytes, 0, bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
        }
        stream.Position = 0;
        return stream;
    }

    private async Task<string> TranscribeAsync(string modelPath, Stream wavStream, CancellationToken ct)
    {
        if (_whisperFactory == null)
        {
            lock (_factoryLock)
            {
                if (_whisperFactory == null)
                {
                    _whisperFactory = WhisperFactory.FromPath(modelPath);
                }
            }
        }

        using var processor = _whisperFactory.CreateBuilder()
            .WithLanguage("auto")
            .Build();

        var segments = new List<string>();
        await foreach (var segment in processor.ProcessAsync(wavStream, ct).ConfigureAwait(false))
        {
            var t = segment.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(t) || IsBlankAudioPlaceholder(t))
                continue;
            segments.Add(t);
        }

        return SanitizeTranscript(string.Join(" ", segments));
    }

    private static bool IsBlankAudioPlaceholder(string text)
    {
        return string.Equals(text, "[Blank_AUDIO]", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "(blank)", StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeTranscript(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        return text
            .Replace("[Blank_AUDIO]", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("(blank)", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();
    }
}
