namespace DepuradorCarpetas.Core;

/// <summary>Utilities for presenting byte sizes in a human-readable way.</summary>
public static class SizeFormatter
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB", "PB" };

    /// <summary>Converts a byte count into a readable string (e.g. "1.23 GB").</summary>
    public static string Humanize(long bytes)
    {
        if (bytes < 0) return "0 B";
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < Units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return unit == 0
            ? $"{size:0} {Units[unit]}"
            : $"{size:0.##} {Units[unit]}";
    }

    /// <summary>
    /// Parses a size string with a suffix (KB, MB, GB...) and converts it to
    /// bytes. Accepts values like "100MB", "1.5 GB", "500000".
    /// </summary>
    public static long ParseSize(string text)
    {
        text = text.Trim().ToUpperInvariant();
        long multiplier = 1;
        foreach (var (suffix, factor) in new[]
                 {
                     ("TB", 1024L * 1024 * 1024 * 1024),
                     ("GB", 1024L * 1024 * 1024),
                     ("MB", 1024L * 1024),
                     ("KB", 1024L),
                     ("B", 1L),
                 })
        {
            if (text.EndsWith(suffix, StringComparison.Ordinal))
            {
                multiplier = factor;
                text = text[..^suffix.Length].Trim();
                break;
            }
        }

        if (double.TryParse(text, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            return (long)(value * multiplier);
        }
        throw new FormatException($"Could not parse size: '{text}'.");
    }
}
