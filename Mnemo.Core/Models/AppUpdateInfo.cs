using System;

namespace Mnemo.Core.Models;

/// <summary>Describes an available application update (Velopack feed or GitHub API fallback).</summary>
public sealed class AppUpdateInfo
{
    public AppUpdateInfo(string version, string? releaseNotesMarkdown, DateTime? publishedAtUtc, bool isMandatory)
    {
        Version = version;
        ReleaseNotesMarkdown = releaseNotesMarkdown;
        PublishedAtUtc = publishedAtUtc;
        IsMandatory = isMandatory;
    }

    public string Version { get; }
    public string? ReleaseNotesMarkdown { get; }
    public DateTime? PublishedAtUtc { get; }
    public bool IsMandatory { get; }
}
