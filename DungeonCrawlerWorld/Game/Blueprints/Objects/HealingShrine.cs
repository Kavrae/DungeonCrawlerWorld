using Game.Modules.Auras;
using Microsoft.Xna.Framework;

namespace Game.Blueprints.Objects;

/// <summary>A stationary shrine that heals whatever stands near it.</summary>
/// <remarks>
/// A prop like TreasureChest: no race, no movement, so it never moves once spawned and is always built at spawn. It
/// radiates a healing aura (Auras), and is as durable as every shrine (ShrineDurability).
/// </remarks>
public static class HealingShrine
{
    public static readonly Guid Id = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000124");

    public const string Name = "Healing Shrine";

    private const string Description = "A weathered shrine. The air around it eases wounds.";

    /// <summary>16 at the shrine, then 13, 10, 7 and 4 a tile further each (Linear over HealingAuraSize).</summary>
    private const ushort HealingAuraPower = 16;

    private const byte HealingAuraSize = 4;

    /// <remarks>
    /// Its aura (BuiltInAuraIds.HealingShrine, in the Auras table) heals a flat amount per point of power, so
    /// overlapping shrines add. On a creature with body parts the whole amount follows its healing priority -- the
    /// part missing the largest share of its health, the rule passive regeneration uses -- rather than being split
    /// evenly: an even split gives most of it to parts that are already full, where it is lost.
    /// </remarks>
    public static readonly BlueprintDefinition Definition = new(Id, Name)
    {
        Build = ShrineDurability.Build,
        Auras = [new AuraGrant(BuiltInAuraIds.HealingShrine, HealingAuraPower, HealingAuraSize)],
        Appearance = new() { Name = Name, Description = Description, Glyph = "+", GlyphColor = Color.White, SpriteName = "Shrine" }
    };
}
