using System;
using System.Collections.Generic;

namespace Mnemo.Core.Models;

/// <summary>
/// Describes the contents of a <c>.mnemo</c> package.
/// </summary>
public sealed class MnemoPackageManifest
{
    /// <summary>
    /// Stable format identifier for package validation.
    /// </summary>
    public string Format { get; set; } = "mnemo-package";

    /// <summary>
    /// Package schema version.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// UTC timestamp when the package was created.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// App version that produced this package.
    /// </summary>
    public string? CreatedByAppVersion { get; set; }

    /// <summary>
    /// Optional user-facing package kind label. Import discovery remains authoritative.
    /// </summary>
    public string? PackageKind { get; set; }

    /// <summary>
    /// Payload entries included in the package.
    /// </summary>
    public List<MnemoPackageEntry> Entries { get; set; } = new();

    /// <summary>
    /// Optional asset metadata for future binary validation.
    /// </summary>
    public List<MnemoPackageAsset> Assets { get; set; } = new();
}

/// <summary>
/// Represents a single logical payload area in the package.
/// </summary>
public sealed class MnemoPackageEntry
{
    public string PayloadType { get; set; } = string.Empty;

    public int ItemCount { get; set; }

    public int SchemaVersion { get; set; } = 1;

    public string Path { get; set; } = string.Empty;

    public List<string> Capabilities { get; set; } = new();
}

/// <summary>
/// Optional asset metadata entry.
/// </summary>
public sealed class MnemoPackageAsset
{
    public string Path { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string? ChecksumSha256 { get; set; }
}
