namespace Mnemo.Core.Models;

public class LanguageManifest
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NativeName { get; set; } = string.Empty;
    public string? IconPath { get; set; }
}