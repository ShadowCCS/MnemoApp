using System;

namespace Mnemo.Core.Models;

/// <summary>Optional primary / secondary actions for a toast (e.g. update prompt).</summary>
public sealed class ToastActionSpec
{
    public string? PrimaryLabel { get; init; }
    public Action? OnPrimary { get; init; }

    public string? SecondaryLabel { get; init; }
    public Action? OnSecondary { get; init; }

    /// <summary>When true, the toast is dismissed after <see cref="OnPrimary"/> runs.</summary>
    public bool DismissAfterPrimary { get; init; } = true;

    /// <summary>When true, the toast is dismissed after <see cref="OnSecondary"/> runs.</summary>
    public bool DismissAfterSecondary { get; init; } = true;

    /// <summary>Invoked when the user dismisses via the ✕ control (not via primary/secondary actions or auto-dismiss).</summary>
    public Action? OnDismissed { get; init; }
}
