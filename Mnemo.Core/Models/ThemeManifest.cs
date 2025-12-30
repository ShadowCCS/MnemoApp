using System;
using System.Collections.Generic;

namespace Mnemo.Core.Models;

public class ThemeManifest
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public DateTimeOffset? LastUsed { get; set; }
    public List<string> PreviewColors { get; set; } = new();
}





