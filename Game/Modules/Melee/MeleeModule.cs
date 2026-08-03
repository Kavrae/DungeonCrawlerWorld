using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Modules.Abilities;
using Game.Modules.StatusEffects;

namespace Game.Modules.Melee;

/// <summary>
/// Registers the single, shared "Default Attack" ability definition every race/class grants a
/// baseline instance of (see the race/class blueprints, and DamageAmount living on the granted
/// instance rather than here -- AbilityInstanceComponent's own doc comment explains why) -- the
/// fallback melee attack for an entity that doesn't have a unique melee ability of its own. No
/// components or systems of its own: activation, effect resolution, and targeting are all
/// generic Abilities-module machinery (see AbilitiesModule) that any ability, melee or
/// otherwise, shares.
///
/// Grants Paralysis (Game.Modules.Paralysis) on every hit -- the concrete proof that
/// AbilityEffectResolver's StatusEffects grant path actually works end to end, chosen here
/// rather than only in a throwaway test ability since it's directly exercisable by attacking
/// anything.
/// </summary>
public sealed class MeleeModule : IGameModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-00000000000d");

    public IReadOnlyList<Type> Dependencies { get; } = [];

    public static readonly Guid DefaultAttackId = new("3f6e9c2a-8b4d-47a1-9c3e-5d2f7b1a6c9e");

    private const short DefaultAttackActionLockFrames = 60;

    public void Configure(GameModuleContext context)
    {
        // TEMPORARY: Line/length 2 instead of the intended Adjacent shape, to exercise
        // Line-shape targeting highlights (Phase 7) -- revert to Adjacent once that's verified.
        context.Abilities.Register(new AbilityDefinition(
            DefaultAttackId,
            "Default Attack",
            "/",
            new AbilityTargeting(TargetShape.Line, Range: 2),
            new AbilityTiming(ActionTimingCategory.Immediate, ActionLockFrames: DefaultAttackActionLockFrames, CooldownFrames: null),
            new AbilityEffect(DamageAmount: 0, StatusEffects: [StatusEffectType.Paralysis], StatModifierGrants: [])));
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
