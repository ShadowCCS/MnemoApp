using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Data.Converters;
using Mnemo.Core.Models.Flashcards;

namespace Mnemo.UI.Converters;

/// <summary>Returns true when the card's due date is on or before today (local calendar).</summary>
public sealed class FlashcardDueDateIsDueConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTimeOffset dto)
            return false;
        return dto.LocalDateTime.Date <= DateTime.Today;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>True when the due calendar day is after today (show scheduled date instead of "Due").</summary>
public sealed class FlashcardDueDateIsFutureDayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTimeOffset dto)
            return false;
        return dto.LocalDateTime.Date > DateTime.Today;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Short culture-specific date for non-due cards.</summary>
public sealed class FlashcardDueDateToShortDateConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTimeOffset dto)
            return string.Empty;
        return dto.LocalDateTime.ToString("d", culture);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Uppercase type label for deck rows (e.g. CLASSIC).</summary>
public sealed class FlashcardTypeToBadgeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not FlashcardType t)
            return string.Empty;
        return t.ToString().ToUpperInvariant();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// True when the bound string contains at least one <c>{{cN::…}}</c> cloze marker. Used to show
/// the small "Cloze" chip on collapsed deck rows — derived from content so cloze stays a presentation
/// mode rather than a distinct card type.
/// </summary>
public sealed class FlashcardHasClozeMarkersConverter : IValueConverter
{
    private static readonly Regex ClozeOrdinalPattern = new(@"\{\{c\d+::", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s && !string.IsNullOrEmpty(s) && ClozeOrdinalPattern.IsMatch(s);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Produces a clean single-line preview for collapsed deck rows: strips markdown image tokens
/// (<c>![alt](path){align=...}</c>) and collapses cloze markers to <c>[…]</c> so the row never
/// leaks raw file paths or cloze syntax into the UI.
/// </summary>
public sealed class FlashcardCollapsedPreviewConverter : IValueConverter
{
    private static readonly Regex ImageMarkdownPattern = new(
        @"!\[[^\]]*\]\([^)]+\)(?:\{align=(?:left|center|right)\})?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex ClozeContentPattern = new(
        @"\{\{c\d+::(.*?)}}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly Regex WhitespacePattern = new(
        @"\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s || string.IsNullOrEmpty(s))
            return string.Empty;

        var stripped = ImageMarkdownPattern.Replace(s, string.Empty);
        stripped = ClozeContentPattern.Replace(stripped, "[…]");
        stripped = WhitespacePattern.Replace(stripped, " ").Trim();
        return stripped;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
