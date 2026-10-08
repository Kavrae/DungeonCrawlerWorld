using Engine.Math;
using Engine.Utilities;
using Game.Effects;
using Game.Effects.Entries;
using Game.Modules.Actions.Activators;
using Game.Modules.StatusEffects;
using Game.Tags;
using Microsoft.Xna.Framework;

namespace Game.Modules.Actions.Definitions.Spells;

/// <summary>A slow ranged burst of fire: a 3-second windup, then the Wand of Fireball's damage and Burning on everyone in the blast.</summary>
/// <remarks>Delayed, so it is where targeting modes show: in Target mode the blast follows whoever was marked through the windup, in Ground mode it lands where it was aimed.</remarks>
public static class FireballAction
{
    public static readonly Guid Id = new("6a1f3c8e-2b7d-4e95-a0c4-9d8e7f1b3a52");

    public const ushort ManaCost = 15;

    private const int Range = 10;
    private const int AreaSize = 3;
    private const short MinDamage = 25;
    private const short MaxDamage = 35;
    private const int BurningStacks = 5;

    private static readonly ushort WindupFrames = GameTiming.FramesForSeconds(3f);

    public static ActionDefinition Build() => new(
        Id, "Fireball", "Wand", "F", Color.OrangeRed,
        Tags: [GameTags.DeliveryRanged, GameTags.ActionAttack, GameTags.DamageFire],
        Effects: [new Effect([new DirectDamage(MinDamage, MaxDamage), new StatusEffectGrant(StatusEffectType.Burning, StackCount: BurningStacks)])],
        Activator: new SpellActivator(
            new TargetingSpec(TargetShape.Burst, Range, AreaSize),
            new ActionTiming(ActionTimingCategory.Delayed, ActionLockFrames: WindupFrames, CooldownFrames: null)),
        Description: "A ball of fire gathered over three long seconds, then hurled to burst over a wide area, scorching everything caught in it and leaving it burning.",
        Summary: "Slow fire burst that inflicts Burning.")
    {
        ActivationEffects = [new Effect([new ManaDrain(ManaCost)])],
    };
}
