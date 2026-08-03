using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Math;

namespace Game.Modules.Abilities;

/// <summary>
/// TEMPORARY: registers a single player-only ranged test ability (Immediate, SingleTarget,
/// range 10) purely to exercise the ranged/SingleTarget targeting path and a second,
/// differently-bound hotkey alongside Default Attack (see PlayerBlueprint and the hotkey
/// binding work) while the targeting/hotkey UI is being built -- not real game content. Damage
/// (10) lives on the granted AbilityInstanceComponent, same as every other ability -- see that
/// component's own doc comment. Remove once a real ranged ability, or the "replace MeleeModule
/// with a general ability library" TODO item, supersedes it as a second, real consumer of the
/// ranged-targeting path.
/// </summary>
public sealed class PlayerTestAbilitiesModule : IGameModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-00000000000e");

    public IReadOnlyList<Type> Dependencies { get; } = [];

    public static readonly Guid RangedTestAbilityId = new("7a1c3e5f-9b2d-4c6a-8e1f-3d5b7a9c2e4f");

    private const int RangedTestAbilityRange = 10;
    private const int RangedTestAbilityAreaSize = 3;
    private const short RangedTestAbilityActionLockFrames = 60;

    public void Configure(GameModuleContext context)
    {
        // TEMPORARY: Burst/size 3 instead of the intended SingleTarget, to exercise Burst-shape
        // targeting highlights (Phase 7) -- revert to SingleTarget once that's verified.
        context.Abilities.Register(new AbilityDefinition(
            RangedTestAbilityId,
            "Ranged Test Bolt",
            "*",
            new AbilityTargeting(TargetShape.Burst, RangedTestAbilityRange, RangedTestAbilityAreaSize),
            new AbilityTiming(ActionTimingCategory.Immediate, ActionLockFrames: RangedTestAbilityActionLockFrames, CooldownFrames: null),
            new AbilityEffect(DamageAmount: 10, StatusEffects: [], StatModifierGrants: [])));
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
