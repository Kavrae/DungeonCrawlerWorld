using Engine.Math;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Effects;
using Microsoft.Xna.Framework;

namespace Game.Modules.Actions.Definitions.Spells;

/// <summary>The starter Heal spell -- see ActionDefinition/SpellActivator's own doc comments for the shape.</summary>
public static class HealAction
{
    public static readonly Guid Id = new("3f6e9c2a-8b4d-47a1-9c3e-5d2f7b1a6c9e");
    public const ushort ManaCost = 2;

    public static ActionDefinition Build() => new(
        Id, "Heal", "Spell-Weak", "h", Color.Red,
        Tags: [Tag.Healing, Tag.Self, Tag.Spell],
        Effects: [new ActionEffect([new DirectHeal(0.2f)])],
        Activator: new SpellActivator(
            new TargetingSpec(TargetShape.Self, Range: 0),
            new ActionTiming(ActionTimingCategory.Immediate, CooldownFrames: null),
            ManaCost),
        Description: "The user glows red while casting and immediately recovers up to 20% of their maximum health. This spell does not level up.",
        Summary: "Heals 20% of Max Health");
}
