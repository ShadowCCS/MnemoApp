using Avalonia;
using Avalonia.Media;
using SkiaSharp;
using System;
using System.Collections.Generic;

namespace Mnemo.UI.Services.LaTeX.Metrics;

public class FontMetrics
{
    private static FontMetrics? _instance;
    private static readonly object _lock = new object();

    private readonly Typeface _typeface;
    private readonly Dictionary<(string, double), (double, double, double)> _measurementCache = new();
    private readonly SKTypeface? _skTypeface;

    public static FontMetrics Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = new FontMetrics();
                    }
                }
            }
            return _instance;
        }
    }

    private FontMetrics()
    {
        // Try to get font from resources first
        FontFamily? fontFamily = null;
        try
        {
            if (Application.Current != null && Application.Current.TryGetResource("MathFontFamily", null, out var res) && res is FontFamily ff)
            {
                fontFamily = ff;
            }
        }
        catch { }

        if (fontFamily == null)
        {
            try
            {
                fontFamily = new FontFamily("avares://Mnemo.UI/Themes/Fonts/STIXTwoMath/STIXTwoMath-Regular.ttf#STIX Two Math");
            }
            catch
            {
                fontFamily = new FontFamily("STIX Two Math");
            }
        }

        _typeface = new Typeface(fontFamily);
        
        // Try to get SKTypeface for more accurate measurements
        try
        {
            _skTypeface = SKTypeface.FromFamilyName(fontFamily.Name, SKFontStyle.Normal);
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
        lock (_measurementCache)
        {
            if (_measurementCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }
        }

        // FormattedText must be created on UI thread
        var formattedText = new FormattedText(
            character,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            _typeface,
            fontSize,
            Brushes.Black
        );

        var width = formattedText.Width;
        var height = formattedText.Baseline;
        var depth = formattedText.Height - formattedText.Baseline;
        
        // If we have Skia, we can refine the ink bounds, but we should never 
        // be smaller than what Avalonia needs to draw the character.
        // Actually, for math, using the font's baseline and height is safer to avoid cropping.
        if (_skTypeface != null)
        {
            using var font = new SKFont(_skTypeface, (float)fontSize);
            var bounds = new SKRect();
            font.MeasureText(character, out bounds);
            
            // We use the larger of the two to ensure no cropping
            height = Math.Max(height, -bounds.Top);
            depth = Math.Max(depth, bounds.Bottom);
        }

        var result = (width, height, depth);
        lock (_measurementCache)
        {
            _measurementCache[cacheKey] = result;
        }
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
        lock (_measurementCache)
        {
            _measurementCache.Clear();
        }
    }
}