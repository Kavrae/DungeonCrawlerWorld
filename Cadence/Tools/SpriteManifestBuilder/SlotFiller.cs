namespace SpriteManifestBuilder;

/// <summary>
/// Fills a linked set's empty slots from its filled ones, mirrored or turned: a piece joined north and east, mirrored left
/// to right, is the piece joined north and west; a wall face's left end, mirrored, is its right end. Sixteen top slots come
/// from as few as six distinct pieces with turns allowed.
/// </summary>
/// <remarks>
/// Breadth first from every filled slot at once, so each empty slot gets the piece reached in the fewest steps; filled
/// slots are never overwritten. Top-down art is usually lit from above, which a left-right mirror keeps and a turn doesn't --
/// hence turns are opt-in. A step the layout doesn't support (a face has no vertical joins) is skipped, and so is a quarter
/// turn of a non-square piece (the game refuses one).
/// </remarks>
public static class SlotFiller
{
    private readonly record struct Step(Func<int, int> TransformMask, Func<SpriteManifestCell, SpriteManifestCell> TransformPiece, bool IsQuarterTurn);

    /// <returns>How many empty slots were filled.</returns>
    public static int FillEmptySlots(LinkLayout layout, SpriteManifestCell?[] slots, bool mirrorLeftRight, bool mirrorTopBottom, bool turns)
    {
        var steps = new List<Step>(3);
        if (mirrorLeftRight)
        {
            steps.Add(new Step(layout.MirrorLeftRight, static piece => piece.MirroredHorizontally(), IsQuarterTurn: false));
        }
        if (mirrorTopBottom && layout.MirrorTopBottom is { } mirrorTopBottomMask)
        {
            steps.Add(new Step(mirrorTopBottomMask, static piece => piece.MirroredVertically(), IsQuarterTurn: false));
        }
        if (turns && layout.TurnClockwise is { } turnClockwiseMask)
        {
            steps.Add(new Step(turnClockwiseMask, static piece => piece.RotatedClockwise(), IsQuarterTurn: true));
        }

        var reached = new Queue<(int Mask, SpriteManifestCell Piece)>();
        for (var mask = 0; mask < slots.Length; mask++)
        {
            if (slots[mask] is { } piece)
            {
                reached.Enqueue((mask, piece));
            }
        }

        var filledCount = 0;
        while (reached.TryDequeue(out var current))
        {
            foreach (var step in steps)
            {
                if (step.IsQuarterTurn && !current.Piece.CanQuarterTurn)
                {
                    continue;
                }

                var targetMask = step.TransformMask(current.Mask);
                if (slots[targetMask] is not null)
                {
                    continue;
                }

                var transformedPiece = step.TransformPiece(current.Piece);
                slots[targetMask] = transformedPiece;
                filledCount++;
                reached.Enqueue((targetMask, transformedPiece));
            }
        }

        return filledCount;
    }
}
