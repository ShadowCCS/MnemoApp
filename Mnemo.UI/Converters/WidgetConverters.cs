using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Mnemo.UI.Modules.Overview;
using Mnemo.UI.Modules.Overview.ViewModels;

namespace Mnemo.UI.Converters
{
    /// <summary>
    /// Converts a grid column index to pixel X coordinate.
    /// Uses <see cref="OverviewGridConstants"/> for cell dimensions.
    /// </summary>
    public class GridColumnToPixelsConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int column)
            {
                return column * (OverviewGridConstants.CellWidth + OverviewGridConstants.CellSpacing);
            }
            return 0.0;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts a grid row index to pixel Y coordinate.
    /// Uses <see cref="OverviewGridConstants"/> for cell dimensions.
    /// </summary>
    public class GridRowToPixelsConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int row)
            {
                return row * (OverviewGridConstants.CellHeight + OverviewGridConstants.CellSpacing);
            }
            return 0.0;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts column span to pixel width.
    /// Uses <see cref="OverviewGridConstants"/> for cell dimensions.
    /// </summary>
    public class ColSpanToWidthConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int colSpan)
            {
                return colSpan * OverviewGridConstants.CellWidth + (colSpan - 1) * OverviewGridConstants.CellSpacing;
            }
            return (double)OverviewGridConstants.CellWidth;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts row span to pixel height.
    /// Uses <see cref="OverviewGridConstants"/> for cell dimensions.
    /// </summary>
    public class RowSpanToHeightConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int rowSpan)
            {
                return rowSpan * OverviewGridConstants.CellHeight + (rowSpan - 1) * OverviewGridConstants.CellSpacing;
            }
            return (double)OverviewGridConstants.CellHeight;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts edit mode boolean to button text.
    /// </summary>
    public class EditModeConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isEditMode)
            {
                return isEditMode ? "Done" : "Edit";
            }
            return "Edit";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts OverviewView DataContext (OverviewViewModel or null) to IsEditMode bool.
    /// Returns false when DataContext is null (e.g. on navigate away) to avoid binding errors.
    /// </summary>
    public class OverviewEditModeConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is OverviewViewModel vm && vm.IsEditMode;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
