using Microsoft.Xna.Framework;
using SpriteFormat;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Workshop.Sprites;

/// <summary>A fixed test layout of walls drawn with an entry's linked pieces, each chosen by the mask the game computes for its joined sides, so a wrong piece shows at a glance.</summary>
/// <remarks>
/// Drawn row by row, top to bottom, the order the map's row walk uses, through the sprite effect. For a wall, each wall tile open to
/// the south draws its face -- the piece for its left and right neighbours -- and every wall tile draws its top on the tile above its
/// face, so thick walls read as tops with one row of faces. An empty slot draws its set's fallback piece, as the game does. The first
/// variant set is shown. An entry with no linked part shows nothing.
/// </remarks>
public sealed class SpriteLinkPreview(FontService fontService, ElementPoolService elementPoolService, UiRenderService renderService)
    : Element(fontService, elementPoolService, renderService)
{
    /// <summary>A horizontal run, a vertical run, a 3x3 block, an L, a T, a + junction and a lone pillar, with room above and beside for anything to reach.</summary>
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

    private static readonly Color FloorColor = new(60, 52, 44);

    private SpriteCellDrawer _drawer = null!;
    private SpriteEntryView? _entry;

    protected override bool RequiresContentViewport => true;

    public void Configure(SpriteCellDrawer drawer) => _drawer = drawer;

    public void Show(SpriteEntryView? entry) => _entry = entry;

    public override void DrawContent(GameTime gameTime)
    {
        if (_entry is not { } entry || !entry.Parts.Any(part => part.Layout is not null))
        {
            return;
        }

        var tilePixels = Math.Max(1, (int)Math.Min(ContentSize.X / TestLayout[0].Length, ContentSize.Y / TestLayout.Length));
        var batch = Render.BeginSpriteEffectPass();
        try
        {
            DrawLayout(batch, entry, tilePixels);
        }
        finally
        {
            Render.EndSpriteEffectPass();
        }
    }

    private void DrawLayout(SpriteEffectBatch batch, SpriteEntryView entry, int tilePixels)
    {
        batch.Draw(Render.UnitRectangle, new Rectangle(0, 0, TestLayout[0].Length * tilePixels, TestLayout.Length * tilePixels), null, FloorColor, SpriteDrawStyle.Plain);

        var face = entry.Kind == SpriteEntryKind.Wall ? entry.Parts[0] : null;
        var joined = entry.Kind == SpriteEntryKind.Wall ? entry.Parts[1] : entry.Parts[0];
        for (var y = 0; y < TestLayout.Length; y++)
        {
            for (var x = 0; x < TestLayout[y].Length; x++)
            {
                if (!IsWall(x, y))
                {
                    continue;
                }

                if (face is { Layout: { } faceLayout } && !IsWall(x, y + 1))
                {
                    DrawPiece(batch, face, faceLayout.PieceFor(FirstSet(face, faceLayout), faceLayout.Compute(IsWall, x, y)), x, y, tilePixels);
                }

                if (joined.Layout is { } layout)
                {
                    DrawPiece(batch, joined, layout.PieceFor(FirstSet(joined, layout), layout.Compute(IsWall, x, y)), x, y, tilePixels);
                }
            }
        }
    }

    private void DrawPiece(SpriteEffectBatch batch, SpriteEntryPartView part, SpriteManifestCell? piece, int x, int y, int tilePixels)
    {
        if (piece is null)
        {
            return;
        }

        var drawn = SpritePlacementTiles.DrawnTiles(1, 1, piece.SourceWidth, piece.SourceHeight, part.Part.PlacementFor(piece), part.RaisedRows);
        _drawer.Draw(batch, piece, new Vector2((x + drawn.Left) * tilePixels, (y + drawn.Top) * tilePixels), new Vector2(drawn.Width * tilePixels, drawn.Height * tilePixels));
    }

    private static SpriteManifestCell?[] FirstSet(SpriteEntryPartView part, SpriteLinkLayout layout) =>
        part.Part.Cells.Count >= layout.SlotCount ? [.. part.Part.Cells.Take(layout.SlotCount)] : layout.NewVariantSet();

    private static bool IsWall(int x, int y) =>
        y >= 0 && y < TestLayout.Length && x >= 0 && x < TestLayout[y].Length && TestLayout[y][x] == '#';
}
