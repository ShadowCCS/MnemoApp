using System;
using System.IO;
using System.Threading.Tasks;

namespace MnemoApp.Core.AI.Services.Tokenizers
{
    /// <summary>
    /// Factory for creating tokenizers based on file type
    /// </summary>
    public static class TokenizerFactory
    {
        /// <summary>
        /// Create a tokenizer based on the file path
        /// </summary>
        public static async Task<ITokenizer?> CreateTokenizerAsync(string tokenizerPath)
        {
            if (string.IsNullOrEmpty(tokenizerPath) || !File.Exists(tokenizerPath))
            {
                return null;
            }

            try
            {
                if (tokenizerPath.EndsWith(".model", StringComparison.OrdinalIgnoreCase))
                {
                    var tokenizer = new SentencePieceTokenizer(tokenizerPath);
                    var loaded = await tokenizer.LoadAsync();
                    return loaded ? tokenizer : null;
                }
                else if (tokenizerPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    var tokenizer = new HuggingFaceTokenizer(tokenizerPath);
                    var loaded = await tokenizer.LoadAsync();
                    return loaded ? tokenizer : null;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Unsupported tokenizer file format: {tokenizerPath}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create tokenizer from {tokenizerPath}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get the appropriate tokenizer file path for a model directory
        /// </summary>
        public static string? GetTokenizerPath(string modelDirectoryPath)
        {
            if (string.IsNullOrEmpty(modelDirectoryPath) || !Directory.Exists(modelDirectoryPath))
            {
                return null;
            }

            // Check for .model file first (SentencePiece format)
            var tokenizerModelPath = Path.Combine(modelDirectoryPath, "tokenizer.model");
            if (File.Exists(tokenizerModelPath))
            {
                return tokenizerModelPath;
            }

            // Check for .json file as fallback (HuggingFace format)
            var tokenizerJsonPath = Path.Combine(modelDirectoryPath, "tokenizer.json");
            if (File.Exists(tokenizerJsonPath))
            {
                return tokenizerJsonPath;
            }

            return null;
        }
    }
}

