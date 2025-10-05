using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using MnemoApp.Core.MnemoAPI;

namespace MnemoApp.Core.Services
{
    public class LocalizationService : ILocalizationService
    {
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentDictionary<string, string>>> _dict
            = new(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _loadLock = new(1, 1);
        private readonly ConcurrentDictionary<string, LanguageManifest> _manifests = new(StringComparer.OrdinalIgnoreCase);

        public string CurrentLanguage { get; private set; } = "en";

        public event EventHandler<string>? LanguageChanged;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            await _loadLock.WaitAsync(cancellationToken);
            try
            {
                // Load core languages embedded in resources if present
                LoadPackagedCoreLanguages();

                // Initial language is set by ApplicationHost after loading settings
                // No need to access ApplicationHost.Services here

                // Ensure English exists as baseline
                if (!_dict.ContainsKey("en"))
                {
                    _dict["en"] = new(StringComparer.OrdinalIgnoreCase);
                }
            }
            finally
            {
                _loadLock.Release();
            }
        }

        public async Task<IReadOnlyList<LanguageManifest>> GetAvailableLanguagesAsync(CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            return _manifests.Values.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public async Task<bool> SetLanguageAsync(string languageCode, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(languageCode)) return false;
            languageCode = languageCode.Trim();

            // Check if the language exists and has data loaded
            if (!_dict.ContainsKey(languageCode) || !HasLanguageData(languageCode))
            {
                // Try to load the language from packaged resources
                LoadPackagedCoreLanguages();
                
                // If still not available, create empty dictionary to allow extensions to populate it later
                if (!_dict.ContainsKey(languageCode))
                {
                    _dict.TryAdd(languageCode, new(StringComparer.OrdinalIgnoreCase));
                }
            }

            if (string.Equals(CurrentLanguage, languageCode, StringComparison.OrdinalIgnoreCase))
                return true;

            CurrentLanguage = languageCode;
            // Language preference is saved by ApplicationHost after language change event

            LanguageChanged?.Invoke(this, CurrentLanguage);
            return await Task.FromResult(true);
        }

        private bool HasLanguageData(string languageCode)
        {
            return _dict.TryGetValue(languageCode, out var lang) && lang.Count > 0;
        }

        public string T(string @namespace, string key, IReadOnlyDictionary<string, string>? parameters = null)
        {
            if (string.IsNullOrEmpty(@namespace) || string.IsNullOrEmpty(key))
                return key;

            var value = LookupInternal(CurrentLanguage, @namespace, key)
                        ?? LookupInternal("en", @namespace, key)
                        ?? key;

            if (parameters != null && parameters.Count > 0)
            {
                foreach (var kvp in parameters)
                {
                    value = value.Replace("{" + kvp.Key + "}", kvp.Value);
                }
            }

            return value;
        }

        public LanguageManifest? GetCurrentLanguageManifest()
        {
            _manifests.TryGetValue(CurrentLanguage, out var m);
            return m;
        }

        public void RegisterNamespace(string languageCode, string @namespace, IReadOnlyDictionary<string, string> entries)
        {
            if (string.IsNullOrWhiteSpace(languageCode) || string.IsNullOrWhiteSpace(@namespace) || entries == null)
                return;

            var lang = _dict.GetOrAdd(languageCode, _ => new(StringComparer.OrdinalIgnoreCase));
            var ns = lang.GetOrAdd(@namespace, _ => new(StringComparer.OrdinalIgnoreCase));
            foreach (var kvp in entries)
            {
                ns[kvp.Key] = kvp.Value;
            }
        }

        public void RegisterLanguageJson(string languageCode, string jsonContent)
        {
            if (string.IsNullOrWhiteSpace(languageCode) || string.IsNullOrWhiteSpace(jsonContent))
                return;

            try
            {
                using var doc = JsonDocument.Parse(jsonContent, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return;

                foreach (var nsProp in doc.RootElement.EnumerateObject())
                {
                    if (nsProp.Value.ValueKind != JsonValueKind.Object) continue;
                    var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var entry in nsProp.Value.EnumerateObject())
                    {
                        if (entry.Value.ValueKind == JsonValueKind.String)
                        {
                            entries[entry.Name] = entry.Value.GetString() ?? string.Empty;
                        }
                    }
                    RegisterNamespace(languageCode, nsProp.Name, entries);
                }
            }
            catch
            {
                // ignore malformed JSON from extensions to avoid crashing host
            }
        }

        private string? LookupInternal(string languageCode, string @namespace, string key)
        {
            if (!_dict.TryGetValue(languageCode, out var lang)) return null;
            if (!lang.TryGetValue(@namespace, out var ns)) return null;
            if (!ns.TryGetValue(key, out var value)) return null;
            return value;
        }

        private void LoadPackagedCoreLanguages()
        {
            try
            {
                // Load from embedded Avalonia resources: avares://MnemoApp/UI/Languages/{code}/
                var baseUri = new Uri("avares://MnemoApp/");
                var languagesUri = new Uri("avares://MnemoApp/UI/Languages");

                // Get assets specifically under UI/Languages
                var allAssets = Avalonia.Platform.AssetLoader.GetAssets(baseUri, languagesUri)
                    .Where(u => u.AbsolutePath.StartsWith("/UI/Languages/", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // Enumerate all assets under UI/Languages and find manifest.json files (exclude themes)
                var manifestUris = allAssets
                    .Where(u => u.AbsolutePath.EndsWith("/manifest.json", StringComparison.OrdinalIgnoreCase))
                    .Where(u => u.AbsolutePath.Contains("/UI/Languages/", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var manifestUri in manifestUris)
                {
                    // manifestUri example: avares://MnemoApp/UI/Languages/Core/en/manifest.json
                    var segments = manifestUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    if (segments.Length < 3) continue; // UI, Languages, ..., {code}, manifest.json
                    var code = segments[^2];

                    // Get the directory where the manifest was found
                    var manifestDir = manifestUri.AbsolutePath.Substring(0, manifestUri.AbsolutePath.LastIndexOf('/'));

                    // Load translations if present - check in the same directory as manifest
                    var langJsonUri = new Uri($"avares://MnemoApp{manifestDir}/language.json");
                    
                    if (Avalonia.Platform.AssetLoader.Exists(langJsonUri))
                    {
                        using var s = Avalonia.Platform.AssetLoader.Open(langJsonUri);
                        using var sr = new StreamReader(s);
                        var json = sr.ReadToEnd();
                        RegisterLanguageJson(code, json);
                    }

                    // Load manifest
                    try
                    {
                        using var ms = Avalonia.Platform.AssetLoader.Open(manifestUri);
                        using var mr = new StreamReader(ms);
                        var manifestJson = mr.ReadToEnd();
                        var m = JsonSerializer.Deserialize<LanguageManifest>(manifestJson, JsonOptions);
                        if (m != null)
                        {
                            m.Code = string.IsNullOrWhiteSpace(m.Code) ? code : m.Code;
                            m.IsCore = true;
                            // Resolve flag path: prefer manifest Flag if present, else default to local flag.svg
                            string? resolvedFlag = null;
                            if (!string.IsNullOrWhiteSpace(m.Flag))
                            {
                                // If manifest flag is relative, make it relative to the actual manifest directory; if absolute avares, use as-is
                                if (Uri.TryCreate(m.Flag, UriKind.Absolute, out var abs) && abs.Scheme == "avares")
                                {
                                    resolvedFlag = abs.ToString();
                                }
                                else
                                {
                                    var candidate = new Uri($"avares://MnemoApp{manifestDir}/{m.Flag.TrimStart('/')}");
                                    if (Avalonia.Platform.AssetLoader.Exists(candidate))
                                        resolvedFlag = candidate.ToString();
                                }
                            }
                            if (resolvedFlag == null)
                            {
                                var fallback = new Uri($"avares://MnemoApp{manifestDir}/flag.svg");
                                if (Avalonia.Platform.AssetLoader.Exists(fallback))
                                    resolvedFlag = fallback.ToString();
                            }
                            m.IconPath = resolvedFlag;
                            _manifests[m.Code] = m;
                        }
                    }
                    catch { /* ignore malformed manifest */ }
                }

                // Also consider languages that may have language.json but no manifest.json
                var langUris = Avalonia.Platform.AssetLoader.GetAssets(baseUri, languagesUri)
                    .Where(u => u.AbsolutePath.EndsWith("/language.json", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                foreach (var langUri in langUris)
                {
                    var segments = langUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    if (segments.Length < 3) continue;
                    var code = segments[^2];

                    if (!_manifests.ContainsKey(code))
                    {
                        using var s = Avalonia.Platform.AssetLoader.Open(langUri);
                        using var sr = new StreamReader(s);
                        var json = sr.ReadToEnd();
                        RegisterLanguageJson(code, json);

                        var flagUri = new Uri($"avares://MnemoApp/UI/Languages/{code}/flag.svg");
                        _manifests[code] = new LanguageManifest
                        {
                            Code = code,
                            Name = code.ToUpperInvariant(),
                            NativeName = code.ToUpperInvariant(),
                            IconPath = Avalonia.Platform.AssetLoader.Exists(flagUri) ? flagUri.ToString() : null,
                            IsCore = true
                        };
                    }
                }
            }
            catch
            {
                // ignore resource loading issues at startup
            }
        }
    }
}


