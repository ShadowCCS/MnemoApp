using System.Text.Json;
using Mnemo.Core.Models;

namespace Mnemo.Infrastructure.Services.Updates;

/// <summary>JSON persistence for <see cref="AppUpdateInfo"/> in app settings.</summary>
public static class AppUpdateInfoPersistence
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Serialize(AppUpdateInfo info) =>
        JsonSerializer.Serialize(
            new Persisted(info.Version, info.ReleaseNotesMarkdown, info.PublishedAtUtc, info.IsMandatory),
            Options);

    public static AppUpdateInfo? Deserialize(string json)
    {
        try
        {
            var p = JsonSerializer.Deserialize<Persisted>(json, Options);
            if (p is null || string.IsNullOrWhiteSpace(p.Version))
                return null;
            return new AppUpdateInfo(p.Version, p.ReleaseNotesMarkdown, p.PublishedAtUtc, p.IsMandatory);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record Persisted(
        string Version,
        string? ReleaseNotesMarkdown,
        DateTime? PublishedAtUtc,
        bool IsMandatory);
}
