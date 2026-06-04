using System.Text.RegularExpressions;

namespace NeumannOrbital.UI;

internal static class Helpers
{
    internal static string FormatDuration(TimeSpan t)
    {
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h{t.Minutes:D2}m";
        if (t.TotalMinutes >= 1) return $"{(int)t.TotalMinutes}m{t.Seconds:D2}s";
        return $"{t.Seconds}s";
    }

    internal static int ParseStarCount(string? summary)
    {
        if (summary is null) return 1;
        var m = Regex.Match(summary, @"with\s+(\d+)\s+star");
        return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : 1;
    }

    internal static int ParseOrbitalCount(string? summary)
    {
        if (summary is null) return 0;
        var m = Regex.Match(summary, @"(\d+)\s+orbital\s+body");
        return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : 0;
    }
}
