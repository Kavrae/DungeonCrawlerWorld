namespace Game.Areas;

/// <summary>What each symbol of an area means once its palettes are applied.</summary>
public static class AreaLegend
{
    /// <summary>Every symbol's cell: each palette in order, a later one overriding an earlier one's symbol, then the area's own legend over all of them. A palette findPalette doesn't find is skipped.</summary>
    public static Dictionary<char, AreaCell> Resolve(AreaDefinition area, Func<Guid, AreaPalette?> findPalette)
    {
        var cells = new Dictionary<char, AreaCell>();
        foreach (var paletteId in area.Palettes)
        {
            if (findPalette(paletteId) is not { } palette)
            {
                continue;
            }

            foreach (var cell in palette.Legend)
            {
                cells[cell.Symbol] = cell;
            }
        }

        foreach (var cell in area.Legend)
        {
            cells[cell.Symbol] = cell;
        }

        return cells;
    }
}
