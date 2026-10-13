using Engine.Math;
using Game.Modules.Auras;
using Microsoft.Xna.Framework;

namespace Game.Views;

/// <summary>Where auras reach on the map and how strongly, read from the same field gameplay applies them from.</summary>
/// <remarks>
/// Power only: how it looks -- blending the colours of the auras at a cell, how strong a glow a power is -- is
/// Presentation's. Every read builds the field first if nothing has yet, so a read before the first frame still
/// sees the terrain's auras.
/// </remarks>
public sealed class AuraPowerView(AuraField auraField, AuraCatalog auras)
{
    /// <summary>Changes whenever any aura source is added, removed or moved, or an aura's definition is replaced: a cached rendering of the field is stale once this differs from the value it was made at.</summary>
    public int Version => auraField.Version;

    /// <summary>Every aura the field has held a source of -- the only auras that can reach anything.</summary>
    public ReadOnlySpan<byte> AuraIdsInField
    {
        get
        {
            auraField.EnsureBuilt();
            return auraField.AuraIdsInField;
        }
    }

    /// <summary>Whether any aura reaches the cell at all, for ruling it out before asking about each aura.</summary>
    public bool AnyAuraReaches(int x, int y, int mapLayer)
    {
        auraField.EnsureBuilt();
        return auraField.AnyAuraReaches(new Vector3Int(x, y, mapLayer));
    }

    /// <summary>The aura's total power at the cell: every source's value there, added together.</summary>
    public int PowerAt(int x, int y, int mapLayer, byte auraId)
    {
        auraField.EnsureBuilt();
        return auraField.GetTotalPowerAt(new Vector3Int(x, y, mapLayer), auraId);
    }

    /// <summary>The colour the aura glows, read from its current definition.</summary>
    public Color GlowColorOf(byte auraId) => auras.Get(auraId).GlowColor;
}
