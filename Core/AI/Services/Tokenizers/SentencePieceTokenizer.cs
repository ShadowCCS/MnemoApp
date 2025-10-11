using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Buffers.Binary;

namespace MnemoApp.Core.AI.Services.Tokenizers
{
    /// <summary>
    /// SentencePiece tokenizer implementation for .model files
    /// </summary>
    public class SentencePieceTokenizer : BaseTokenizer
    {
        private readonly string _modelPath;
        private readonly Dictionary<string, float> _scores = new();
        private readonly List<SentencePieceToken> _pieces = new();
        
        private class SentencePieceToken
        {
            public string Piece { get; set; } = "";
            public float Score { get; set; }
            public int Type { get; set; } // 1=normal, 2=unknown, 3=control
        }

        public SentencePieceTokenizer(string modelPath)
        {
            _modelPath = modelPath ?? throw new ArgumentNullException(nameof(modelPath));
        }

        public async Task<bool> LoadAsync()
        {
            try
            {
                if (!File.Exists(_modelPath))
                {
                    System.Diagnostics.Debug.WriteLine($"SentencePiece model file not found: {_modelPath}");
                    return false;
                }

                await ParseSentencePieceModel();
                _isLoaded = true;
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load SentencePiece model: {ex.Message}");
                return false;
            }
        }

        private async Task ParseSentencePieceModel()
        {
            // Parse the protobuf .model file
            var bytes = await File.ReadAllBytesAsync(_modelPath);
            
            var tokenToId = new Dictionary<string, int>();
            var specialTokensList = new List<string>();
            
            // Parse protobuf format - simplified parser for SentencePiece model
            int position = 0;
            int tokenId = 0;
            
            while (position < bytes.Length)
            {
                if (position + 2 > bytes.Length) break;
                
                // Read field tag (varint wire type)
                var tag = ReadVarint(bytes, ref position);
                var fieldNumber = tag >> 3;
                var wireType = tag & 0x7;
                
                if (fieldNumber == 1) // pieces field
                {
                    if (wireType == 2) // Length-delimited
                    {
                        var length = ReadVarint(bytes, ref position);
                        var endPos = position + length;
                        
                        var token = new SentencePieceToken();
                        
                        // Parse piece message
                        while (position < endPos && position < bytes.Length)
                        {
                            var innerTag = ReadVarint(bytes, ref position);
                            var innerField = innerTag >> 3;
                            var innerWire = innerTag & 0x7;
                            
                            if (innerField == 1 && innerWire == 2) // piece string
                            {
                                var strLen = ReadVarint(bytes, ref position);
                                if (position + strLen <= bytes.Length)
                                {
                                    token.Piece = Encoding.UTF8.GetString(bytes, position, strLen);
                                    position += strLen;
                                }
                                else
                                {
                                    position = bytes.Length; // Skip malformed data
                                }
                            }
                            else if (innerField == 2 && innerWire == 5) // score float
                            {
                                if (position + 4 <= bytes.Length)
                                {
                                    token.Score = BitConverter.ToSingle(bytes, position);
                                    position += 4;
                                }
                            }
                            else if (innerField == 3 && innerWire == 0) // type varint
                            {
                                token.Type = ReadVarint(bytes, ref position);
                            }
                            else
                            {
                                // Skip unknown field
                                SkipField(bytes, ref position, innerWire);
                            }
                        }
                        
                        if (!string.IsNullOrEmpty(token.Piece))
                        {
                            _pieces.Add(token);
                            tokenToId[token.Piece] = tokenId;
                            _scores[token.Piece] = token.Score;
                            
                            if (token.Type == 2 || token.Type == 3) // unknown or control
                            {
                                specialTokensList.Add(token.Piece);
                            }
                            
                            tokenId++;
                        }
                    }
                    else
                    {
                        SkipField(bytes, ref position, wireType);
                    }
                }
                else
                {
                    SkipField(bytes, ref position, wireType);
                }
            }
            
            // If no tokens were parsed, use fallback
            if (tokenToId.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("Warning: Failed to parse .model file, using fallback vocabulary");
                LoadFallbackVocabulary(tokenToId, specialTokensList);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"Successfully parsed {tokenToId.Count} tokens from .model file");
            }
            
            InitializeVocabulary(tokenToId);
            AddSpecialTokens(specialTokensList.ToArray());
        }

        private void LoadFallbackVocabulary(Dictionary<string, int> tokenToId, List<string> specialTokensList)
        {
            var commonTokens = new[]
            {
                "<unk>", "<s>", "</s>", "<pad>", "▁", ".", ",", "!", "?", ";", ":",
                "▁a", "▁an", "▁the", "▁and", "▁or", "▁but", "▁in", "▁on", "▁at", "▁to", "▁for", "▁of", "▁with", "▁by"
            };

            for (int i = 0; i < commonTokens.Length; i++)
            {
                tokenToId[commonTokens[i]] = i;
            }

            for (int i = 0; i < 256; i++)
            {
                var charToken = $"<0x{i:X2}>";
                tokenToId[charToken] = commonTokens.Length + i;
            }
            
            specialTokensList.AddRange(new[] { "<unk>", "<s>", "</s>", "<pad>" });
        }

        private int ReadVarint(byte[] bytes, ref int position)
        {
            int result = 0;
            int shift = 0;
            
            while (position < bytes.Length)
            {
                byte b = bytes[position++];
                result |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0) break;
                shift += 7;
            }
            
            return result;
        }

        private void SkipField(byte[] bytes, ref int position, int wireType)
        {
            switch (wireType)
            {
                case 0: // Varint
                    ReadVarint(bytes, ref position);
                    break;
                case 1: // 64-bit
                    if (position + 8 <= bytes.Length)
                        position += 8;
                    else
                        position = bytes.Length;
                    break;
                case 2: // Length-delimited
                    var length = ReadVarint(bytes, ref position);
                    if (position + length <= bytes.Length)
                        position += length;
                    else
                        position = bytes.Length;
                    break;
                case 5: // 32-bit
                    if (position + 4 <= bytes.Length)
                        position += 4;
                    else
                        position = bytes.Length;
                    break;
                default:
                    if (position < bytes.Length)
                        position++;
                    break;
            }
        }

        public override async Task<string[]> TokenizeAsync(string text)
        {
            if (!_isLoaded)
                throw new InvalidOperationException("Tokenizer not loaded");

            if (string.IsNullOrEmpty(text))
                return Array.Empty<string>();

            // SentencePiece uses longest-match tokenization
            var tokens = new List<string>();
            
            // Replace spaces with meta symbol ▁ (U+2581)
            // Note: Some models prepend ▁, some don't. Check if ▁ exists in vocab.
            text = text.Replace(" ", "▁");
            
            // Only prepend ▁ if it seems to be the convention (check if "▁" tokens exist)
            if (_tokenToId.ContainsKey("▁") && !text.StartsWith("▁"))
            {
                text = "▁" + text;
            }
            
            int position = 0;
            while (position < text.Length)
            {
                // Find the longest matching piece (greedy longest-match)
                string? bestMatch = null;
                
                // Try from longest to shortest - first match is the longest
                for (int length = Math.Min(text.Length - position, 128); length > 0; length--)
                {
                    var candidate = text.Substring(position, length);
                    
                    if (_tokenToId.ContainsKey(candidate))
                    {
                        bestMatch = candidate;
                        break; // First match is longest match
                    }
                }
                
                if (bestMatch != null)
                {
                    tokens.Add(bestMatch);
                    position += bestMatch.Length;
                }
                else
                {
                    // No match found, use byte-level encoding
                    var c = text[position];
                    
                    // For UTF-8, encode the byte sequence
                    var bytes = Encoding.UTF8.GetBytes(new[] { c });
                    bool handled = false;
                    
                    foreach (var b in bytes)
                    {
                        var byteToken = $"<0x{b:X2}>";
                        if (_tokenToId.ContainsKey(byteToken))
                        {
                            tokens.Add(byteToken);
                            handled = true;
                        }
                    }
                    
                    if (!handled)
                    {
                        // Fallback to <unk> token
                        if (_tokenToId.ContainsKey("<unk>"))
                        {
                            tokens.Add("<unk>");
                        }
                        else
                        {
                            tokens.Add(c.ToString());
                        }
                    }
                    
                    position++;
                }
            }

            return await Task.FromResult(tokens.ToArray());
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
                    // Handle unknown tokens by character-level encoding
                    foreach (char c in token)
                    {
                        var charToken = $"<0x{(int)c:X2}>";
                        var charId = GetTokenId(charToken);
                        if (charId != -1)
                        {
                            ids.Add(charId);
                        }
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

            // Replace meta symbol ▁ with spaces
            var text = result.ToString().Replace("▁", " ");
            
            // Trim leading space from initial meta symbol
            if (text.StartsWith(" "))
                text = text.Substring(1);

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

