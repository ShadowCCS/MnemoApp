using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.AI;

/// <summary>
/// Manages llama.cpp server processes for local text models.
/// </summary>
public class LlamaCppServerManager : IAIServerManager
{
    private readonly ILoggerService _logger;
    private readonly ISettingsService _settings;
    private readonly HardwareDetector _hardware;
    private readonly IAIModelRegistry _modelRegistry;
    private readonly HttpClient _httpClient;
    
    private readonly ConcurrentDictionary<string, ServerProcess> _runningServers = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastUsed = new();
    private readonly Timer _cleanupTimer;

    private readonly TimeSpan _fastIdleTimeout = TimeSpan.FromMinutes(15);
    private readonly TimeSpan _smartIdleTimeout = TimeSpan.FromMinutes(7);
    
    private bool _disposed;

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

        // Check idle servers every minute
        _cleanupTimer = new Timer(CleanupIdleServers, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// Ensures the specified model's server is running and ready.
    /// </summary>
    public async Task EnsureRunningAsync(AIModelManifest manifest, CancellationToken ct)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(LlamaCppServerManager));
        }

        if (string.IsNullOrEmpty(manifest.Endpoint))
        {
            throw new InvalidOperationException($"Model {manifest.DisplayName} has no endpoint configured.");
        }

        // Update last used time
        _lastUsed[manifest.Id] = DateTime.UtcNow;

        // If already running, just verify health
        if (_runningServers.TryGetValue(manifest.Id, out var existing) && !existing.Process.HasExited)
        {
            _logger.Info("LlamaCppServerManager", $"Server for {manifest.DisplayName} already running on PID {existing.Process.Id}");
            return;
        }

        // Memory pressure policy: if starting smart, stop fast first
        if (manifest.Role == "smart")
        {
            var fastModel = (await _modelRegistry.GetAvailableModelsAsync().ConfigureAwait(false))
                .FirstOrDefault(m => m.Role == "fast");
            
            if (fastModel != null)
            {
                await StopServerAsync(fastModel.Id).ConfigureAwait(false);
            }
        }

        // Start the server
        await StartServerAsync(manifest, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts a new llama.cpp server process for the given model.
    /// </summary>
    private async Task StartServerAsync(AIModelManifest manifest, CancellationToken ct)
    {
        var serverPath = await _settings.GetAsync<string>("AI.LlamaCpp.ServerPath").ConfigureAwait(false);
        
        if (string.IsNullOrEmpty(serverPath) || !File.Exists(serverPath))
        {
            throw new FileNotFoundException(
                $"llama.cpp server executable not found. Please set AI.LlamaCpp.ServerPath in settings. Current path: {serverPath}");
        }

        // Find the model file
        var modelFile = ResolveModelPath(manifest);
        
        // Parse endpoint to get port
        var uri = new Uri(manifest.Endpoint!);
        var port = uri.Port;

        var useGpuSetting = await _settings.GetAsync<bool?>("AI.GpuAcceleration").ConfigureAwait(false);
        var hardwareInfo = _hardware.Detect();
        var useGpu = useGpuSetting ?? hardwareInfo.HasNvidiaGpu;

        // Build command line arguments
        var args = BuildServerArgs(manifest, modelFile, port, useGpu);

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

        // Log output for debugging
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

        _runningServers[manifest.Id] = new ServerProcess
        {
            Process = process,
            Manifest = manifest,
            StartedAt = DateTime.UtcNow
        };

        _logger.Info("LlamaCppServerManager", $"Server started with PID {process.Id}, waiting for health check...");

        // Wait for server to be ready
        if (!string.IsNullOrEmpty(manifest.Endpoint))
        {
            await WaitForHealthAsync(manifest.Endpoint, ct).ConfigureAwait(false);
        }
        
        _logger.Info("LlamaCppServerManager", $"Server for {manifest.DisplayName} is ready!");
    }

    /// <summary>
    /// Resolves the full path to the model file from the manifest.
    /// </summary>
    private string ResolveModelPath(AIModelManifest manifest)
    {
        if (!manifest.Metadata.TryGetValue("FileName", out var fileName))
        {
            throw new ArgumentException($"Model manifest for '{manifest.DisplayName}' is missing the 'FileName' metadata field.");
        }

        var modelPath = Path.Combine(manifest.LocalPath, fileName);
        
        if (File.Exists(modelPath)) return modelPath;

        // Try adding .gguf if it's missing
        if (!fileName.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
        {
            var ggufPath = modelPath + ".gguf";
            if (File.Exists(ggufPath)) return ggufPath;
        }

        throw new FileNotFoundException($"Model file not found at: {modelPath}");
    }

    /// <summary>
    /// Builds command line arguments for the llama.cpp server.
    /// </summary>
    private string BuildServerArgs(AIModelManifest manifest, string modelPath, int port, bool useGpu)
    {
        var contextSize = 8192;
        if (manifest.Metadata.TryGetValue("ContextSize", out var csStr) && int.TryParse(csStr, out var cs))
        {
            contextSize = cs;
        }

        var gpuLayers = 0;
        if (useGpu)
        {
            // Default: try to offload all layers
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

        // Add flash attention if GPU is enabled and supported
        if (useGpu && manifest.Metadata.TryGetValue("FlashAttn", out var flashStr) && bool.TryParse(flashStr, out var flash) && flash)
        {
            args += " --flash-attn";
        }

        return args;
    }

    /// <summary>
    /// Waits for the server to become healthy by polling its health endpoint.
    /// </summary>
    private async Task WaitForHealthAsync(string endpoint, CancellationToken ct)
    {
        var baseUrl = endpoint.TrimEnd('/');
        var healthUrl = $"{baseUrl}/health";
        var fallbackUrl = $"{baseUrl}/v1/models";

        var maxAttempts = 60; // 60 seconds max wait
        var attempt = 0;

        while (attempt < maxAttempts && !ct.IsCancellationRequested)
        {
            try
            {
                // Try health endpoint first
                var response = await _httpClient.GetAsync(healthUrl, ct).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch
            {
                // Try fallback endpoint
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
                    // Continue waiting
                }
            }

            // Fast polling for first 5 attempts (200ms), then 1s so server becomes ready sooner when it starts quickly
            var delayMs = attempt < 5 ? 200 : 1000;
            await Task.Delay(delayMs, ct).ConfigureAwait(false);
            attempt++;
        }

        throw new TimeoutException($"Server did not become healthy within {maxAttempts} seconds.");
    }

    /// <summary>
    /// Stops the server for the given model ID.
    /// </summary>
    public async Task StopServerAsync(string modelId)
    {
        if (_runningServers.TryRemove(modelId, out var serverProcess))
        {
            try
            {
                if (!serverProcess.Process.HasExited)
                {
                    _logger.Info("LlamaCppServerManager", $"Stopping server for {serverProcess.Manifest.DisplayName} (PID {serverProcess.Process.Id})");
                    
                    serverProcess.Process.Kill(entireProcessTree: true);
                    await serverProcess.Process.WaitForExitAsync().ConfigureAwait(false);
                }
                
                serverProcess.Process.Dispose();
            }
            catch (Exception ex)
            {
                _logger.Error("LlamaCppServerManager", $"Error stopping server: {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// Cleanup timer callback that stops idle servers.
    /// </summary>
    private void CleanupIdleServers(object? state)
    {
        if (_disposed) return;

        var now = DateTime.UtcNow;
        
        foreach (var kvp in _lastUsed.ToList())
        {
            var modelId = kvp.Key;
            var lastUsed = kvp.Value;
            
            if (!_runningServers.TryGetValue(modelId, out var serverProcess))
            {
                continue;
            }

            var manifest = serverProcess.Manifest;
            var idle = now - lastUsed;

            // Router never unloads
            if (manifest.Role == "router")
            {
                continue;
            }

            // Check idle timeout based on role
            var shouldUnload = manifest.Role switch
            {
                "fast" => idle > _fastIdleTimeout,
                "smart" => idle > _smartIdleTimeout,
                _ => false
            };

            if (shouldUnload)
            {
                _logger.Info("LlamaCppServerManager", $"Model {manifest.DisplayName} idle for {idle.TotalMinutes:F1} minutes, unloading...");
                _ = StopServerAsync(modelId); // Fire and forget
                _lastUsed.TryRemove(modelId, out _);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cleanupTimer.Dispose();
        _httpClient.Dispose();

        // Stop all running servers and wait briefly for them to exit (cross-platform)
        foreach (var kvp in _runningServers.ToList())
        {
            try
            {
                if (!kvp.Value.Process.HasExited)
                {
                    kvp.Value.Process.Kill(entireProcessTree: true);
                    try
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        kvp.Value.Process.WaitForExitAsync(cts.Token).GetAwaiter().GetResult();
                    }
                    catch (OperationCanceledException)
                    {
                        // Process didn't exit in time; Kill was already sent
                    }
                }
                kvp.Value.Process.Dispose();
            }
            catch (Exception ex)
            {
                _logger.Error("LlamaCppServerManager", $"Error disposing server: {ex.Message}", ex);
            }
        }
        
        _runningServers.Clear();
    }

    private class ServerProcess
    {
        public Process Process { get; set; } = null!;
        public AIModelManifest Manifest { get; set; } = null!;
        public DateTime StartedAt { get; set; }
    }
}
