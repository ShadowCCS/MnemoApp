using Avalonia;
using Avalonia.Media;
using SkiaSharp;
using System;
using System.Collections.Generic;

namespace MnemoApp.Core.LaTeX.Metrics;

public class FontMetrics
{
    private readonly Typeface _typeface;
    private readonly Dictionary<(string, double), (double, double, double)> _measurementCache = new();
    private readonly SKTypeface? _skTypeface;
    private const double EmSize = 16.0; // Base font size

    public FontMetrics()
    {
        // Try to load STIX Two Math from embedded resources
        try
        {
            var fontFamily = new FontFamily("avares://MnemoApp/UI/Themes/Fonts/STIXTwoMath/STIXTwoMath-Regular.ttf#STIX Two Math");
            _typeface = new Typeface(fontFamily);
        }
        catch
        {
            // Fallback to system font
            _typeface = new Typeface("STIX Two Math");
        }
        
        // Try to get SKTypeface for more accurate measurements
        try
        {
            _skTypeface = SKTypeface.FromFamilyName("STIX Two Math", SKFontStyle.Normal);
        }
        catch
        {
            _skTypeface = null;
        }
    }

    public (double width, double height, double depth) MeasureChar(string character, double fontSize)
    {
        // Check cache first
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

        var width = formattedText.Width;
        
        // Use SkiaSharp for more accurate height/depth measurements if available
        double height;
        double depth;
        
        if (_skTypeface != null)
        {
            using var font = new SKFont(_skTypeface, (float)fontSize);
            
            var bounds = new SKRect();
            font.MeasureText(character, out bounds);
            
            // Convert Skia metrics to our coordinate system
            height = Math.Max(-bounds.Top, fontSize * 0.7); // Distance from baseline to top
            depth = Math.Max(bounds.Bottom, 0); // Distance from baseline down
            
            // Adjust for known character classes
            if ("gjpqy".Contains(character.ToLower()))
            {
                depth = Math.Max(depth, fontSize * 0.25);
            }
            else if ("()[]{}".Contains(character))
            {
                depth = Math.Max(depth, fontSize * 0.15);
            }
        }
        else
        {
            // Fallback to approximations if SkiaSharp not available
            height = formattedText.Height * 0.7;
            
            depth = 0.0;
            if ("gjpqy".Contains(character.ToLower()))
                depth = fontSize * 0.25;
            else if ("()[]{}".Contains(character))
                depth = fontSize * 0.15;
            else if (character.Length == 1 && char.IsUpper(character[0]))
                depth = 0;
            else
                depth = fontSize * 0.05; // Slight depth for most chars
        }

        var result = (width, height, depth);
        _measurementCache[cacheKey] = result;
        return result;
    }

    public double GetScriptSize(double fontSize)
    {
        return fontSize * 0.7;
    }

    public double GetScriptShiftUp(double fontSize)
    {
        return fontSize * 0.5;
    }

    public double GetScriptShiftDown(double fontSize)
    {
        return fontSize * 0.3;
    }

    public double GetFractionRuleThickness(double fontSize)
    {
        return fontSize * 0.08;
    }

    public double GetFractionNumeratorShift(double fontSize)
    {
        return fontSize * 0.65;
    }

    public double GetFractionDenominatorShift(double fontSize)
    {
        return fontSize * 0.05;
    }

    public double GetAxisHeight(double fontSize)
    {
        return fontSize * 0.28;
    }

    public void ClearCache()
    {
        _measurementCache.Clear();
    }
}

