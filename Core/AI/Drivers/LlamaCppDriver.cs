using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MnemoApp.Core.AI.Models;

namespace MnemoApp.Core.AI.Drivers
{
    /// <summary>
    /// Driver for llama.cpp compatible models
    /// </summary>
    public class LlamaCppDriver : IModelDriver
    {
        public string DriverName => "LlamaCppDriver";

        private readonly HttpClient _httpClient;
        private readonly Dictionary<string, Process> _runningProcesses = new();
        private readonly Dictionary<string, string> _modelEndpoints = new();
        private readonly Services.IAILogger _logger;

        public LlamaCppDriver(Services.IAILogger? logger = null)
        {
            var handler = new HttpClientHandler
            {
                UseProxy = false
            };
            _httpClient = new HttpClient(handler);
            _httpClient.Timeout = TimeSpan.FromMinutes(5);
            _logger = logger ?? new Services.DebugAILogger();
        }

        public bool CanHandle(AIModel model)
        {
            return model.Capabilities?.ExecutionConfig?.ContainsKey("executable") == true ||
                   model.Capabilities?.HttpEndpoint != null ||
                   model.ModelFileName.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<bool> InitializeAsync(AIModel model)
        {
            try
            {
                if (model.Capabilities?.HttpEndpoint != null)
                {
                    // Use existing HTTP endpoint; trust and defer validation to first request
                    _modelEndpoints[model.Manifest.Name] = model.Capabilities.HttpEndpoint;
                    return true;
                }

                if (model.Capabilities?.ExecutionConfig != null)
                {
                    // Start llama.cpp server process
                    return await StartLlamaCppServerAsync(model);
                }

                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize {DriverName} for {model.Manifest.Name}: {ex.Message}");
                return false;
            }
        }

        public async Task<AIInferenceResponse> InferAsync(AIModel model, AIInferenceRequest request, CancellationToken cancellationToken = default)
        {
            if (!_modelEndpoints.TryGetValue(model.Manifest.Name, out var endpoint))
            {
                return new AIInferenceResponse
                {
                    Success = false,
                    ErrorMessage = $"Model {model.Manifest.Name} not initialized"
                };
            }

            try
            {
                // brief readiness wait to handle race with server start
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    if (await TestConnectionAsync(endpoint)) break;
                    await Task.Delay(250, cancellationToken);
                }

                var startTime = DateTime.UtcNow;
                var prompt = FormatPrompt(model, request);

                var payload = new
                {
                    prompt = prompt,
                    temperature = request.Temperature,
                    top_p = request.TopP,
                    top_k = request.TopK,
                    repeat_penalty = request.RepeatPenalty,
                    n_predict = request.MaxTokens,
                    stop = request.StopTokens ?? model.Capabilities?.StopTokens ?? new List<string>(),
                    stream = false
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(endpoint, content, cancellationToken);
                response.EnsureSuccessStatusCode();

                var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
                var result = JsonSerializer.Deserialize<JsonElement>(responseText);

                var generatedText = result.GetProperty("content").GetString() ?? "";
                var processingTime = DateTime.UtcNow - startTime;

                return new AIInferenceResponse
                {
                    Success = true,
                    Response = CleanResponse(generatedText, model, request),
                    TokensGenerated = EstimateTokens(generatedText),
                    TokensProcessed = EstimateTokens(prompt),
                    ProcessingTime = processingTime,
                    Metadata = new Dictionary<string, object>
                    {
                        ["model"] = model.Manifest.Name,
                        ["driver"] = DriverName,
                        ["endpoint"] = endpoint
                    }
                };
            }
            catch (Exception ex)
            {
                return new AIInferenceResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async IAsyncEnumerable<string> InferStreamAsync(AIModel model, AIInferenceRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (!_modelEndpoints.TryGetValue(model.Manifest.Name, out var endpoint))
            {
                yield return $"Error: Model {model.Manifest.Name} not initialized";
                yield break;
            }

            var prompt = FormatPrompt(model, request);

            var payload = new
            {
                prompt = prompt,
                temperature = request.Temperature,
                top_p = request.TopP,
                top_k = request.TopK,
                repeat_penalty = request.RepeatPenalty,
                n_predict = request.MaxTokens,
                stop = request.StopTokens ?? model.Capabilities?.StopTokens ?? new List<string>(),
                stream = true
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var requestMessage = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = content
            };
            var response = await _httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

            while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrEmpty(line))
                    continue;

                // Handle SSE format variations
                string jsonData;
                if (line.StartsWith("data: "))
                {
                    jsonData = line[6..]; // Remove "data: " prefix
                }
                else if (line.StartsWith("data:"))
                {
                    jsonData = line[5..]; // Some servers don't include space
                }
                else if (line.StartsWith("{")) // Direct JSON without SSE wrapper
                {
                    jsonData = line;
                }
                else
                {
                    continue; // Skip non-data lines
                }

                jsonData = jsonData.Trim();
                if (jsonData == "[DONE]" || jsonData == "")
                    break;

                string? token = null;
                try
                {
                    var streamResult = JsonSerializer.Deserialize<JsonElement>(jsonData);
                    // llama.cpp typically uses "content" directly
                    if (streamResult.TryGetProperty("content", out var contentProp))
                    {
                        token = contentProp.GetString();
                    }
                    // Fallback for other formats
                    else if (streamResult.TryGetProperty("text", out var textProp))
                    {
                        token = textProp.GetString();
                    }
                    else if (streamResult.TryGetProperty("token", out var tokenProp))
                    {
                        token = tokenProp.GetString();
                    }
                }
                catch (Exception ex)
                {
                    // Log but continue - some chunks might be malformed
                    System.Diagnostics.Debug.WriteLine($"Failed to parse llama.cpp streaming chunk: {ex.Message}, data: {jsonData}");
                    continue;
                }

                if (!string.IsNullOrEmpty(token))
                {
                    // Clean each streaming token to remove stop tokens
                    var cleanedToken = CleanStreamingToken(token, model, request);
                    if (!string.IsNullOrEmpty(cleanedToken))
                        yield return cleanedToken;
                }
            }
        }

        public async Task ShutdownAsync(AIModel model)
        {
            if (_runningProcesses.TryGetValue(model.Manifest.Name, out var process))
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        await process.WaitForExitAsync();
                    }
                }
                catch
                {
                    // Ignore cleanup errors
                }
                finally
                {
                    process.Dispose();
                    _runningProcesses.Remove(model.Manifest.Name);
                    _modelEndpoints.Remove(model.Manifest.Name);
                }
            }
        }

        public async Task<bool> IsReadyAsync(AIModel model)
        {
            if (!_modelEndpoints.TryGetValue(model.Manifest.Name, out var endpoint))
                return false;

            return await TestConnectionAsync(endpoint);
        }

        private string FormatPrompt(AIModel model, AIInferenceRequest request)
        {
            var template = model.Capabilities?.PromptTemplate ?? "{user_prompt}";
            var formatted = template.Replace("{user_prompt}", request.Prompt);

            if (!string.IsNullOrEmpty(request.SystemPrompt) && model.Capabilities?.SystemPromptSupport == true)
            {
                formatted = formatted.Replace("{system_prompt}", request.SystemPrompt);
            }

            // Add thinking prompt if enabled
            if (model.Capabilities?.SupportsThinking == true && !string.IsNullOrEmpty(model.Capabilities.ThinkingPrompt))
            {
                formatted = model.Capabilities.ThinkingPrompt + "\n\n" + formatted;
            }

            return formatted;
        }

        private async Task<bool> StartLlamaCppServerAsync(AIModel model)
        {
            System.Diagnostics.Debug.WriteLine("StartLlamaCppServerAsync: Entry");
            if (model.Capabilities?.ExecutionConfig == null)
            {
                System.Diagnostics.Debug.WriteLine("StartLlamaCppServerAsync: No ExecutionConfig");
                return false;
            }

            var config = model.Capabilities.ExecutionConfig;
            if (!config.TryGetValue("executable", out var executableObj))
            {
                System.Diagnostics.Debug.WriteLine("StartLlamaCppServerAsync: No executable key in config");
                return false;
            }

            string executable;
            if (executableObj is JsonElement executableElement)
            {
                executable = executableElement.GetString() ?? "";
            }
            else if (executableObj is string executableStr)
            {
                executable = executableStr;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"StartLlamaCppServerAsync: Unexpected executable type: {executableObj?.GetType().Name ?? "null"}");
                return false;
            }

            if (string.IsNullOrWhiteSpace(executable))
            {
                System.Diagnostics.Debug.WriteLine("StartLlamaCppServerAsync: Empty executable");
                return false;
            }

            if (!config.TryGetValue("args", out var argsObj) || argsObj is not JsonElement argsElement)
            {
                System.Diagnostics.Debug.WriteLine("StartLlamaCppServerAsync: No args in config");
                return false;
            }

            // Resolve executable path robustly (placeholders, relative locations, PATH)
            var resolvedExecutable = ResolveExecutablePath(executable, model);
            System.Diagnostics.Debug.WriteLine($"LlamaCpp spawn attempt: Configured='{executable}', Resolved='{resolvedExecutable ?? "<null>"}', Exists={File.Exists(resolvedExecutable ?? "")}");
            if (resolvedExecutable == null || !File.Exists(resolvedExecutable))
            {
                System.Diagnostics.Debug.WriteLine($"llama.cpp executable not found. Configured: '{executable}', Resolved: '{resolvedExecutable ?? "<null>"}'");
                return false;
            }

            var args = new List<string>();
            foreach (var arg in argsElement.EnumerateArray())
            {
                var argStr = arg.GetString();
                if (argStr != null)
                {
                    // Replace placeholders
                    argStr = argStr.Replace("{model_path}", Path.Combine(model.DirectoryPath, model.ModelFileName));
                    argStr = argStr.Replace("{ctx}", model.Capabilities?.MaxContextLength.ToString() ?? "2048");
                    args.Add(argStr);
                }
            }

            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = resolvedExecutable,
                    Arguments = string.Join(" ", args.ConvertAll(arg => $"\"{arg}\"")),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = model.DirectoryPath // Set working directory to model directory
                };

                var process = new Process { StartInfo = processInfo, EnableRaisingEvents = true };
                var stderrBuffer = new System.Collections.Concurrent.ConcurrentQueue<string>();
                var stdoutBuffer = new System.Collections.Concurrent.ConcurrentQueue<string>();

                process.OutputDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) stdoutBuffer.Enqueue(e.Data); };
                process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) stderrBuffer.Enqueue(e.Data); };

                System.Diagnostics.Debug.WriteLine($"LlamaCpp starting process: '{resolvedExecutable}' {processInfo.Arguments}");
                if (!process.Start())
                {
                    System.Diagnostics.Debug.WriteLine("LlamaCpp process failed to start");
                    return false;
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                _runningProcesses[model.Manifest.Name] = process;

                // Extract port from args to build endpoint
                var portIndex = args.FindIndex(a => a == "--port");
                var port = portIndex >= 0 && portIndex + 1 < args.Count ? args[portIndex + 1] : "8080";
                var endpoint = $"http://127.0.0.1:{port}/completion";
                _modelEndpoints[model.Manifest.Name] = endpoint;

                // Wait for server to start with retries and timeout
                var ready = false;
                var maxWaitTime = TimeSpan.FromSeconds(30); // Total timeout
                var startTime = DateTime.UtcNow;
                
                while (!ready && DateTime.UtcNow - startTime < maxWaitTime)
                {
                    await Task.Delay(1000);
                    
                    if (process.HasExited)
                    {
                        System.Diagnostics.Debug.WriteLine($"llama.cpp process exited during startup (exit code: {process.ExitCode})");
                        break;
                    }
                    
                    if (await TestConnectionAsync(endpoint))
                    {
                        ready = true;
                        break;
                    }
                }

                if (!ready)
                {
                    // Log diagnostics
                    System.Diagnostics.Debug.WriteLine($"llama.cpp server failed to become ready. exe='{resolvedExecutable}', args='{processInfo.Arguments}', endpoint='{endpoint}', hasExited={process.HasExited}");

                    // Drain some stderr lines for context
                    var errPreview = new List<string>();
                    while (stderrBuffer.TryDequeue(out var line) && errPreview.Count < 20)
                    {
                        errPreview.Add(line);
                    }
                    if (errPreview.Count > 0)
                    {
                        System.Diagnostics.Debug.WriteLine("llama.cpp stderr (first lines):" + Environment.NewLine + string.Join(Environment.NewLine, errPreview));
                    }

                    return false;
                }

                System.Diagnostics.Debug.WriteLine("llama.cpp server ready!");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to start llama.cpp server: {ex.Message}");
                return false;
            }
        }

        private static string? ResolveExecutablePath(string configuredExecutable, AIModel model)
        {
            if (string.IsNullOrWhiteSpace(configuredExecutable))
                return null;

            // Environment override
            var envOverride = Environment.GetEnvironmentVariable("MNEMO_LLAMA_SERVER");
            if (!string.IsNullOrWhiteSpace(envOverride) && File.Exists(envOverride))
                return envOverride;

            // Replace placeholders
            string exe = configuredExecutable
                .Replace("{app_dir}", AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase)
                .Replace("{model_dir}", model.DirectoryPath, StringComparison.OrdinalIgnoreCase)
                .Replace("{models_dir}", Path.GetDirectoryName(model.DirectoryPath) ?? string.Empty, StringComparison.OrdinalIgnoreCase);

            // If path points to a directory, append default filename
            exe = AppendDefaultIfDirectory(exe);

            var candidates = new List<string>();

            // Absolute path first
            if (Path.IsPathRooted(exe))
                candidates.Add(exe);

            // Relative to model dir
            candidates.Add(Path.Combine(model.DirectoryPath, exe));
            // Relative to models parent dir
            var modelsDir = Path.GetDirectoryName(model.DirectoryPath) ?? string.Empty;
            if (!string.IsNullOrEmpty(modelsDir))
                candidates.Add(Path.Combine(modelsDir, exe));
            // Relative to app base dir
            candidates.Add(Path.Combine(AppContext.BaseDirectory, exe));
            // Relative to current working dir
            candidates.Add(Path.Combine(Environment.CurrentDirectory, exe));

            foreach (var candidate in candidates)
            {
                var normalized = AppendDefaultIfDirectory(candidate);
                if (File.Exists(normalized))
                    return normalized;
            }

            // Search in PATH
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var pathDir in pathEnv.Split(Path.PathSeparator))
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(pathDir)) continue;
                    var candidate = Path.Combine(pathDir, GetPlatformExecutableName(exe));
                    if (File.Exists(candidate))
                        return candidate;
                }
                catch { }
            }

            return null;
        }

        private static string AppendDefaultIfDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    var filename = GetPlatformExecutableName("llama-server");
                    return Path.Combine(path, filename);
                }
            }
            catch { }
            return path;
        }

        private static string GetPlatformExecutableName(string baseName)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (baseName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return baseName;
                return baseName + ".exe";
            }
            return baseName;
        }

        private async Task<bool> TestConnectionAsync(string endpoint)
        {
            try
            {
                var testPayload = new { prompt = "test", n_predict = 1 };
                var json = JsonSerializer.Serialize(testPayload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var response = await _httpClient.PostAsync(endpoint, content, cts.Token);

                var status = (int)response.StatusCode;
                // Consider typical non-success statuses as "server reachable" for readiness (e.g., requires different payload)
                return response.IsSuccessStatusCode || status == 404 || status == 422;
            }
            catch
            {
                return false;
            }
        }

        private string CleanStreamingToken(string token, AIModel model, AIInferenceRequest request)
        {
            if (string.IsNullOrEmpty(token))
                return token;

            // For streaming, we need to be more careful - only remove complete stop tokens
            var commonStopTokens = new[]
            {
                "<|im_end|>", "|<im_end|>", "<im_end>", "im_end>",
                "<|endoftext|>", "<|end_of_text|>",
                "</s>", "<s>",
                "<|assistant|>", "<|user|>", "<|system|>",
                "### Human:", "### Assistant:", "Human:", "Assistant:",
                "[INST]", "[/INST]", "<<SYS>>", "<</SYS>>",
                "<end_of_turn>", "<start_of_turn>",
            };

            var stopTokens = request.StopTokens ?? model.Capabilities?.StopTokens ?? new List<string>();
            var allStopTokens = stopTokens.Concat(commonStopTokens).Distinct().ToList();

            // For streaming tokens, only remove if the token contains a complete stop token
            foreach (var stopToken in allStopTokens.OrderByDescending(t => t.Length))
            {
                if (token.Contains(stopToken, StringComparison.OrdinalIgnoreCase))
                {
                    // Return empty string to stop the stream
                    return "";
                }
            }

            return token;
        }

        private string CleanResponse(string text, AIModel model, AIInferenceRequest request)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            var cleaned = text.Trim();

            // Remove common instruction-tuned model stop tokens
            var commonStopTokens = new[]
            {
                "<|im_end|>", "|<im_end|>", "<im_end>", "im_end>", // ChatML format variants
                "<|endoftext|>", "<|end_of_text|>", // GPT variants
                "</s>", "<s>", // Llama variants
                "<|assistant|>", "<|user|>", "<|system|>", // Role tokens
                "### Human:", "### Assistant:", "Human:", "Assistant:", // Alpaca variants
                "[INST]", "[/INST]", "<<SYS>>", "<</SYS>>", // Llama-2 chat variants
                "<end_of_turn>", "<start_of_turn>", // Gemma variants
            };

            // First try model-specific stop tokens
            var stopTokens = request.StopTokens ?? model.Capabilities?.StopTokens ?? new List<string>();
            
            // Add common stop tokens to the list
            var allStopTokens = stopTokens.Concat(commonStopTokens).Distinct().ToList();

            // Remove any stop tokens that appear at the end
            foreach (var stopToken in allStopTokens.OrderByDescending(t => t.Length))
            {
                if (cleaned.EndsWith(stopToken, StringComparison.OrdinalIgnoreCase))
                {
                    cleaned = cleaned.Substring(0, cleaned.Length - stopToken.Length).TrimEnd();
                }
            }

            // Also remove stop tokens that appear anywhere (some models put them mid-response)
            foreach (var stopToken in allStopTokens.OrderByDescending(t => t.Length))
            {
                cleaned = cleaned.Replace(stopToken, "", StringComparison.OrdinalIgnoreCase);
            }

            return cleaned.Trim();
        }

        private int EstimateTokens(string text)
        {
            return (int)(text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length * 1.3);
        }

        public void Dispose()
        {
            foreach (var process in _runningProcesses.Values)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        // Try graceful shutdown first, then force kill
                        try
                        {
                            process.CloseMainWindow();
                            if (!process.WaitForExit(2000)) // 2 second grace period
                                process.Kill();
                        }
                        catch
                        {
                            process.Kill();
                        }
                    }
                    process.Dispose();
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
            _runningProcesses.Clear();
            _modelEndpoints.Clear();
            _httpClient?.Dispose();
        }
    }
}
