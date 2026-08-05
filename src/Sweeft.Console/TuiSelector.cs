using Sweeft.Core;

namespace Sweeft.ConsoleApp;

/// <summary>
/// A tiny, dependency-free interactive terminal selector (ncdu/fzf-style) for
/// choosing which findings to clean. Built on System.Console only, so it stays
/// fully NativeAOT-compatible.
/// </summary>
internal static class TuiSelector
{
    /// <summary>Whether an interactive TUI can run (needs a real terminal).</summary>
    public static bool IsAvailable => !Console.IsInputRedirected && !Console.IsOutputRedirected;

    /// <summary>
    /// Shows the selector. Returns the chosen findings, or <c>null</c> if the
    /// user cancelled (q / Esc).
    /// </summary>
    public static List<Finding>? Select(IReadOnlyList<Finding> items)
    {
        if (items.Count == 0) return new List<Finding>();

        var selected = new bool[items.Count];
        for (int i = 0; i < items.Count; i++)
            selected[i] = items[i].Kind == FindingKind.GlobalCache
                || (items[i].Kind == FindingKind.JunkFolder && items[i].RepoStatus != GitRepoStatus.Dirty);

        int cursor = 0, top = 0;
        // Note: only set CursorVisible (the getter is Windows-only, so we don't read it).
        try { Console.CursorVisible = false; } catch { /* ignore */ }

        try
        {
            while (true)
            {
                int width = SafeWidth();
                int rows = Math.Max(3, SafeHeight() - 5);
                if (cursor < top) top = cursor;
                if (cursor >= top + rows) top = cursor - rows + 1;
                if (top > Math.Max(0, items.Count - rows)) top = Math.Max(0, items.Count - rows);

                Render(items, selected, cursor, top, rows, width);

                ConsoleKeyInfo key;
                try { key = Console.ReadKey(intercept: true); }
                catch { return null; } // input became unavailable

                switch (key.Key)
                {
                    case ConsoleKey.UpArrow or ConsoleKey.K:
                        cursor = (cursor - 1 + items.Count) % items.Count; break;
                    case ConsoleKey.DownArrow or ConsoleKey.J:
                        cursor = (cursor + 1) % items.Count; break;
                    case ConsoleKey.PageUp:
                        cursor = Math.Max(0, cursor - rows); break;
                    case ConsoleKey.PageDown:
                        cursor = Math.Min(items.Count - 1, cursor + rows); break;
                    case ConsoleKey.Home:
                        cursor = 0; break;
                    case ConsoleKey.End:
                        cursor = items.Count - 1; break;
                    case ConsoleKey.Spacebar:
                        selected[cursor] = !selected[cursor]; break;
                    case ConsoleKey.A:
                        Array.Fill(selected, true); break;
                    case ConsoleKey.N:
                        Array.Fill(selected, false); break;
                    case ConsoleKey.Enter or ConsoleKey.D:
                        return Enumerable.Range(0, items.Count)
                            .Where(i => selected[i]).Select(i => items[i]).ToList();
                    case ConsoleKey.Q or ConsoleKey.Escape:
                        return null;
                }
            }
        }
        finally
        {
            try { Console.CursorVisible = true; } catch { /* ignore */ }
            Console.ResetColor();
            Console.Clear();
        }
    }

    private static void Render(
        IReadOnlyList<Finding> items, bool[] selected, int cursor, int top, int rows, int width)
    {
        int selCount = 0;
        long selBytes = 0;
        for (int i = 0; i < items.Count; i++)
            if (selected[i]) { selCount++; selBytes += items[i].SizeBytes; }

        Console.SetCursorPosition(0, 0);

        WriteLine("Sweeft — select items to clean", width, ConsoleColor.Cyan);
        WriteLine("↑/↓ move · Space toggle · a all · n none · Enter clean · q quit", width, ConsoleColor.DarkGray);

        for (int r = 0; r < rows; r++)
        {
            int i = top + r;
            if (i >= items.Count)
            {
                WriteLine("", width);
                continue;
            }

            var f = items[i];
            string marker = i == cursor ? "›" : " ";
            string check = selected[i] ? "[x]" : "[ ]";
            string kind = f.Kind switch
            {
                FindingKind.JunkFolder => "DIR ",
                FindingKind.GlobalCache => "CACHE",
                _ => "FILE",
            };
            string risky = f.RepoStatus == GitRepoStatus.Dirty ? " !" : "  ";
            string line = $" {marker} {check} {f.HumanSize,10}{risky} {kind,-5} {f.Path}";

            if (i == cursor)
                WriteInverted(line, width);
            else
                WriteLine(line, width, selected[i] ? ConsoleColor.Green : null);
        }

        WriteLine($" Selected: {selCount} item(s) · {SizeFormatter.Humanize(selBytes)}" +
                  (items.Count > rows ? $"   [{top + 1}-{Math.Min(top + rows, items.Count)}/{items.Count}]" : ""),
                  width, ConsoleColor.Yellow);
    }

    private static void WriteLine(string text, int width, ConsoleColor? color = null)
    {
        if (color is { } c) Console.ForegroundColor = c;
        Console.Write(Truncate(text, width - 1).PadRight(width - 1));
        Console.ResetColor();
        Console.Write('\n');
    }

    private static void WriteInverted(string text, int width)
    {
        Console.BackgroundColor = ConsoleColor.DarkCyan;
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write(Truncate(text, width - 1).PadRight(width - 1));
        Console.ResetColor();
        Console.Write('\n');
    }

    private static string Truncate(string text, int max)
        => max <= 1 || text.Length <= max ? text : text[..(max - 1)] + "…";

    private static int SafeWidth()
    {
        try { return Math.Max(40, Console.WindowWidth); } catch { return 80; }
    }

    private static int SafeHeight()
    {
        try { return Math.Max(8, Console.WindowHeight); } catch { return 25; }
    }
}
