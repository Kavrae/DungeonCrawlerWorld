namespace SpriteManifestBuilder;

/// <summary>
/// The working state of one separately selected picture of the entry being edited: the entry itself, or a wall's face
/// or top. Each has its own selection space -- options or slots -- its own span and its own placement, so switching
/// between a wall's face and top never mixes their cells.
/// </summary>
/// <param name="layout">The kind of linked set this part's slots are, when it is one: Cardinal for a top or a plain linked set, Horizontal for a wall's face.</param>
public sealed class EntryPart(SpriteLinkLayout layout)
{
    public SpriteLinkLayout Layout { get; } = layout;

    public List<SpriteManifestCell> Options { get; } = [];

    public List<SpriteManifestCell?[]> VariantSets { get; } = [layout.NewVariantSet()];

    public int CurrentVariantSetIndex { get; set; }

    public int SelectedMask { get; set; } = layout.DefaultSelectedMask;

    public Size Span { get; set; } = new(1, 1);

    /// <summary>Null stretches every cell to its footprint.</summary>
    public int? PixelsPerTile { get; set; }

    /// <summary>Null on both is the default pivot, the bottom-centre of each cell.</summary>
    public int? PivotX { get; set; }

    public int? PivotY { get; set; }

    public SpriteManifestCell?[] CurrentVariantSet => VariantSets[Math.Clamp(CurrentVariantSetIndex, 0, VariantSets.Count - 1)];

    /// <summary>Each variant set's tint ("#RRGGBB"), parallel to VariantSets: written onto every cell of its set, so a second set can be the same pieces in another colour. Null for none.</summary>
    public List<string?> VariantSetTints { get; } = [null];

    public string? CurrentSetTint => VariantSetTints[Math.Clamp(CurrentVariantSetIndex, 0, VariantSetTints.Count - 1)];

    /// <summary>Each variant set's chance in whole percent, parallel to VariantSets. They must add up to 100 to save; adding or removing a set splits 100 evenly again.</summary>
    public List<int> VariantWeights { get; } = [100];

    public int VariantWeightsTotal => VariantWeights.Sum();

    /// <summary>100 split as evenly as whole percents allow, any remainder on the first sets: what an entry with no saved chances has.</summary>
    public static List<int> EvenSplit(int setCount) =>
        [.. Enumerable.Range(0, setCount).Select(index => 100 / setCount + (index < 100 % setCount ? 1 : 0))];

    /// <summary>The chances to save: null when they are the even split (the game's default), so an untouched entry re-saves unchanged.</summary>
    public List<int>? SavedVariantWeights() => VariantWeights.SequenceEqual(EvenSplit(VariantSets.Count)) ? null : [.. VariantWeights];

    public bool HasExplicitPivot => PivotX is not null || PivotY is not null;

    public Point PivotFor(SpriteManifestCell cell) =>
        new(PivotX ?? cell.SourceWidth / 2, PivotY ?? cell.SourceHeight);

    public IEnumerable<SpriteManifestCell?> Cells(bool linked) => linked ? VariantSets.SelectMany(set => set) : Options;

    /// <summary>This part as the manifest saves it: its cells (linked, in mask order with empty slots, or its options), placement, span and chances.</summary>
    public SpriteManifestPart ToManifestPart(bool linked) =>
        new(
            [.. Cells(linked)],
            PixelsPerTile,
            PivotX,
            PivotY,
            linked ? Layout.Linking : SpriteLinking.None,
            Span.Width == 1 ? 0 : Span.Width,
            Span.Height == 1 ? 0 : Span.Height,
            linked ? SavedVariantWeights() : null);

    /// <summary>The variant sets (numbered from 1) missing the layout's required piece.</summary>
    public List<int> SetsMissingRequiredPiece() =>
        Layout.RequiredMask is { } requiredMask
            ? [.. VariantSets.Select((set, index) => (set, index)).Where(pair => pair.set[requiredMask] is null).Select(pair => pair.index + 1)]
            : [];

    public void Clear()
    {
        Options.Clear();
        VariantSets.Clear();
        VariantSets.Add(Layout.NewVariantSet());
        VariantSetTints.Clear();
        VariantSetTints.Add(null);
        ResetVariantWeights();
        CurrentVariantSetIndex = 0;
        SelectedMask = Layout.DefaultSelectedMask;
        Span = new Size(1, 1);
        PixelsPerTile = null;
        PivotX = null;
        PivotY = null;
    }

    /// <summary>Sets every variant set's chance: saved when it has one per set, otherwise 100 split evenly.</summary>
    private void ResetVariantWeights(IReadOnlyList<int>? saved = null)
    {
        VariantWeights.Clear();
        VariantWeights.AddRange(saved is not null && saved.Count == VariantSets.Count ? saved : EvenSplit(VariantSets.Count));
    }

    /// <summary>Adds an empty variant set, untinted, makes it the current one, and splits the chances evenly again.</summary>
    public void AddVariantSet()
    {
        VariantSets.Add(Layout.NewVariantSet());
        VariantSetTints.Add(null);
        CurrentVariantSetIndex = VariantSets.Count - 1;
        ResetVariantWeights();
    }

    /// <summary>Removes the current variant set (the last one left is emptied instead), and splits the chances evenly again.</summary>
    public void RemoveCurrentVariantSet()
    {
        var index = Math.Clamp(CurrentVariantSetIndex, 0, VariantSets.Count - 1);
        if (VariantSets.Count == 1)
        {
            Array.Clear(VariantSets[0]);
            VariantSetTints[0] = null;
            ResetVariantWeights();
            return;
        }

        VariantSets.RemoveAt(index);
        VariantSetTints.RemoveAt(index);
        CurrentVariantSetIndex = Math.Min(index, VariantSets.Count - 1);
        ResetVariantWeights();
    }

    /// <summary>Tints the current variant set: every piece in it now, and every piece put in it later.</summary>
    public void SetCurrentSetTint(string? tint)
    {
        var index = Math.Clamp(CurrentVariantSetIndex, 0, VariantSets.Count - 1);
        VariantSetTints[index] = tint;
        var variantSet = VariantSets[index];
        for (var mask = 0; mask < variantSet.Length; mask++)
        {
            if (variantSet[mask] is { } piece)
            {
                variantSet[mask] = piece with { Tint = tint };
            }
        }
    }

    /// <summary>piece carrying the current variant set's tint, for a piece about to go into that set.</summary>
    public SpriteManifestCell WithCurrentSetTint(SpriteManifestCell piece) => piece with { Tint = CurrentSetTint };

    /// <summary>Fills this part from saved cells: options as they are, or a linked set's cells split into variant sets in mask order.</summary>
    /// <param name="savedAsOptions">For a linked part, cells saved as plain options: each becomes its own set's fallback piece.</param>
    /// <param name="variantWeights">The saved chances, one per variant set; null (or the wrong count) splits 100 evenly.</param>
    public void Load(IReadOnlyList<SpriteManifestCell?> cells, bool linked, bool savedAsOptions, int? pixelsPerTile, int? pivotX, int? pivotY, Size span, IReadOnlyList<int>? variantWeights = null)
    {
        Clear();
        PixelsPerTile = pixelsPerTile;
        PivotX = pivotX;
        PivotY = pivotY;
        Span = span;

        if (!linked)
        {
            Options.AddRange(cells.OfType<SpriteManifestCell>());
            return;
        }

        VariantSets.Clear();
        if (savedAsOptions)
        {
            foreach (var option in cells.OfType<SpriteManifestCell>())
            {
                var variantSet = Layout.NewVariantSet();
                variantSet[Layout.FallbackMask] = option;
                VariantSets.Add(variantSet);
            }
        }
        else
        {
            for (var start = 0; start < cells.Count; start += Layout.SlotCount)
            {
                var variantSet = Layout.NewVariantSet();
                for (var mask = 0; mask < Layout.SlotCount && start + mask < cells.Count; mask++)
                {
                    variantSet[mask] = cells[start + mask];
                }

                VariantSets.Add(variantSet);
            }
        }

        if (VariantSets.Count == 0)
        {
            VariantSets.Add(Layout.NewVariantSet());
        }

        VariantSetTints.Clear();
        VariantSetTints.AddRange(VariantSets.Select(set => set.OfType<SpriteManifestCell>().FirstOrDefault()?.Tint));
        ResetVariantWeights(variantWeights);
    }
}
