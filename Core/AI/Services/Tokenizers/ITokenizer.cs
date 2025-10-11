using System.Collections.Generic;
using System.Threading.Tasks;

namespace MnemoApp.Core.AI.Services.Tokenizers
{
    /// <summary>
    /// Interface for tokenization operations
    /// </summary>
    public interface ITokenizer
    {
        /// <summary>
        /// Tokenize text into individual tokens
        /// </summary>
        Task<string[]> TokenizeAsync(string text);

        /// <summary>
        /// Tokenize text into token IDs
        /// </summary>
        Task<int[]> TokenizeToIdsAsync(string text);

        /// <summary>
        /// Detokenize tokens back into text
        /// </summary>
        Task<string> DetokenizeAsync(string[] tokens);

        /// <summary>
        /// Detokenize token IDs back into text
        /// </summary>
        Task<string> DetokenizeAsync(int[] tokenIds);

        /// <summary>
        /// Get the vocabulary size
        /// </summary>
        int GetVocabularySize();

        /// <summary>
        /// Check if the tokenizer is properly loaded
        /// </summary>
        bool IsLoaded { get; }
    }
}

