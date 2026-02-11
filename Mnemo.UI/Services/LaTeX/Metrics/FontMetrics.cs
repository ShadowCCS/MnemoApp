using Avalonia;
using Avalonia.Media;
using SkiaSharp;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Mnemo.UI.Services.LaTeX.Metrics;

public class FontMetrics
{
    private static FontMetrics? _instance;
    private static readonly object _lock = new();

    private readonly Typeface _typeface;
    private readonly ConcurrentDictionary<(string, double), (double, double, double)> _measurementCache = new();
    private readonly SKTypeface? _skTypeface;

    public static FontMetrics Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new FontMetrics();
                }
            }
            return _instance;
        }
    }

    private FontMetrics()
    {
        var fontFamily = ResolveFontFamily();
        _typeface = new Typeface(fontFamily);
        _skTypeface = CreateSkiaTypeface(fontFamily);
    }

    private static FontFamily ResolveFontFamily()
    {
        // Try to get font from resources first
        if (Application.Current?.TryGetResource("MathFontFamily", null, out var res) == true && res is FontFamily ff)
        {
            return ff;
        }

        // Try embedded font
        try
        {
            return new FontFamily("avares://Mnemo.UI/Themes/Fonts/STIXTwoMath/STIXTwoMath-Regular.ttf#STIX Two Math");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"FontMetrics: Failed to load embedded font, falling back to system font. Error: {ex.Message}");
            return new FontFamily("STIX Two Math");
        }
    }

    private static SKTypeface? CreateSkiaTypeface(FontFamily fontFamily)
    {
        try
        {
            return SKTypeface.FromFamilyName(fontFamily.Name, SKFontStyle.Normal);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"FontMetrics: Failed to create SKTypeface. Error: {ex.Message}");
            return null;
        }
    }

    public Typeface Typeface => _typeface;

    public (double width, double height, double depth) MeasureChar(string character, double fontSize)
    {
        var cacheKey = (character, fontSize);
        if (_measurementCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var formattedText = new FormattedText(
            character,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            _typeface,
            fontSize,
            Brushes.Black
        );

        // Avalonia's FormattedText metrics
        var width = formattedText.Width;
        var height = formattedText.Baseline;
        var depth = formattedText.Height - formattedText.Baseline;
        
        // Use Skia for more accurate ink bounds if available
        if (_skTypeface != null)
        {
            using var font = new SKFont(_skTypeface, (float)fontSize);
            font.MeasureText(character, out var bounds);
            
            // For math symbols, we often need more space than the standard line height
            // bounds.Top is negative (distance from baseline up)
            // bounds.Bottom is positive (distance from baseline down)
            
            // Add safety margin
            const double skiaSafetyMargin = 4.0;
            
            height = Math.Max(height, -bounds.Top + skiaSafetyMargin);
            depth = Math.Max(depth, bounds.Bottom + skiaSafetyMargin);
            width = Math.Max(width, bounds.Right + skiaSafetyMargin);
        }
        else
        {
             // Fallback buffer if Skia unavailable
             height += 2.0;
             depth += 2.0;
             width += 2.0;
        }

        // Add a global safety margin to every character to prevent tight cropping
        const double safetyMargin = 4.0;
        var result = (width + safetyMargin, height + safetyMargin, depth + safetyMargin);
        
        _measurementCache.TryAdd(cacheKey, result);
        return result;
    }

    public double GetScriptSize(double fontSize) => fontSize * 0.7;
    public double GetScriptShiftUp(double fontSize) => fontSize * 0.5;
    public double GetScriptShiftDown(double fontSize) => fontSize * 0.3;
    public double GetFractionRuleThickness(double fontSize) => fontSize * 0.08;
    public double GetFractionNumeratorShift(double fontSize) => fontSize * 0.65;
    public double GetFractionDenominatorShift(double fontSize) => fontSize * 0.05;
    public double GetAxisHeight(double fontSize) => fontSize * 0.28;
    public double GetSqrtSymbolWidth(double fontSize) => fontSize * 0.75;
    public double GetSqrtPadding(double fontSize) => fontSize * 0.25;
    public double GetSqrtRuleThickness(double fontSize) => fontSize * 0.08;

    public void ClearCache()
    {
        _measurementCache.Clear();
    }
}