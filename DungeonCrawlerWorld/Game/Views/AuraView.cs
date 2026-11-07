using Engine.Math;
using Game.Modules.Auras;
using Microsoft.Xna.Framework;

namespace Game.Views;

/// <summary>The auras on the map: where each reaches, how strongly and whom it is credited to, read from the same field gameplay applies them from, and what each is called and does.</summary>
/// <remarks>
/// Power, never a look: how it looks -- blending the colours of the auras at a cell, how strong a glow a power is -- is
/// Presentation's. Every read builds the field first if nothing has yet, so a read before the first frame still
/// sees the terrain's auras.
/// </remarks>
public sealed class AuraView(AuraField auraField, AuraCatalog auras)
{
    private const int NoEntity = -1;

    /// <summary>Changes whenever any aura source is added, removed or moved: a cached rendering of the field is stale once this differs from the value it was made at.</summary>
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

    /// <summary>Fills destination with every aura reaching the cell, strongest first (ties by aura id), each with its power there and whom it is credited to.</summary>
    public void CopyAurasAt(int x, int y, int mapLayer, List<AuraAtCell> destination)
    {
        destination.Clear();
        auraField.EnsureBuilt();
        var position = new Vector3Int(x, y, mapLayer);
        if (!auraField.AnyAuraReaches(position))
        {
            return;
        }

        foreach (var auraId in auraField.AuraIdsInField)
        {
            var power = auraField.PowerReaching(position, auraId, NoEntity, out var credit);
            if (power > 0)
            {
                destination.Add(new AuraAtCell(auraId, power, credit));
            }
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
