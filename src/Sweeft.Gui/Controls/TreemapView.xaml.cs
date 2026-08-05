using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Sweeft.Core;

namespace Sweeft.Gui.Controls;

/// <summary>
/// Renders a squarified treemap (area ∝ size) of the given items on a Canvas.
/// Double-clicking a directory raises <see cref="ItemActivated"/> (for drill-down).
/// </summary>
public partial class TreemapView : UserControl
{
    private IReadOnlyList<TreemapItem> _items = Array.Empty<TreemapItem>();
    private long _total = 1;

    // Tableau-10 palette with a precomputed contrasting text color per entry.
    private static readonly (Color Fill, Brush Text)[] Palette = BuildPalette(new[]
    {
        "#4E79A7", "#F28E2B", "#E15759", "#76B7B2", "#59A14F",
        "#EDC948", "#B07AA1", "#FF9DA7", "#9C755F", "#BAB0AC",
    });

    public TreemapView() => InitializeComponent();

    /// <summary>Raised when a directory cell is double-clicked.</summary>
    public event Action<TreemapItem>? ItemActivated;

    public void SetItems(IReadOnlyList<TreemapItem> items, long total)
    {
        _items = items;
        _total = Math.Max(1, total);
        Relayout();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Relayout();

    private sealed class Node
    {
        public required TreemapItem Item;
        public double Area;
    }

    private void Relayout()
    {
        PART_Canvas.Children.Clear();
        double w = PART_Canvas.ActualWidth, h = PART_Canvas.ActualHeight;
        if (w < 4 || h < 4) return;

        var visible = _items.Where(i => i.SizeBytes > 0).OrderByDescending(i => i.SizeBytes).ToList();
        if (visible.Count == 0) return;

        double totalSize = visible.Sum(i => (double)i.SizeBytes);
        double areaScale = (w * h) / totalSize;
        var nodes = visible.Select(i => new Node { Item = i, Area = i.SizeBytes * areaScale }).ToList();

        var placed = new List<(Node Node, Rect R)>();
        Squarify(nodes, new Rect(0, 0, w, h), placed);

        int colorIndex = 0;
        foreach (var (node, r) in placed)
            DrawCell(node.Item, r, Palette[colorIndex++ % Palette.Length]);
    }

    // ---- Squarified treemap layout ----
    private static void Squarify(List<Node> nodes, Rect rect, List<(Node, Rect)> output)
    {
        int i = 0;
        while (i < nodes.Count)
        {
            var row = new List<Node>();
            double side = Math.Min(rect.Width, rect.Height);
            while (i < nodes.Count)
            {
                var candidate = new List<Node>(row) { nodes[i] };
                if (row.Count == 0 || Worst(candidate, side) <= Worst(row, side))
                {
                    row.Add(nodes[i]);
                    i++;
                }
                else break;
            }
            rect = LayoutRow(row, rect, output);
        }
    }

    private static double Worst(List<Node> row, double side)
    {
        if (row.Count == 0) return double.MaxValue;
        double sum = 0, max = double.MinValue, min = double.MaxValue;
        foreach (var n in row)
        {
            sum += n.Area;
            if (n.Area > max) max = n.Area;
            if (n.Area < min) min = n.Area;
        }
        if (sum <= 0) return double.MaxValue;
        double s2 = sum * sum, side2 = side * side;
        return Math.Max(side2 * max / s2, s2 / (side2 * min));
    }

    private static Rect LayoutRow(List<Node> row, Rect rect, List<(Node, Rect)> output)
    {
        double sum = row.Sum(n => n.Area);
        if (sum <= 0) return rect;

        if (rect.Width >= rect.Height)
        {
            double stripW = sum / rect.Height;
            double y = rect.Top;
            foreach (var n in row)
            {
                double cellH = n.Area / stripW;
                output.Add((n, new Rect(rect.Left, y, stripW, cellH)));
                y += cellH;
            }
            return new Rect(rect.Left + stripW, rect.Top, Math.Max(0, rect.Width - stripW), rect.Height);
        }
        else
        {
            double stripH = sum / rect.Width;
            double x = rect.Left;
            foreach (var n in row)
            {
                double cellW = n.Area / stripH;
                output.Add((n, new Rect(x, rect.Top, cellW, stripH)));
                x += cellW;
            }
            return new Rect(rect.Left, rect.Top + stripH, rect.Width, Math.Max(0, rect.Height - stripH));
        }
    }

    // ---- Cell rendering ----
    private void DrawCell(TreemapItem item, Rect r, (Color Fill, Brush Text) color)
    {
        if (r.Width < 1 || r.Height < 1) return;

        var border = new Border
        {
            Width = r.Width,
            Height = r.Height,
            Background = new SolidColorBrush(color.Fill),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)),
            BorderThickness = new Thickness(0.5),
            SnapsToDevicePixels = true,
            ToolTip = $"{item.Name}\n{SizeFormatter.Humanize(item.SizeBytes)}  ·  {100.0 * item.SizeBytes / _total:0.#}%" +
                      (item.IsDirectory ? "\n(double-click to open)" : ""),
            Cursor = item.IsDirectory ? Cursors.Hand : Cursors.Arrow,
        };

        if (r.Width > 44 && r.Height > 26)
        {
            border.Child = new TextBlock
            {
                Text = $"{item.Name}\n{SizeFormatter.Humanize(item.SizeBytes)}",
                Foreground = color.Text,
                Margin = new Thickness(4, 2, 2, 2),
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
            };
        }

        if (item.IsDirectory)
        {
            border.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2)
                    ItemActivated?.Invoke(item);
            };
        }

        Canvas.SetLeft(border, r.Left);
        Canvas.SetTop(border, r.Top);
        PART_Canvas.Children.Add(border);
    }

    private static (Color, Brush)[] BuildPalette(string[] hex)
    {
        var result = new (Color, Brush)[hex.Length];
        for (int i = 0; i < hex.Length; i++)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex[i]);
            // Relative luminance → pick black or white text for contrast.
            double lum = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
            var text = lum > 0.6 ? Brushes.Black : Brushes.White;
            result[i] = (c, text);
        }
        return result;
    }
}
