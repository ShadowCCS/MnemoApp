using System;

namespace Mnemo.Core.Services;

/// <summary>
/// Raised after the shell navigates from one route to another (including the first navigation).
/// </summary>
public sealed class NavigationChangedEventArgs : EventArgs
{
    /// <summary>Previous top route, or null on first navigation.</summary>
    public string? PreviousRoute { get; init; }

    /// <summary>New top route.</summary>
    public required string Route { get; init; }

    /// <summary>View model instance being replaced, if any.</summary>
    public object? PreviousViewModel { get; init; }

    /// <summary>New active view model.</summary>
    public object? ViewModel { get; init; }
}
