using System.Globalization;

namespace Sweeft.Core;

/// <summary>
/// Parses human durations like "90d", "2w", "6mo", "1y", or a plain number of
/// days ("30"). Used for the "stale project" window.
/// </summary>
public static class DurationParser
{
    /// <summary>Parses a duration; throws <see cref="FormatException"/> if invalid.</summary>
    public static TimeSpan Parse(string text)
    {
        text = text.Trim().ToLowerInvariant();
        if (text.Length == 0)
            throw new FormatException("Empty duration.");

        int i = 0;
        while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.'))
            i++;

        var numberPart = text[..i];
        var unit = text[i..].Trim();

        if (!double.TryParse(numberPart, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) || value < 0)
            throw new FormatException($"Invalid duration: '{text}'.");

        return unit switch
        {
            "" or "d" or "day" or "days"     => TimeSpan.FromDays(value),
            "w" or "wk" or "week" or "weeks" => TimeSpan.FromDays(value * 7),
            "mo" or "month" or "months"      => TimeSpan.FromDays(value * 30),
            "y" or "yr" or "year" or "years" => TimeSpan.FromDays(value * 365),
            _ => throw new FormatException($"Unknown duration unit: '{unit}'. Use d, w, mo or y."),
        };
    }

    public static bool TryParse(string text, out TimeSpan result)
    {
        try { result = Parse(text); return true; }
        catch { result = default; return false; }
    }
}
