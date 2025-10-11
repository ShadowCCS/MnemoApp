using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using MnemoApp.Core.LaTeX;
using MnemoApp.Core.LaTeX.Symbols;

namespace MnemoApp.Core.MnemoAPI
{
    /// <summary>
    /// LaTeX rendering and mathematical expression API
    /// </summary>
    public class LaTeXApi
    {
        public LaTeXApi()
        {
        }

        /// <summary>
        /// Render LaTeX expression to Avalonia Control (synchronous)
        /// </summary>
        /// <param name="latex">LaTeX expression string</param>
        /// <param name="fontSize">Font size in points (default 16.0)</param>
        /// <returns>Rendered control that can be added to UI</returns>
        public Control Render(string latex, double fontSize = 16.0)
        {
            return LaTeXEngine.Render(latex, fontSize);
        }

        /// <summary>
        /// Render LaTeX expression asynchronously (recommended for complex expressions)
        /// </summary>
        /// <param name="latex">LaTeX expression string</param>
        /// <param name="fontSize">Font size in points (default 16.0)</param>
        /// <returns>Task that resolves to rendered control</returns>
        public Task<Control> RenderAsync(string latex, double fontSize = 16.0)
        {
            return LaTeXEngine.RenderAsync(latex, fontSize);
        }

        /// <summary>
        /// Clear the LaTeX rendering cache (parse and layout caches)
        /// Useful for memory management or when many unique expressions are rendered
        /// </summary>
        public void ClearCache()
        {
            LaTeXEngine.ClearCache();
        }

        /// <summary>
        /// Check if a LaTeX expression is syntactically valid
        /// </summary>
        /// <param name="latex">LaTeX expression to validate</param>
        /// <returns>True if the expression can be parsed without errors</returns>
        public bool IsValid(string latex)
        {
            try
            {
                var lexer = new LaTeX.Parser.LaTeXLexer(latex);
                var tokens = lexer.Tokenize();
                var parser = new LaTeX.Parser.LaTeXParser(tokens);
                var ast = parser.Parse();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Get all available LaTeX symbol commands
        /// </summary>
        /// <returns>Array of symbol command names (e.g., "alpha", "beta", "sum", etc.)</returns>
        public string[] GetAvailableSymbols()
        {
            return SymbolRegistry.GetAllSymbolNames().ToArray();
        }

        /// <summary>
        /// Check if a specific symbol command is registered
        /// </summary>
        /// <param name="command">Symbol command name (without backslash)</param>
        /// <returns>True if the symbol is registered</returns>
        public bool HasSymbol(string command)
        {
            return SymbolRegistry.TryGetSymbol(command, out _);
        }

        /// <summary>
        /// Get the Unicode character for a LaTeX symbol command
        /// </summary>
        /// <param name="command">Symbol command name (without backslash)</param>
        /// <returns>Unicode string if symbol exists, null otherwise</returns>
        public string? GetSymbolUnicode(string command)
        {
            return SymbolRegistry.TryGetSymbol(command, out var unicode) ? unicode : null;
        }

        /// <summary>
        /// Register a custom LaTeX symbol
        /// </summary>
        /// <param name="command">Symbol command name (without backslash)</param>
        /// <param name="unicode">Unicode character or string to map to</param>
        public void RegisterCustomSymbol(string command, string unicode)
        {
            SymbolRegistry.RegisterSymbol(command, unicode);
        }

        /// <summary>
        /// Get a dictionary of all registered symbols (command -> unicode)
        /// </summary>
        /// <returns>Dictionary mapping command names to Unicode strings</returns>
        public System.Collections.Generic.IReadOnlyDictionary<string, string> GetAllSymbols()
        {
            return SymbolRegistry.GetAllSymbols();
        }
    }
}

