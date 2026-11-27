using Engine.Math;
using Game.Modules.Auras;
using Game.World;
using Microsoft.Xna.Framework;

namespace Game.Views;

/// <summary>The auras on the map: where each reaches, how strongly and whom it is credited to, read from the same field gameplay applies them from and the pulse sources (AuraPulseSources) carried by whatever can move, and what each is called and does.</summary>
/// <remarks>
/// Power, never a look: how it looks -- blending the colours of the auras at a cell, how strong a glow a power is -- is
/// Presentation's. Every read builds the field first if nothing has yet, so a read before the first frame still
/// sees the terrain's auras.
/// </remarks>
public sealed class AuraView(AuraField auraField, AuraPulseSources pulses, AuraCatalog auras)
{
    private const int NoEntity = -1;

    private readonly Dictionary<byte, (int Power, ActionSource Credit)> _strongestByAuraScratch = [];
    private readonly List<PulseSourceReach> _tileSourcesScratch = [];

    /// <summary>Changes whenever any aura source is added, removed or moved: a cached rendering of the field is stale once this differs from the value it was made at.</summary>
    public int Version => auraField.Version;

    /// <summary>What a tile's auras are as of: Version with the pulse sources reaching the tile folded in, so a cached read of one tile (CopyAurasAt) knows when a carried aura moved onto or off it.</summary>
    public int VersionAt(int x, int y, int mapLayer)
    {
        _tileSourcesScratch.Clear();
        pulses.CollectSourcesReaching(x, y, x + 1, y + 1, mapLayer, _tileSourcesScratch);
        var hash = new HashCode();
        hash.Add(auraField.Version);
        foreach (var source in _tileSourcesScratch)
        {
            hash.Add(source);
        }

        return hash.ToHashCode();
    }

    /// <summary>Every aura the field has held a source of. A pulse source's aura need not be one: CopyPulseSourcesReaching names those.</summary>
    public ReadOnlySpan<byte> AuraIdsInField
    {
        get
        {
            auraField.EnsureBuilt();
            return auraField.AuraIdsInField;
        }
    }

    /// <summary>Whether any aura reaches the cell at all, the field's or a pulse source's. For many cells at once, read AnyFieldAuraReaches and the pulse sources of the area (CopyPulseSourcesReaching).</summary>
    public bool AnyAuraReaches(int x, int y, int mapLayer)
    {
        auraField.EnsureBuilt();
        var position = new Vector3Int(x, y, mapLayer);
        return auraField.AnyAuraReaches(position) || pulses.AnyReaches(position);
    }

    /// <summary>Whether any aura in the field reaches the tile: an array read, for a pass that adds the pulse sources itself (CopyPulseSourcesReaching).</summary>
    public bool AnyFieldAuraReaches(int x, int y, int mapLayer)
    {
        auraField.EnsureBuilt();
        return auraField.AnyAuraReaches(new Vector3Int(x, y, mapLayer));
    }

    /// <summary>The field's power of an aura at the tile, leaving out pulse sources: for a pass that adds them itself (CopyPulseSourcesReaching). PowerAt is both.</summary>
    public int FieldPowerAt(int x, int y, int mapLayer, byte auraId)
    {
        auraField.EnsureBuilt();
        return auraField.GetTotalPowerAt(new Vector3Int(x, y, mapLayer), auraId);
    }

    /// <summary>Fills destination with every pulse source reaching the rectangle on mapLayer -- the auras carried by whatever can move, which the field never holds -- in one scan, for drawing an area.</summary>
    public void CopyPulseSourcesReaching(int minX, int minY, int maxXExclusive, int maxYExclusive, int mapLayer, List<PulseSourceReach> destination)
    {
        destination.Clear();
        pulses.CollectSourcesReaching(minX, minY, maxXExclusive, maxYExclusive, mapLayer, destination);
    }

    /// <summary>The aura's power at the cell: the strongest source's value there, the field's or a pulse source's.</summary>
    public int PowerAt(int x, int y, int mapLayer, byte auraId)
    {
        auraField.EnsureBuilt();
        return Math.Max(auraField.GetTotalPowerAt(new Vector3Int(x, y, mapLayer), auraId), pulses.PowerAt(new Vector3Int(x, y, mapLayer), auraId));
    }

    /// <summary>Fills destination with every aura reaching the cell, strongest first (ties by aura id), each with its power there and whom it is credited to.</summary>
    public void CopyAurasAt(int x, int y, int mapLayer, List<AuraAtCell> destination)
    {
        destination.Clear();
        auraField.EnsureBuilt();
        var position = new Vector3Int(x, y, mapLayer);
        _strongestByAuraScratch.Clear();
        pulses.CollectAurasAt(position, _strongestByAuraScratch);
        if (auraField.AnyAuraReaches(position))
        {
            foreach (var auraId in auraField.AuraIdsInField)
            {
                var power = auraField.PowerReaching(position, auraId, NoEntity, out var credit);
                if (power > 0 && (!_strongestByAuraScratch.TryGetValue(auraId, out var pulse) || power >= pulse.Power))
                {
                    _strongestByAuraScratch[auraId] = (power, credit);
                }
            }
        }

        foreach (var (auraId, (power, credit)) in _strongestByAuraScratch)
        {
            destination.Add(new AuraAtCell(auraId, power, credit));
        }

        destination.Sort(static (first, second) => first.Power != second.Power ? second.Power.CompareTo(first.Power) : first.AuraId.CompareTo(second.AuraId));
    }

    /// <summary>The aura's name, read from its current definition.</summary>
    public string NameOf(byte auraId) => auras.Get(auraId).Name;

    /// <summary>What being in the aura does, for the player, read from its current definition; empty for none.</summary>
    public string DescriptionOf(byte auraId) => auras.Get(auraId).Description;

    /// <summary>The colour the aura glows, read from its current definition.</summary>
    public Color GlowColorOf(byte auraId) => auras.Get(auraId).GlowColor;

    /// <summary>The aura glow's opacity at full power, read from its current definition.</summary>
    public float GlowOpacityMultiplierOf(byte auraId) => auras.Get(auraId).GlowOpacityMultiplier;
}
