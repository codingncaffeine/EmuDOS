using System.Windows;
using System.Windows.Controls;

namespace EmuDOS.Controls;

/// <summary>
/// Lays game boxes onto the repeating bookshelf so each rests on a real shelf board. The
/// bookshelf image isn't perfectly evenly spaced (the top compartment differs), so rather
/// than one pitch we snap to the four measured board positions and repeat them every tile.
/// </summary>
public sealed class ShelfPanel : Panel
{
    private const double ColumnWidth = 561;
    private const double TileHeight = 645;
    private const double RailLeft = 76;
    private const double RailRight = 488;

    // ── Tunables: where each shelf's box BOTTOM sits within one 645px tile ─────
    // (top board, second, third, bottom base). Adjust these to seat the boxes.
    private static readonly double[] ShelfBottoms = [162, 328, 487, 645];
    // ──────────────────────────────────────────────────────────────────────────

    private const double BoxHeight = 106;
    private const double BoxWidth = 69;
    private const double ColumnGap = 16;

    private int _columns = 1;

    protected override Size MeasureOverride(Size availableSize)
    {
        double inner = RailRight - RailLeft;
        _columns = Math.Max(1, (int)((inner + ColumnGap) / (BoxWidth + ColumnGap)));

        var boxSize = new Size(BoxWidth, BoxHeight);
        foreach (UIElement child in InternalChildren)
            child.Measure(boxSize);

        int perTile = _columns * ShelfBottoms.Length;
        int tiles = Math.Max(1, (int)Math.Ceiling(InternalChildren.Count / (double)perTile));
        return new Size(ColumnWidth, tiles * TileHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int rows = ShelfBottoms.Length;
        int perTile = _columns * rows;
        double inner = RailRight - RailLeft;
        double used = (_columns * BoxWidth) + ((_columns - 1) * ColumnGap);
        double startX = RailLeft + ((inner - used) / 2);

        for (int i = 0; i < InternalChildren.Count; i++)
        {
            int tile = i / perTile;
            int within = i % perTile;
            int row = within / _columns;
            int col = within % _columns;

            double x = startX + (col * (BoxWidth + ColumnGap));
            double bottom = (tile * TileHeight) + ShelfBottoms[row];
            InternalChildren[i].Arrange(new Rect(x, bottom - BoxHeight, BoxWidth, BoxHeight));
        }

        return finalSize;
    }
}
