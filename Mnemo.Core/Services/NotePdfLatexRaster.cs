namespace Mnemo.Core.Services;

/// <summary>PNG bytes for a LaTeX formula plus its intended size in PDF points (from Avalonia DIP × 72/96).</summary>
public sealed record NotePdfLatexRaster(byte[] Png, float WidthPt, float HeightPt);
