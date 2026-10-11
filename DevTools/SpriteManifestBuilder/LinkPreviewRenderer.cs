namespace SpriteManifestBuilder;

/// <summary>A wall's face, as the link preview draws it under the top: a Horizontal set of standalone, left, right and middle pieces.</summary>
/// <param name="HeightRows">Rows the face covers; the top sits on the tile above them.</param>
public sealed record WallFacePreview(ManifestCell?[] Slots, int? PixelsPerTile, Func<ManifestCell, Point> PivotFor, int HeightRows);

/// <summary>
/// Draws a fixed test layout of walls with a linked set's pieces, chosen by the same masks the game uses, so a wrong piece shows before saving.
/// Each piece is sized and placed by its placement exactly as the placement preview shows it at a 1x1 footprint, and the layout is drawn
/// row by row, top to bottom, the order the map's row walk uses. For a wall, each wall cell open to the south draws its face -- the piece for
/// its left and right neighbours, else the standalone one -- and every wall cell draws its top on the tile above its face, so thick walls
/// read as tops with one row of faces, and a tall face covers the rows behind it. An empty slot draws its set's fallback piece, as the game will.
/// </summary>
public static class LinkPreviewRenderer
{
    /// <summary>
    /// A horizontal run, a vertical run, a 3x3 block, an L, a T, a + junction and a lone pillar. The empty margin is two rows above and
    /// two columns either side -- the most anything may reach -- so nothing is cut off and nothing joins the layout's edge.
    /// </summary>
    private static readonly string[] TestLayout =
    [
        "......................",
        "......................",
        "...#####...#...###....",
        "...........#...###....",
        "...#.......#...###....",
        "...#..................",
        "...###..###...#...#...",
        ".........#...###......",
        ".........#....#.......",
        "......................",
    ];

    public static Size PreferredSize(int tilePixels) => new(TestLayout[0].Length * tilePixels, TestLayout.Length * tilePixels);

    /// <param name="topPivotFor">The pivot a top piece is placed by, in its source pixels.</param>
    /// <param name="face">The wall's face, or null to preview a plain linked set on its own tiles.</param>
    public static void Paint(Graphics graphics, Size canvasSize, ManifestCell?[] topSlots, int? topPixelsPerTile, Func<ManifestCell, Point> topPivotFor, WallFacePreview? face, SheetImageCache sheets)
    {
        var tilePixels = Math.Max(1, Math.Min(canvasSize.Width / TestLayout[0].Length, canvasSize.Height / TestLayout.Length));
        var raisedRows = face?.HeightRows ?? 0;

        using var floorBrush = new SolidBrush(Color.FromArgb(60, 52, 44));
        graphics.FillRectangle(floorBrush, 0, 0, TestLayout[0].Length * tilePixels, TestLayout.Length * tilePixels);

        for (var y = 0; y < TestLayout.Length; y++)
        {
            for (var x = 0; x < TestLayout[y].Length; x++)
            {
                if (!IsWall(x, y))
                {
                    continue;
                }

                if (face is not null && !IsWall(x, y + 1) && LinkLayout.Horizontal.PieceFor(face.Slots, LinkLayout.Horizontal.Compute(IsWall, x, y)) is { } facePiece)
                {
                    var faceTiles = SpritePlacementRules.DrawnTiles(facePiece.SourceRectangle.Size, face.PixelsPerTile, face.PivotFor(facePiece), 1);
                    CellDrawing.Draw(graphics, sheets, facePiece, ToPixels(faceTiles, x, y, tilePixels));
                }

                var topPiece = LinkLayout.Cardinal.PieceFor(topSlots, LinkLayout.Cardinal.Compute(IsWall, x, y));
                var topTiles = topPiece is null
                    ? SpritePlacementRules.DrawnTiles(new Size(1, 1), null, Point.Empty, 1, raisedRows)
                    : SpritePlacementRules.DrawnTiles(topPiece.SourceRectangle.Size, topPixelsPerTile, topPivotFor(topPiece), 1, raisedRows);
                CellDrawing.Draw(graphics, sheets, topPiece, ToPixels(topTiles, x, y, tilePixels));
            }
        }
    }

    private static RectangleF ToPixels(RectangleF tiles, int x, int y, int tilePixels) =>
        RectangleF.FromLTRB((x + tiles.Left) * tilePixels, (y + tiles.Top) * tilePixels, (x + tiles.Right) * tilePixels, (y + tiles.Bottom) * tilePixels);

    private static bool IsWall(int x, int y) =>
        y >= 0 && y < TestLayout.Length && x >= 0 && x < TestLayout[y].Length && TestLayout[y][x] == '#';
}
