using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using TeletextRecoveReese.Core;

namespace TeletextRecoveReese;

// The status message is twenty transmitted positions, including spacing codes.
internal sealed class BroadcastStatusMessageControl : Control
{
    private const double CellWidth = 10;
    private const double CellHeight = 20;
    private static readonly Typeface MessageTypeface = new("Menlo,DejaVu Sans Mono,monospace");
    private static readonly IBrush[] Palette =
        [Brushes.Black, Brushes.Red, Brushes.Lime, Brushes.Yellow, Brushes.Blue, Brushes.Magenta, Brushes.Cyan, Brushes.White];
    private readonly IReadOnlyList<BroadcastStatusCell> _cells;
    private readonly FormattedText?[] _glyphs;

    public BroadcastStatusMessageControl(BroadcastServiceInfo data)
    {
        _cells = data.GetStatusCells();
        _glyphs = new FormattedText?[_cells.Count];
        for (int index = 0; index < _cells.Count; index++)
        {
            var cell = _cells[index];
            if (cell.Character == ' ') continue;
            _glyphs[index] = new FormattedText(cell.Character.ToString(), CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, MessageTypeface, 14, Palette[(int)cell.Foreground]);
        }
    }

    protected override Size MeasureOverride(Size availableSize) => new(_cells.Count * CellWidth, CellHeight);

    public override void Render(DrawingContext context)
    {
        using (context.PushRenderOptions(new RenderOptions { EdgeMode = EdgeMode.Aliased }))
            for (int index = 0; index < _cells.Count; index++)
                context.FillRectangle(Palette[(int)_cells[index].Background],
                    new Rect(index * CellWidth, 0, CellWidth, CellHeight));
        for (int index = 0; index < _glyphs.Length; index++)
            if (_glyphs[index] is { } glyph)
                context.DrawText(glyph, new Point(index * CellWidth + (CellWidth - glyph.Width) / 2,
                    (CellHeight - glyph.Height) / 2));
    }
}
