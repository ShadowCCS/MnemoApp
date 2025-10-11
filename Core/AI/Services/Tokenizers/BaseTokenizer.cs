using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MnemoApp.Core.AI.Services.Tokenizers
{
    /// <summary>
    /// Base implementation for tokenizers
    /// </summary>
    public abstract class BaseTokenizer : ITokenizer
    {
        protected bool _isLoaded = false;
        protected Dictionary<string, int> _tokenToId = new();
        protected Dictionary<int, string> _idToToken = new();
        protected HashSet<string> _specialTokens = new();

        public bool IsLoaded => _isLoaded;

        public abstract Task<string[]> TokenizeAsync(string text);
        public abstract Task<int[]> TokenizeToIdsAsync(string text);
        public abstract Task<string> DetokenizeAsync(string[] tokens);
        public abstract Task<string> DetokenizeAsync(int[] tokenIds);
        public abstract int GetVocabularySize();

        /// <summary>
        /// Initialize the tokenizer with vocabulary data
        /// </summary>
        protected virtual void InitializeVocabulary(Dictionary<string, int> tokenToId)
        {
            _tokenToId = tokenToId ?? new Dictionary<string, int>();
            _idToToken = _tokenToId.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);
            _isLoaded = true;
        }

        /// <summary>
        /// Add special tokens to the vocabulary
        /// </summary>
        protected virtual void AddSpecialTokens(params string[] tokens)
        {
            foreach (var token in tokens)
            {
                if (!string.IsNullOrEmpty(token))
                {
                    _specialTokens.Add(token);
                }
            }
        }

        /// <summary>
        /// Get token ID, returning -1 if not found
        /// </summary>
        protected virtual int GetTokenId(string token)
        {
            return _tokenToId.TryGetValue(token, out var id) ? id : -1;
        }

        /// <summary>
        /// Get token string from ID, returning null if not found
        /// </summary>
        protected virtual string? GetTokenString(int id)
        {
            return _idToToken.TryGetValue(id, out var token) ? token : null;
        }

        /// <summary>
        /// Check if a token is a special token
        /// </summary>
        protected virtual bool IsSpecialToken(string token)
        {
            return _specialTokens.Contains(token);
        }
    }
}

