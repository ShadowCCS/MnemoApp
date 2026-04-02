using System;
using Mnemo.Core.Models.Tools;

namespace Mnemo.Infrastructure.Services.Tools;

/// <summary>Parses 1-based inclusive line ranges like <c>5</c>, <c>1-20</c>.</summary>
public static class LineRangeParser
{
    public static bool TryParseSingle(string? spec, out int line1Based, out string? error)
    {
        line1Based = 0;
        error = null;
        if (string.IsNullOrWhiteSpace(spec))
        {
            error = "Range is required.";
            return false;
        }

        var s = spec.Trim();
        if (!int.TryParse(s, out var n) || n < 1)
        {
            error = "Invalid line number.";
            return false;
        }

        line1Based = n;
        return true;
    }

    /// <summary>Parse inclusive 1-based range. End optional => same as start.</summary>
    public static bool TryParseRange(string? spec, out int start1Based, out int end1Based, out string? error)
    {
        start1Based = end1Based = 0;
        error = null;
        if (string.IsNullOrWhiteSpace(spec))
        {
            error = "Line range is required (e.g. \"1-20\" or \"5\").";
            return false;
        }

        var s = spec.Trim().Replace("–", "-"); // en-dash
        var dash = s.IndexOf('-', StringComparison.Ordinal);
        if (dash < 0)
        {
            if (!int.TryParse(s, out var n) || n < 1)
            {
                error = "Invalid line range.";
                return false;
            }

            start1Based = end1Based = n;
            return true;
        }

        var a = s[..dash].Trim();
        var b = s[(dash + 1)..].Trim();
        if (!int.TryParse(a, out var start) || start < 1 ||
            !int.TryParse(b, out var end) || end < 1)
        {
            error = "Invalid line range.";
            return false;
        }

        if (end < start)
        {
            error = "Range end must be >= start.";
            return false;
        }

        start1Based = start;
        end1Based = end;
        return true;
    }

    public static ToolInvocationResult InvalidRange(string detail) =>
        ToolInvocationResult.Failure(ToolResultCodes.InvalidRange, detail);
}
