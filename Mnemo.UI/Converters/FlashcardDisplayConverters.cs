using System.Globalization;
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
