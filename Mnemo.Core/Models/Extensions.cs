using System;
using System.Collections.Generic;

namespace Mnemo.Core.Models;

[Flags]
public enum ExtensionPermission
{
    None = 0,
    FileAccess = 1,
    NetworkAccess = 2,
    UIAccess = 4,
    ApiRegistration = 8,
    FullTrust = 16
}

public class ExtensionManifest
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class ExtensionMetadata
{
    public ExtensionManifest Manifest { get; set; } = new();
}
