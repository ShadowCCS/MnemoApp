using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MnemoApp.Core.AI.Services.Tokenizers
{
    /// <summary>
    /// HuggingFace tokenizer implementation for .json files
    /// </summary>
    public class HuggingFaceTokenizer : BaseTokenizer
    {
        private readonly string _jsonPath;
        private readonly List<(string, string)> _merges = new();
        private readonly Dictionary<string, int> _mergeRanks = new();
        private readonly Dictionary<string, float> _scores = new();
        private readonly Dictionary<string, string> _specialTokensMap = new();
        private string _preTokenizerType = "ByteLevel";

        public HuggingFaceTokenizer(string jsonPath)
        {
            _jsonPath = jsonPath ?? throw new ArgumentNullException(nameof(jsonPath));
        }

        public async Task<bool> LoadAsync()
        {
            try
            {
                if (!File.Exists(_jsonPath))
                {
                    System.Diagnostics.Debug.WriteLine($"HuggingFace tokenizer file not found: {_jsonPath}");
                    return false;
                }

                var jsonContent = await File.ReadAllTextAsync(_jsonPath);
                var tokenizerData = JsonSerializer.Deserialize<JsonElement>(jsonContent);

                await ParseTokenizerJson(tokenizerData);
                _isLoaded = true;
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load HuggingFace tokenizer: {ex.Message}");
                return false;
            }
        }

        private async Task ParseTokenizerJson(JsonElement root)
        {
            var tokenToId = new Dictionary<string, int>();

            // Parse model section
            if (root.TryGetProperty("model", out var modelElement))
            {
                // Parse vocab
                if (modelElement.TryGetProperty("vocab", out var vocabElement))
                {
                    foreach (var kvp in vocabElement.EnumerateObject())
                    {
                        if (kvp.Value.TryGetInt32(out var id))
                        {
                            tokenToId[kvp.Name] = id;
                        }
                    }
                }

                // Parse merges - properly extract pairs
                if (modelElement.TryGetProperty("merges", out var mergesElement))
                {
                    int rank = 0;
                    foreach (var merge in mergesElement.EnumerateArray())
                    {
                        if (merge.ValueKind == JsonValueKind.String)
                        {
                            var mergeStr = merge.GetString();
                            if (!string.IsNullOrEmpty(mergeStr))
                            {
                                var parts = mergeStr.Split(' ');
                                if (parts.Length == 2)
                                {
                                    _merges.Add((parts[0], parts[1]));
                                    _mergeRanks[mergeStr] = rank++;
                                }
                            }
                        }
                    }
                }

                // Parse scores
                if (modelElement.TryGetProperty("scores", out var scoresElement))
                {
                    foreach (var kvp in scoresElement.EnumerateObject())
                    {
                        if (kvp.Value.TryGetSingle(out var score))
                        {
                            _scores[kvp.Name] = score;
                        }
                    }
                }
                
                // Check model type
                if (modelElement.TryGetProperty("type", out var typeElement) &&
                    typeElement.ValueKind == JsonValueKind.String)
                {
                    var modelType = typeElement.GetString();
                    System.Diagnostics.Debug.WriteLine($"Model type: {modelType}");
                }
            }

            // Parse pre_tokenizer section
            if (root.TryGetProperty("pre_tokenizer", out var preTokenizerElement))
            {
                if (preTokenizerElement.TryGetProperty("type", out var typeElement) &&
                    typeElement.ValueKind == JsonValueKind.String)
                {
                    _preTokenizerType = typeElement.GetString() ?? "ByteLevel";
                }
            }

            // Parse added_tokens (special tokens)
            if (root.TryGetProperty("added_tokens", out var addedTokensElement))
            {
                foreach (var token in addedTokensElement.EnumerateArray())
                {
                    if (token.TryGetProperty("content", out var contentElement) &&
                        contentElement.ValueKind == JsonValueKind.String)
                    {
                        var content = contentElement.GetString();
                        if (!string.IsNullOrEmpty(content))
                        {
                            _specialTokensMap[content] = content;
                        }
                    }
                }
            }

            InitializeVocabulary(tokenToId);
            AddSpecialTokens(_specialTokensMap.Keys.ToArray());
            
            await Task.CompletedTask;
        }

        public override async Task<string[]> TokenizeAsync(string text)
        {
            if (!_isLoaded)
                throw new InvalidOperationException("Tokenizer not loaded");

            if (string.IsNullOrEmpty(text))
                return Array.Empty<string>();

            var tokens = new List<string>();
            
            // Pre-tokenization (split on whitespace and punctuation for simplicity)
            var words = PreTokenize(text);

            foreach (var word in words)
            {
                if (_tokenToId.ContainsKey(word))
                {
                    tokens.Add(word);
                }
                else
                {
                    // Apply BPE to the word
                    var bpeTokens = ApplyBPE(word);
                    tokens.AddRange(bpeTokens);
                }
            }

            return await Task.FromResult(tokens.ToArray());
        }

        private List<string> PreTokenize(string text)
        {
            var words = new List<string>();
            
            if (_preTokenizerType == "ByteLevel")
            {
                // ByteLevel pre-tokenization: convert to bytes and add Ġ for spaces
                var currentWord = new StringBuilder();
                
                foreach (char c in text)
                {
                    if (c == ' ')
                    {
                        if (currentWord.Length > 0)
                        {
                            words.Add(currentWord.ToString());
                            currentWord.Clear();
                        }
                        currentWord.Append('Ġ'); // Ġ represents space in GPT-2 tokenizer
                    }
                    else
                    {
                        currentWord.Append(c);
                    }
                }
                
                if (currentWord.Length > 0)
                {
                    words.Add(currentWord.ToString());
                }
            }
            else
            {
                // Simple whitespace split
                words.AddRange(text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            }
            
            return words;
        }

        private List<string> ApplyBPE(string word)
        {
            if (string.IsNullOrEmpty(word))
                return new List<string>();

            // Start with character-level tokens
            var tokens = new List<string>();
            foreach (char c in word)
            {
                tokens.Add(c.ToString());
            }

            // Iteratively apply merges based on rank
            while (tokens.Count > 1)
            {
                var bestMerge = -1;
                var bestRank = int.MaxValue;
                var bestPos = -1;

                // Find the best merge to apply
                for (int i = 0; i < tokens.Count - 1; i++)
                {
                    var pair = $"{tokens[i]} {tokens[i + 1]}";
                    
                    if (_mergeRanks.TryGetValue(pair, out var rank) && rank < bestRank)
                    {
                        bestRank = rank;
                        bestPos = i;
                        bestMerge = rank;
                    }
                }

                // If no merge found, we're done
                if (bestMerge == -1)
                    break;

                // Apply the merge
                var merged = tokens[bestPos] + tokens[bestPos + 1];
                tokens[bestPos] = merged;
                tokens.RemoveAt(bestPos + 1);
            }

            // Map tokens to vocab, using unknown token if not found
            var result = new List<string>();
            foreach (var token in tokens)
            {
                if (_tokenToId.ContainsKey(token))
                {
                    result.Add(token);
                }
                else
                {
                    // Try byte-level encoding
                    foreach (char c in token)
                    {
                        var byteToken = $"<0x{(int)c:X2}>";
                        if (_tokenToId.ContainsKey(byteToken))
                        {
                            result.Add(byteToken);
                        }
                        else
                        {
                            result.Add(c.ToString());
                        }
                    }
                }
            }

            return result;
        }

        public override async Task<int[]> TokenizeToIdsAsync(string text)
        {
            var tokens = await TokenizeAsync(text);
            var ids = new List<int>();

            foreach (var token in tokens)
            {
                var id = GetTokenId(token);
                if (id == -1)
                {
                    // Use UNK token ID
                    var unkId = GetTokenId("<unk>");
                    if (unkId != -1)
                    {
                        ids.Add(unkId);
                    }
                }
                else
                {
                    ids.Add(id);
                }
            }

            return await Task.FromResult(ids.ToArray());
        }

        public override async Task<string> DetokenizeAsync(string[] tokens)
        {
            if (!_isLoaded)
                throw new InvalidOperationException("Tokenizer not loaded");

            if (tokens == null || tokens.Length == 0)
                return string.Empty;

            var result = new StringBuilder();
            for (int i = 0; i < tokens.Length; i++)
            {
                var token = tokens[i];
                
                // Handle special tokens
                if (IsSpecialToken(token))
                {
                    if (token == "<s>" || token == "</s>")
                        continue; // Skip sentence markers
                    if (token == "<pad>")
                        continue; // Skip padding
                }
                else
                {
                    result.Append(token);
                }
            }

            // Convert Ġ back to spaces (GPT-2 style)
            var text = result.ToString().Replace('Ġ', ' ');
            
            return await Task.FromResult(text);
        }

        public override async Task<string> DetokenizeAsync(int[] tokenIds)
        {
            if (!_isLoaded)
                throw new InvalidOperationException("Tokenizer not loaded");

            if (tokenIds == null || tokenIds.Length == 0)
                return string.Empty;

            var tokens = new string[tokenIds.Length];
            for (int i = 0; i < tokenIds.Length; i++)
            {
                var token = GetTokenString(tokenIds[i]);
                tokens[i] = token ?? "<unk>";
            }

            return await DetokenizeAsync(tokens);
        }

        public override int GetVocabularySize()
        {
            return _tokenToId.Count;
        }
    }
}


