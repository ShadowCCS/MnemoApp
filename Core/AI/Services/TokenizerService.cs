using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MnemoApp.Core.AI.Models;
using MnemoApp.Core.AI.Services.Tokenizers;

namespace MnemoApp.Core.AI.Services
{
    /// <summary>
    /// Implementation of tokenizer service for handling tokenization operations
    /// Uses custom tokenizers for real tokenization with tokenizer.model and tokenizer.json
    /// </summary>
    public class TokenizerService : ITokenizerService
    {
        private readonly IAIService _aiService;
        private readonly Dictionary<string, ITokenizer> _tokenizerCache = new();
        private readonly object _cacheLock = new();

        public TokenizerService(IAIService aiService)
        {
            _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        }

        public async Task<TokenCountResult> CountTokensAsync(string text, string modelName)
        {
            try
            {
                var tokenizer = await GetTokenizerAsync(modelName);
                if (tokenizer == null)
                {
                    // Fallback to estimation if tokenizer can't be loaded
                    var estimatedCount = EstimateTokenCount(text);
                    System.Diagnostics.Debug.WriteLine($"Tokenizer not available for model '{modelName}'");
                    return new TokenCountResult
                    {
                        Success = true,
                        TokenCount = estimatedCount,
                        IsEstimate = true
                    };
                }

                var tokens = await tokenizer.TokenizeAsync(text);
                var tokenIds = await tokenizer.TokenizeToIdsAsync(text);
                
                System.Diagnostics.Debug.WriteLine($"Tokenized '{text.Substring(0, Math.Min(50, text.Length))}...' into {tokenIds.Length} tokens");
                System.Diagnostics.Debug.WriteLine($"Tokens: [{string.Join(", ", tokens.Take(20).Select(t => $"\"{t}\""))}]");
                
                return new TokenCountResult
                {
                    Success = true,
                    TokenCount = tokenIds.Length,
                    IsEstimate = false
                };
            }
            catch (Exception ex)
            {
                return new TokenCountResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<string[]> TokenizeAsync(string text, string modelName)
        {
            var tokenizer = await GetTokenizerAsync(modelName);
            if (tokenizer == null)
                throw new InvalidOperationException($"Tokenizer not available for model '{modelName}'");

            return await tokenizer.TokenizeAsync(text);
        }

        public async Task<string> DetokenizeAsync(string[] tokens, string modelName)
        {
            var tokenizer = await GetTokenizerAsync(modelName);
            if (tokenizer == null)
                throw new InvalidOperationException($"Tokenizer not available for model '{modelName}'");

            return await tokenizer.DetokenizeAsync(tokens);
        }

        public async Task<bool> IsTokenizerAvailableAsync(string modelName)
        {
            var model = await _aiService.GetModelAsync(modelName);
            return model?.HasTokenizer ?? false;
        }

        public async Task<int> GetVocabSizeAsync(string modelName)
        {
            var tokenizer = await GetTokenizerAsync(modelName);
            if (tokenizer == null)
                throw new InvalidOperationException($"Tokenizer not available for model '{modelName}'");

            return tokenizer.GetVocabularySize();
        }

        /// <summary>
        /// Estimate token count using heuristics
        /// </summary>
        private int EstimateTokenCount(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            // Basic heuristic: words * 1.3 to account for subword tokenization
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            var estimatedTokens = (int)(words * 1.3);

            // Account for punctuation and special characters
            var punctuationCount = 0;
            foreach (char c in text)
            {
                if (char.IsPunctuation(c) || char.IsSymbol(c))
                    punctuationCount++;
            }

            // Add tokens for punctuation (many punctuation marks become separate tokens)
            estimatedTokens += punctuationCount / 2;

            return Math.Max(1, estimatedTokens);
        }

        /// <summary>
        /// Get or load a tokenizer for the specified model
        /// </summary>
        private async Task<ITokenizer?> GetTokenizerAsync(string modelName)
        {
            // Check cache first
            lock (_cacheLock)
            {
                if (_tokenizerCache.TryGetValue(modelName, out var cachedTokenizer))
                {
                    System.Diagnostics.Debug.WriteLine($"Tokenizer cache hit for model '{modelName}'");
                    return cachedTokenizer;
                }
            }

            // Load model info
            var model = await _aiService.GetModelAsync(modelName);
            if (model == null)
            {
                System.Diagnostics.Debug.WriteLine($"Model '{modelName}' not found");
                return null;
            }

            if (!model.HasTokenizer)
            {
                System.Diagnostics.Debug.WriteLine($"Model '{modelName}' HasTokenizer=false (DirectoryPath: '{model.DirectoryPath}')");
                return null;
            }

            var tokenizerPath = TokenizerFactory.GetTokenizerPath(model.DirectoryPath);
            if (tokenizerPath == null)
            {
                System.Diagnostics.Debug.WriteLine($"No tokenizer file found in '{model.DirectoryPath}'");
                return null;
            }

            System.Diagnostics.Debug.WriteLine($"Found tokenizer file at '{tokenizerPath}' for model '{modelName}'");

            try
            {
                var tokenizer = await TokenizerFactory.CreateTokenizerAsync(tokenizerPath);
                if (tokenizer == null)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to create tokenizer for model '{modelName}' from '{tokenizerPath}'");
                    return null;
                }

                // Cache the tokenizer
                lock (_cacheLock)
                {
                    _tokenizerCache[modelName] = tokenizer;
                }

                System.Diagnostics.Debug.WriteLine($"Successfully loaded tokenizer for model '{modelName}' from '{tokenizerPath}'");
                return tokenizer;
            }
            catch (Exception ex)
            {
                // If loading fails, log and return null to fallback to estimation
                System.Diagnostics.Debug.WriteLine($"Failed to load tokenizer for model '{modelName}' from '{tokenizerPath}': {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Exception details: {ex}");
                return null;
            }
        }

    }
}
