using System.Threading.Tasks;
using MnemoApp.Core.AI.Models;

namespace MnemoApp.Core.AI.Services
{
    /// <summary>
    /// Service for handling tokenization operations
    /// </summary>
    public interface ITokenizerService
    {
        /// <summary>
        /// Count tokens in text using a specific model's tokenizer
        /// </summary>
        Task<TokenCountResult> CountTokensAsync(string text, string modelName);

        /// <summary>
        /// Tokenize text into individual tokens
        /// </summary>
        Task<string[]> TokenizeAsync(string text, string modelName);

        /// <summary>
        /// Detokenize tokens back into text
        /// </summary>
        Task<string> DetokenizeAsync(string[] tokens, string modelName);

        /// <summary>
        /// Check if a tokenizer is available for a model
        /// </summary>
        Task<bool> IsTokenizerAvailableAsync(string modelName);

        /// <summary>
        /// Get the vocabulary size for a model's tokenizer
        /// </summary>
        Task<int> GetVocabSizeAsync(string modelName);
    }
}
