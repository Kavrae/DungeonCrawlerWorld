using Engine.Math;
using Game.Effects;
using Game.Effects.Entries;
using Engine.Utilities;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.StatusEffects;
using Game.Tags;
using Microsoft.Xna.Framework;

namespace Game.Modules.Inventory.Definitions;

/// <summary>Removes every stack of Poison from the drinker, then makes them immune to Poison for 5 minutes.</summary>
/// <remarks>Rare and valuable: its GoldValue is set on its own, not derived from another potion's.</remarks>
public static class CurePoisonPotion
{
    public static readonly Guid Id = new("7c3e9a1d-4b6f-4e2a-8d1c-000000000032");

    /// <summary>5 minutes -- fits ushort (18000 &lt; 65535).</summary>
    private const ushort ImmunityDurationFrames = 5 * 60 * GameTiming.FramesPerSecond;

    public static ItemDefinition Build() => new(
        Id, "Cure Poison Potion", "HealthPotion", "c", Color.LimeGreen,
        Tags: [GameTags.EffectHealing, GameTags.TargetingSelf],
        Effects: [new Effect([
            new StatusEffectRemoval(StatusEffectType.Poison),
            new StatusEffectImmunityGrant(StatusEffectType.Poison, ImmunityDurationFrames),
        ])],
        Description: "Poisoned yourself by chugging potions faster than your liver could file the paperwork? The cure is, naturally, another potion. " +
            "Whether that counts as learning your lesson is between you and your insides.",
        Summary: "Removes all Poison, then grants immunity to Poison for 5 minutes.",
        GoldValue: 50,
        Activator: new PotionActivator(
            new TargetingSpec(Shape: TargetShape.Burst, Range: 3, AreaSize: 1),
            new ActionTiming(ActionTimingCategory.Immediate, CooldownFrames: null)));
}
