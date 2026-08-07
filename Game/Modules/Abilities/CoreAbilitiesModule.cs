using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Math;
using Engine.Utilities;
using Microsoft.Xna.Framework;

namespace Game.Modules.Abilities;

/// <summary>
/// Registers the first real, permanent ability catalog -- race/class-agnostic abilities any
/// entity can be granted. A flat catalog rather than one module per tag, since tags are
/// multi-valued (e.g. Punch is Melee+Unarmed+Attack, with no single tag-module it would belong
/// to) -- see PlayerBlueprint and the race blueprints for where these are granted.
/// </summary>
public sealed class CoreAbilitiesModule : IGameModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000017");

    public IReadOnlyList<Type> Dependencies { get; } = [];

    public static readonly Guid HealId = new("3f6e9c2a-8b4d-47a1-9c3e-5d2f7b1a6c9e");
    public static readonly Guid PunchId = new("7a1c3e5f-9b2d-4c6a-8e1f-3d5b7a9c2e4f");
    public static readonly Guid MagicMissileId = new("2b6d4f8a-1c3e-4a5d-9f7b-6e8c1a3d5f9b");

    public const short HealManaCost = 2;
    public const short MagicMissileManaCost = 5;

    public void Configure(GameModuleContext context)
    {
        context.Abilities.Register(new AbilityDefinition(
            HealId, "Heal", "h",
            new TargetingSpec(TargetShape.Self, Range: 0),
            new AbilityTiming(ActionTimingCategory.Immediate, ActionLockFrames: (short)GameTiming.FramesForSeconds(1f), CooldownFrames: null),
            new AbilityEffect(DamageAmount: 0, StatusEffects: [], HealFraction: 0.2f),
            ManaCost: HealManaCost,
            Description: "The user glows red while casting and immediately recovers up to 20% of their maximum health. This spell does not level up.",
            Summary: "Heals 20% of Max Health",
            SpriteName: "Spell-Weak",
            GlyphColor: Color.Red,
            Tags: [Tag.Healing, Tag.Self, Tag.Spell]));

        context.Abilities.Register(new AbilityDefinition(
            PunchId, "Punch", "p",
            new TargetingSpec(TargetShape.Adjacent, Range: 0),
            new AbilityTiming(ActionTimingCategory.Immediate, ActionLockFrames: (short)GameTiming.FramesForSeconds(1f), CooldownFrames: null),
            new AbilityEffect(DamageAmount: 10, StatusEffects: []),
            Description: "It doesn't get any more simple than this. Just keep hitting the target with your bare fists until they stop moving. This ability is primarily modified by the Bare Knuckle skill.",
            Summary: "Basic melee attack",
            SpriteName: "Punch",
            GlyphColor: Color.Black,
            Tags: [Tag.Melee, Tag.Unarmed, Tag.Attack]));

        context.Abilities.Register(new AbilityDefinition(
            MagicMissileId, "Magic Missile", "m",
            new TargetingSpec(TargetShape.SingleTarget, Range: 20),
            new AbilityTiming(ActionTimingCategory.Immediate, ActionLockFrames: (short)GameTiming.FramesForSeconds(1f), CooldownFrames: null),
            new AbilityEffect(DamageAmount: 25, StatusEffects: []),
            ManaCost: MagicMissileManaCost,
            Description: "A basic single target ranged attack spell that shoots hot laser bolts from the caster's eyes, one bolt after another.",
            Summary: "Single target ranged attack.",
            SpriteName: "Magic Missile",
            GlyphColor: Color.Black,
            Tags: [Tag.Ranged, Tag.Attack, Tag.Spell]));
    }

    public void RegisterComponents(ComponentManager componentManager)
    {
        // No components of its own -- see class doc comment.
    }

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
        // No systems of its own -- see class doc comment.
    }
}
