using Engine.Math;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Effects;
using Game.Modules.StatusEffects;
using Microsoft.Xna.Framework;

namespace Game.Modules.Inventory.Definitions;

/// <summary>
/// First real user of AuraSourceGrant's permanent flip-toggle mode -- unblocks TODO.md's
/// "Toggle poison aura ability" as an item rather than a granted action. AuraAndGlowStrength: 16,
/// not 4 -- the aura's actual reach is DistanceFalloff.MaxRadius(strength) = log2(strength), not
/// the strength value itself (see AuraGrid/MapTintGrid), so 16 is the strength that produces
/// exactly a 4-tile range, the same way Lava's own Strength 8 produces a 3-tile range.
/// AuraSourceGrant always targets context.TargetEntityId -- this item's Self-shaped Targeting
/// (below) is what makes that resolve to the wielder, not the activator granting on the wielder
/// by some other means.
/// </summary>
public static class ToxicIdol
{
    public static readonly Guid Id = new("f3a8c1d6-2b4e-4a9f-8c6d-1e7b3a5f9c2d");

    private const int AuraAndGlowStrength = 16;

    public static ItemDefinition Build() => new(
        Id, "Toxic Idol", "HealthPotion", "i", Color.DarkGreen,
        Tags: [Tag.Potion, Tag.Consumable, Tag.Self],
        Effects: [new ActionEffect([new AuraSourceGrant(StatusEffectType.Poison, AuraAndGlowStrength, Color.DarkGreen)])],
        Description: "A squat stone idol weeping a slow green ichor. Holding it active keeps you wreathed in a spreading toxic cloud -- useful for softening a crowd, less so for standing still in one.",
        Summary: "Toggles a Poison aura (range 4) around you.",
        GoldValue: 15,
        Activator: new PotionActivator(
            new TargetingSpec(Shape: TargetShape.Self, Range: 0, AreaSize: 0),
            new ActionTiming(ActionTimingCategory.Immediate, CooldownFrames: null)));
}
