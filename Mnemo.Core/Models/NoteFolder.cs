using System;

namespace Mnemo.Core.Models;

/// <summary>
/// Represents a folder in the notes hierarchy.
/// </summary>
public class NoteFolder
{
    /// <summary>
    /// Unique identifier for the folder.
    /// </summary>
    public string FolderId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Display name of the folder.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Parent folder id, or null for root-level folders.
    /// </summary>
    public string? ParentId { get; set; }

    /// <summary>
    /// Display order among siblings (lower = first).
    /// </summary>
    public int Order { get; set; }
}
