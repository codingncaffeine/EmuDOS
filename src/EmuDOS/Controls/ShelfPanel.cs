using System.Windows;
using System.Windows.Controls;
using EmuDOS.ViewModels;

namespace EmuDOS.Controls;

/// <summary>
/// Lays game boxes onto the repeating (1425×671) bookshelf using a section-aware flow derived
/// from the user's calibration: boxes fill an area (one of the 3 sections between the dividers)
/// and are spread evenly within it; when an area is full the flow moves to the next area, and
/// when all areas on a row are full it drops to the next shelf, far-left. Every box bottom rests
/// on a shelf board. A manually-placed box (edit mode) stays exactly where it was dropped.
/// </summary>
public sealed class ShelfPanel : Panel
{
    private const double ColumnWidth = 1425;
    private const double TileHeight = 671;
    private const double RailLeft = 25;
    private const double RailRight = 1399;

    // Box-bottom Y of each shelf within one tile, from the user's calibration.
    private static readonly double[] ShelfBottoms = [164, 322, 485, 647];

    // The 3 areas (between the 2 dividers), from the user's calibration. Middle is wider.
    private static readonly (double Left, double Right)[] Sections =
    [
        (25, 408),
        (442, 976),
        (1026, 1399),
    ];

    private const double MinGap = 24;
    private const double MaxGap = 55;

    public double LeftRail => RailLeft;

    public double RightRail => RailRight;

    /// <summary>Snap an arbitrary Y to the nearest shelf board (bottom), repeating per tile.</summary>
    public double SnapBottom(double y)
    {
        double best = ShelfBottoms[0];
        double bestDist = double.MaxValue;
        int maxTile = (int)(y / TileHeight) + 2;
        for (int tile = 0; tile <= maxTile; tile++)
        {
            foreach (var b in ShelfBottoms)
            {
                double candidate = (tile * TileHeight) + b;
                double d = Math.Abs(candidate - y);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = candidate;
                }
            }
        }

        return best;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (UIElement child in InternalChildren)
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        var flow = FlowAuto();
        int shelves = flow.Count > 0 ? flow.Max(f => f.Shelf) + 1 : 0;
        int tiles = Math.Max(1, (int)Math.Ceiling(shelves / (double)ShelfBottoms.Length));
        double height = tiles * TileHeight;

        foreach (UIElement child in InternalChildren)
            if (Tile(child) is { IsManuallyPlaced: true } t)
                height = Math.Max(height, t.ManualBottom!.Value + 20);

        return new Size(ColumnWidth, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (UIElement child in InternalChildren)
        {
            if (Tile(child) is { IsManuallyPlaced: true } t)
            {
                double h = child.DesiredSize.Height;
                child.Arrange(new Rect(t.ManualLeft!.Value, t.ManualBottom!.Value - h,
                    child.DesiredSize.Width, h));
            }
        }

        foreach (var item in FlowAuto())
        {
            double w = item.Child.DesiredSize.Width;
            double h = item.Child.DesiredSize.Height;
            int tile = item.Shelf / ShelfBottoms.Length;
            int row = item.Shelf % ShelfBottoms.Length;
            double bottom = (tile * TileHeight) + ShelfBottoms[row];
            item.Child.Arrange(new Rect(item.X, bottom - h, w, h));
        }

        return finalSize;
    }

    private List<(UIElement Child, double X, int Shelf)> FlowAuto()
    {
        var auto = new List<UIElement>();
        foreach (UIElement child in InternalChildren)
            if (Tile(child) is not { IsManuallyPlaced: true })
                auto.Add(child);

        var result = new List<(UIElement, double, int)>();
        int idx = 0;
        int shelf = 0;

        while (idx < auto.Count)
        {
            for (int sec = 0; sec < Sections.Length && idx < auto.Count; sec++)
            {
                var (left, right) = Sections[sec];
                double available = right - left;

                // Greedily take boxes that fit in this area (with at least MinGap between them).
                var group = new List<UIElement>();
                double used = 0;
                while (idx < auto.Count)
                {
                    double w = auto[idx].DesiredSize.Width;
                    double need = group.Count == 0 ? w : used + MinGap + w;
                    if (group.Count == 0 || need <= available)
                    {
                        group.Add(auto[idx]);
                        used = need;
                        idx++;
                    }
                    else
                    {
                        break;
                    }
                }

                // Spread the group evenly across the area.
                int n = group.Count;
                double sumWidth = 0;
                foreach (var g in group)
                    sumWidth += g.DesiredSize.Width;

                double gap = n > 1 ? Math.Min(MaxGap, (available - sumWidth) / (n - 1)) : 0;
                double x = left;
                foreach (var g in group)
                {
                    result.Add((g, x, shelf));
                    x += g.DesiredSize.Width + gap;
                }
            }

            shelf++;
        }

        return result;
    }

    private static GameTile? Tile(UIElement element) =>
        (element as FrameworkElement)?.DataContext as GameTile;
}
