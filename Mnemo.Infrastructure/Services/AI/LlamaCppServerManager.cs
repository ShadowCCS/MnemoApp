using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.AI;

/// <summary>
/// Manages llama.cpp server processes for local text models: reuses existing servers by a deterministic key,
/// tracks PIDs in a persisted registry for crash recovery, and ensures shutdown on exit (graceful then hard).
/// </summary>
public class LlamaCppServerManager : IAIServerManager
{
    private const int GracefulShutdownWaitSeconds = 5;
    private const int HardKillWaitSeconds = 5;
    private static readonly TimeSpan StartupGracePeriod = TimeSpan.FromSeconds(60);

    private readonly ILoggerService _logger;
    private readonly ISettingsService _settings;
    private readonly HardwareDetector _hardware;
    private readonly IAIModelRegistry _modelRegistry;
    private readonly HttpClient _httpClient;

    private readonly ConcurrentDictionary<string, ServerProcess> _runningServers = new();
    private readonly ConcurrentDictionary<string, string> _modelIdToKey = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastUsed = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly Timer _cleanupTimer;
    private readonly string _registryPath;

    private readonly TimeSpan _fastIdleTimeout = TimeSpan.FromMinutes(15);
    private readonly TimeSpan _smartIdleTimeout = TimeSpan.FromMinutes(7);

    private int _disposed;

    public LlamaCppServerManager(
        ILoggerService logger,
        ISettingsService settings,
        HardwareDetector hardware,
        IAIModelRegistry modelRegistry)
    {
        _logger = logger;
        _settings = settings;
        _hardware = hardware;
        _modelRegistry = modelRegistry;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        _registryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "mnemo",
            "llama-servers.json");

        KillLeftoversOnStartup();

        _cleanupTimer = new Timer(CleanupIdleServers, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// Ensures the specified model's server is running and ready. Reuses an existing process when the same
    /// server key (model path + port + params) is already running and healthy. Use <see cref="BeginRequest"/> when making a request so idle eviction does not kill the server mid-request.
    /// </summary>
    public async Task EnsureRunningAsync(AIModelManifest manifest, CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _disposed, 0, 0) != 0)
        {
            throw new ObjectDisposedException(nameof(LlamaCppServerManager));
        }

        if (string.IsNullOrEmpty(manifest.Endpoint))
        {
            throw new InvalidOperationException($"Model {manifest.DisplayName} has no endpoint configured.");
        }

        var modelFile = ResolveModelPath(manifest);
        var mmprojFile = ResolveMmprojPath(manifest);
        var uri = new Uri(manifest.Endpoint);
        var port = uri.Port;
        var useGpuSetting = await _settings.GetAsync<bool?>("AI.GpuAcceleration").ConfigureAwait(false);
        var hardwareInfo = _hardware.Detect();
        var useGpu = useGpuSetting ?? hardwareInfo.HasNvidiaGpu;
        var serverKey = ComputeServerKey(manifest, modelFile, port, useGpu, mmprojFile);

        _lastUsed[manifest.Id] = DateTime.UtcNow;

        var gate = _locks.GetOrAdd(serverKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_runningServers.TryGetValue(serverKey, out var existing))
            {
                if (!existing.Process.HasExited && await IsHealthyAsync(existing.Manifest.Endpoint!, existing.StartedAt, ct).ConfigureAwait(false))
                {
                    _modelIdToKey[manifest.Id] = serverKey;
                    _logger.Info("LlamaCppServerManager", $"Reusing server for {manifest.DisplayName} on PID {existing.Process.Id}");
                    return;
                }

                RemoveAndDisposeServer(serverKey, existing);
            }

            if (manifest.Role == "smart")
            {
                var fastModel = (await _modelRegistry.GetAvailableModelsAsync().ConfigureAwait(false))
                    .FirstOrDefault(m => m.Role == "fast");
                if (fastModel != null)
                {
                    await StopServerAsync(fastModel.Id).ConfigureAwait(false);
                }
            }

            await StartServerAsync(manifest, modelFile, mmprojFile, port, useGpu, serverKey, ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Marks the start of a request using the server for the given model. Dispose the returned handle when the request completes so idle eviction can consider the server.
    /// </summary>
    public IDisposable BeginRequest(string modelId)
    {
        if (!_modelIdToKey.TryGetValue(modelId, out var serverKey) || !_runningServers.TryGetValue(serverKey, out var server))
        {
            return NullRequestHandle.Instance;
        }

        Interlocked.Increment(ref server.ActiveRequests);
        return new RequestHandle(this, modelId);
    }

    private sealed class NullRequestHandle : IDisposable
    {
        internal static readonly NullRequestHandle Instance = new();
        public void Dispose() { }
    }

    internal void ReleaseServer(string modelId)
    {
        if (!_modelIdToKey.TryGetValue(modelId, out var serverKey) || !_runningServers.TryGetValue(serverKey, out var server))
        {
            return;
        }

        Interlocked.Decrement(ref server.ActiveRequests);
    }

    private sealed class RequestHandle : IDisposable
    {
        private readonly LlamaCppServerManager _manager;
        private readonly string _modelId;
        private int _released;

        internal RequestHandle(LlamaCppServerManager manager, string modelId)
        {
            _manager = manager;
            _modelId = modelId;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
            {
                return;
            }

            _manager.ReleaseServer(_modelId);
        }
    }

    private static string ComputeServerKey(AIModelManifest manifest, string modelPath, int port, bool useGpu, string? mmprojPath)
    {
        var contextSize = 8192;
        if (manifest.Metadata.TryGetValue("ContextSize", out var csStr) && int.TryParse(csStr, out var cs))
        {
            contextSize = cs;
        }

        var gpuLayers = 0;
        if (useGpu)
        {
            gpuLayers = 99;
            if (manifest.Metadata.TryGetValue("GpuLayers", out var layersStr) && int.TryParse(layersStr, out var layers))
            {
                gpuLayers = layers;
            }
        }

        var flashAttn = useGpu && manifest.Metadata.TryGetValue("FlashAttn", out var flashStr) && bool.TryParse(flashStr, out var flash) && flash;
        var payload = $"{modelPath}|{port}|{contextSize}|{gpuLayers}|{flashAttn}|{mmprojPath ?? ""}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        return hash;
    }

    private void KillLeftoversOnStartup()
    {
        List<LlamaServerRegistryEntry> entries;
        try
        {
            if (!File.Exists(_registryPath))
            {
                return;
            }

            var json = File.ReadAllText(_registryPath);
            entries = JsonSerializer.Deserialize<List<LlamaServerRegistryEntry>>(json) ?? new List<LlamaServerRegistryEntry>();
        }
        catch (Exception ex)
        {
            _logger.Warning("LlamaCppServerManager", $"Could not read PID registry: {ex.Message}");
            TryDeleteRegistryFile();
            return;
        }

        foreach (var entry in entries)
        {
            try
            {
                if (!LlamaProcessValidator.IsLlamaProcess(entry.Pid, entry.ServerPath))
                {
                    continue;
                }

                using var process = Process.GetProcessById(entry.Pid);
                if (process.HasExited)
                {
                    continue;
                }

                if (!IsPortServingLlama(entry.Port))
                {
                    continue;
                }

                _logger.Info("LlamaCppServerManager", $"Killing leftover llama-server from previous run (PID {entry.Pid})");
                process.Kill(entireProcessTree: true);
                try
                {
                    process.WaitForExit(TimeSpan.FromSeconds(HardKillWaitSeconds));
                }
                catch
                {
                    // Ignore
                }
            }
            catch (Exception ex)
            {
                _logger.Warning("LlamaCppServerManager", $"Could not kill leftover PID {entry.Pid}: {ex.Message}");
            }
        }

        TryDeleteRegistryFile();
    }

    private void TryDeleteRegistryFile()
    {
        try
        {
            if (File.Exists(_registryPath))
            {
                File.Delete(_registryPath);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning("LlamaCppServerManager", $"Could not delete registry file: {ex.Message}");
        }
    }

    /// <summary>
    /// Verifies that the given port is serving our llama API (ownership proof for crash recovery).
    /// Uses HTTP probe to /health or /v1/models so no CLI flags are required.
    /// </summary>
    private bool IsPortServingLlama(int port)
    {
        var baseUrl = $"http://127.0.0.1:{port}";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            using var healthResponse = _httpClient.GetAsync($"{baseUrl}/health", cts.Token).GetAwaiter().GetResult();
            if (healthResponse.IsSuccessStatusCode)
            {
                return true;
            }
        }
        catch
        {
            // Fallback to /v1/models
        }

        try
        {
            using var response = _httpClient.GetAsync($"{baseUrl}/v1/models", cts.Token).GetAwaiter().GetResult();
            if (response.IsSuccessStatusCode && LooksLikeLlamaResponseAsync(response, CancellationToken.None).GetAwaiter().GetResult())
            {
                return true;
            }
        }
        catch
        {
            // Ignore
        }

        return false;
    }

    private async Task<bool> IsHealthyAsync(string endpoint, DateTime startedAtUtc, CancellationToken ct)
    {
        var inStartupGrace = (DateTime.UtcNow - startedAtUtc) < StartupGracePeriod;
        if (inStartupGrace)
        {
            return true;
        }

        var baseUrl = endpoint.TrimEnd('/');
        var healthUrl = $"{baseUrl}/health";
        var modelsUrl = $"{baseUrl}/v1/models";
        try
        {
            var response = await _httpClient.GetAsync(healthUrl, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }
        }
        catch
        {
            // Fallback to /v1/models
        }

        try
        {
            var response = await _httpClient.GetAsync(modelsUrl, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            return await LooksLikeLlamaResponseAsync(response, ct).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> LooksLikeLlamaResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("object", out var obj) && obj.GetString() == "list"
                && root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                return true;
            }
        }
        catch
        {
            // Ignore
        }

        return false;
    }

    private void RemoveAndDisposeServer(string serverKey, ServerProcess serverProcess)
    {
        _runningServers.TryRemove(serverKey, out _);
        var modelId = serverProcess.Manifest.Id;
        _modelIdToKey.TryRemove(modelId, out _);
        _lastUsed.TryRemove(modelId, out _);

        try
        {
            if (!serverProcess.Process.HasExited)
            {
                serverProcess.Process.Kill(entireProcessTree: true);
            }

            serverProcess.Process.Dispose();
        }
        catch (Exception ex)
        {
            _logger.Warning("LlamaCppServerManager", $"Error disposing dead server: {ex.Message}");
        }

        PersistRegistry();
    }

    private async Task StartServerAsync(AIModelManifest manifest, string modelFile, string? mmprojFile, int port, bool useGpu, string serverKey, CancellationToken ct)
    {
        var serverPath = await _settings.GetAsync<string>("AI.LlamaCpp.ServerPath").ConfigureAwait(false);

        if (string.IsNullOrEmpty(serverPath) || !File.Exists(serverPath))
        {
            throw new FileNotFoundException(
                $"llama.cpp server executable not found. Please set AI.LlamaCpp.ServerPath in settings. Current path: {serverPath}");
        }

        var args = BuildServerArgs(manifest, modelFile, mmprojFile, port, useGpu);

        _logger.Info("LlamaCppServerManager", $"Starting llama.cpp server for {manifest.DisplayName} on port {port}");
        _logger.Info("LlamaCppServerManager", $"Command: {serverPath} {args}");

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = serverPath,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(serverPath)
            }
        };

        process.OutputDataReceived += (s, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                _logger.Info($"llama.cpp[{manifest.Role}]", e.Data);
            }
        };

        process.ErrorDataReceived += (s, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                _logger.Warning($"llama.cpp[{manifest.Role}]", e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var startedAt = DateTime.UtcNow;
        _runningServers[serverKey] = new ServerProcess
        {
            Process = process,
            Manifest = manifest,
            Port = port,
            ModelKey = serverKey,
            StartedAt = startedAt,
            ServerPath = serverPath
        };
        _modelIdToKey[manifest.Id] = serverKey;

        PersistRegistry();

        _logger.Info("LlamaCppServerManager", $"Server started with PID {process.Id}, waiting for health check...");

        if (!string.IsNullOrEmpty(manifest.Endpoint))
        {
            await WaitForHealthAsync(manifest.Endpoint, ct).ConfigureAwait(false);
        }

        _logger.Info("LlamaCppServerManager", $"Server for {manifest.DisplayName} is ready!");
    }

    private string BuildServerArgs(AIModelManifest manifest, string modelPath, string? mmprojPath, int port, bool useGpu)
    {
        var contextSize = 8192;
        if (manifest.Metadata.TryGetValue("ContextSize", out var csStr) && int.TryParse(csStr, out var cs))
        {
            contextSize = cs;
        }

        var gpuLayers = 0;
        if (useGpu)
        {
            gpuLayers = 99;
            if (manifest.Metadata.TryGetValue("GpuLayers", out var layersStr) && int.TryParse(layersStr, out var layers))
            {
                gpuLayers = layers;
            }
        }

        var args = $"-m \"{modelPath}\" " +
                   $"-c {contextSize} " +
                   $"-ngl {gpuLayers} " +
                   $"--host 127.0.0.1 " +
                   $"--port {port} " +
                   $"--cache-reuse 0 " +
                   $"--cont-batching " +
                   $"--metrics " +
                   $"--mlock";

        if (!string.IsNullOrEmpty(mmprojPath))
        {
            args += $" --mmproj \"{mmprojPath}\"";
        }

        if (useGpu && manifest.Metadata.TryGetValue("FlashAttn", out var flashStr) && bool.TryParse(flashStr, out var flash) && flash)
        {
            args += " --flash-attn";
        }

        return args;
    }

    private string ResolveModelPath(AIModelManifest manifest)
    {
        if (!manifest.Metadata.TryGetValue("FileName", out var fileName))
        {
            throw new ArgumentException($"Model manifest for '{manifest.DisplayName}' is missing the 'FileName' metadata field.");
        }

        var modelPath = Path.Combine(manifest.LocalPath, fileName);

        if (File.Exists(modelPath))
        {
            return modelPath;
        }

        if (!fileName.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
        {
            var ggufPath = modelPath + ".gguf";
            if (File.Exists(ggufPath))
            {
                return ggufPath;
            }
        }

        throw new FileNotFoundException($"Model file not found at: {modelPath}");
    }

    /// <summary>
    /// Resolves the multimodal projector (mmproj) path if the manifest specifies one.
    /// Add "MmprojFileName" to the model's manifest.json Metadata to enable vision (e.g. for Ministral 3 3B).
    /// </summary>
    private static string? ResolveMmprojPath(AIModelManifest manifest)
    {
        if (!manifest.Metadata.TryGetValue("MmprojFileName", out var fileName) || string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var path = Path.Combine(manifest.LocalPath, fileName.Trim());
        return File.Exists(path) ? path : null;
    }

    private async Task WaitForHealthAsync(string endpoint, CancellationToken ct)
    {
        var baseUrl = endpoint.TrimEnd('/');
        var healthUrl = $"{baseUrl}/health";
        var fallbackUrl = $"{baseUrl}/v1/models";
        var maxAttempts = 60;
        var attempt = 0;

        while (attempt < maxAttempts && !ct.IsCancellationRequested)
        {
            try
            {
                var response = await _httpClient.GetAsync(healthUrl, ct).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch
            {
                try
                {
                    var response = await _httpClient.GetAsync(fallbackUrl, ct).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        return;
                    }
                }
                catch
                {
                    // Continue
                }
            }

            var delayMs = attempt < 5 ? 200 : 1000;
            await Task.Delay(delayMs, ct).ConfigureAwait(false);
            attempt++;
        }

        throw new TimeoutException($"Server did not become healthy within {maxAttempts} seconds.");
    }

    private void PersistRegistry()
    {
        var entries = _runningServers.Values
            .Where(s => !s.Process.HasExited)
            .Select(s => new LlamaServerRegistryEntry
            {
                Pid = s.Process.Id,
                Port = s.Port,
                ModelKey = s.ModelKey,
                StartTimeUtc = s.StartedAt.ToString("O"),
                ServerPath = s.ServerPath
            })
            .ToList();

        try
        {
            var dir = Path.GetDirectoryName(_registryPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var tmpPath = _registryPath + ".tmp";
            File.WriteAllText(tmpPath, JsonSerializer.Serialize(entries));
            if (File.Exists(_registryPath))
            {
                File.Replace(tmpPath, _registryPath, null);
            }
            else
            {
                File.Move(tmpPath, _registryPath);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning("LlamaCppServerManager", $"Could not persist PID registry: {ex.Message}");
            try
            {
                if (File.Exists(_registryPath + ".tmp"))
                {
                    File.Delete(_registryPath + ".tmp");
                }
            }
            catch
            {
                // Ignore
            }
        }
    }

    /// <summary>
    /// Stops the server for the given model ID and removes it from the PID registry.
    /// </summary>
    public async Task StopServerAsync(string modelId)
    {
        if (!_modelIdToKey.TryRemove(modelId, out var serverKey))
        {
            return;
        }

        if (!_runningServers.TryRemove(serverKey, out var serverProcess))
        {
            return;
        }

        _lastUsed.TryRemove(modelId, out _);

        await StopProcessAsync(serverProcess).ConfigureAwait(false);
        PersistRegistry();
    }

    private async Task StopProcessAsync(ServerProcess serverProcess)
    {
        try
        {
            if (serverProcess.Process.HasExited)
            {
                serverProcess.Process.Dispose();
                return;
            }

            _logger.Info("LlamaCppServerManager", $"Stopping server for {serverProcess.Manifest.DisplayName} (PID {serverProcess.Process.Id})");

            var baseUrl = (serverProcess.Manifest.Endpoint ?? "").TrimEnd('/');
            if (!string.IsNullOrEmpty(baseUrl))
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await _httpClient.PostAsync($"{baseUrl}/shutdown", null, cts.Token).ConfigureAwait(false);
                }
                catch
                {
                    // Graceful endpoint may not exist or may have failed
                }
            }

            try
            {
                using var exitCts = new CancellationTokenSource(TimeSpan.FromSeconds(GracefulShutdownWaitSeconds));
                await serverProcess.Process.WaitForExitAsync(exitCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Timeout; proceed to hard kill
            }

            if (!serverProcess.Process.HasExited)
            {
                serverProcess.Process.Kill(entireProcessTree: true);
                using var killCts = new CancellationTokenSource(TimeSpan.FromSeconds(HardKillWaitSeconds));
                try
                {
                    await serverProcess.Process.WaitForExitAsync(killCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Process didn't exit in time; Kill was already sent
                }
            }

            serverProcess.Process.Dispose();
        }
        catch (Exception ex)
        {
            _logger.Error("LlamaCppServerManager", $"Error stopping server: {ex.Message}", ex);
            try
            {
                if (!serverProcess.Process.HasExited)
                {
                    serverProcess.Process.Kill(entireProcessTree: true);
                }

                serverProcess.Process.Dispose();
            }
            catch
            {
                // Ignore
            }
        }
    }

    private void CleanupIdleServers(object? state)
    {
        if (Interlocked.CompareExchange(ref _disposed, 0, 0) != 0)
        {
            return;
        }

        var now = DateTime.UtcNow;

        foreach (var kvp in _lastUsed.ToList())
        {
            var modelId = kvp.Key;
            var lastUsed = kvp.Value;

            if (!_modelIdToKey.TryGetValue(modelId, out var serverKey) || !_runningServers.TryGetValue(serverKey, out var serverProcess))
            {
                continue;
            }

            var manifest = serverProcess.Manifest;
            var idle = now - lastUsed;

            if (manifest.Role == "router")
            {
                continue;
            }

            var shouldUnload = manifest.Role switch
            {
                "fast" => idle > _fastIdleTimeout,
                "smart" => idle > _smartIdleTimeout,
                _ => false
            };

            if (shouldUnload && serverProcess.ActiveRequests == 0)
            {
                _logger.Info("LlamaCppServerManager", $"Model {manifest.DisplayName} idle for {idle.TotalMinutes:F1} minutes, unloading...");
                _ = StopServerAsync(modelId);
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _cleanupTimer.Dispose();
        _httpClient.Dispose();
        foreach (var sem in _locks.Values)
        {
            sem.Dispose();
        }
        _locks.Clear();

        foreach (var server in _runningServers.Values.ToList())
        {
            try
            {
                if (!server.Process.HasExited)
                {
                    server.Process.Kill(entireProcessTree: true);
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(HardKillWaitSeconds));
                    try
                    {
                        server.Process.WaitForExitAsync(cts.Token).GetAwaiter().GetResult();
                    }
                    catch (OperationCanceledException)
                    {
                        // Ignore
                    }
                }

                server.Process.Dispose();
            }
            catch (Exception ex)
            {
                _logger.Error("LlamaCppServerManager", $"Error disposing server: {ex.Message}", ex);
            }
        }

        _runningServers.Clear();
        _modelIdToKey.Clear();
        _lastUsed.Clear();
        TryDeleteRegistryFile();
    }

    private sealed class ServerProcess
    {
        public Process Process { get; set; } = null!;
        public AIModelManifest Manifest { get; set; } = null!;
        public int Port { get; set; }
        public string ModelKey { get; set; } = null!;
        public DateTime StartedAt { get; set; }
        public string? ServerPath { get; set; }
        public int ActiveRequests;
    }
}
