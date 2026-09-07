using Engine.ECS.Components;
using Engine.Math;
using Engine.Utilities;
using Game.Modules.AbilityScores;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Definitions.DirectActions;
using Game.Modules.Core.Components;
using Game.Modules.Health.Components;
using Game.Modules.NpcBehavior.Components;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.Race.Components;
using Microsoft.Xna.Framework;

namespace Game.Blueprints.NPCs.Generic;

/// <summary>
/// A dedicated, minimal fixture for manually testing Dodge timing against a predictable,
/// non-chasing attacker -- not a real race. Stationary (no MovementComponent at all, so
/// MovementSystem/TestCombatBehaviorSystem never visit it), high Constitution for a fast health
/// regen (repeated test hits don't need a respawn), and only PowerAttackAction granted -- see
/// TestDummyAttackSystem for the unconditional "attack the instant the shared lock clears" trigger
/// this pairs with (deliberately not TestCombatBehaviorSystem's engage/chase logic, which a
/// stationary dummy has no use for).
///
/// Explicitly granted ProcessingTierComponent(Local): ProcessingTierSystem's own membership is
/// driven off MovementComponent (see its own doc comment) -- an entity with none, like this one,
/// is *never* visited by it and so never gets a real tier computed at all, permanently reading as
/// the Beyond fallback (ProcessingTierWiring's own "fail open to Beyond" default for "no component
/// yet") to every *other* tiered consumer. That's a real, confirmed bug: ActionLockSystem/
/// ActionCooldownSystem/SimpleHealthRegenSystem are all tiered off this same component, and each
/// decrements its own countdown by a flat per-visit amount that assumes Local's cadence -- at
/// Beyond's 8x-longer-between-visits cadence (ProcessingTierDivisors.ByTierIndex), the same flat
/// decrement makes every one of those countdowns (Power Attack's own windup/cooldown, this dummy's
/// own regen) run roughly 8x slower than intended. Hardcoding Local here (this dummy always spawns
/// right next to the player and never moves, so it's never wrong in practice) sidesteps the whole
/// class of bug rather than granting a MovementComponent purely to be tracked, which would pull in
/// MovementSystem/TestCombatBehaviorSystem machinery this stationary fixture has no use for.
/// </summary>
public sealed class TestDummyBlueprint : IBlueprint
{
    private const string Name = "TestDummy";

    private const string Description = "A stationary training dummy. Winds up Power Attack on its own cooldown -- stand adjacent and practice dodging it.";

    private const string Glyph = "t";

    private const ushort MaximumHealth = 200;

    /// <summary>Flat default for every other NPC race (see TODO.md's Stats entry) -- this dummy only needs Constitution raised for regen, not the rest.</summary>
    private const ushort DefaultAbilityScoreBaseValue = 5;

    /// <summary>Maxes SimpleHealthRegenSystem's own Constitution-scaled regen ramp (MaxHealthRegenPerSecond at total 300) -- a training dummy should shrug off repeated test hits without needing a respawn.</summary>
    private const ushort HighRegenConstitutionBaseValue = 300;

    /// <summary>Moderate, race-typical lock -- this dummy's own "speed" only ever matters for how often it re-fires Power Attack (TestDummyAttackSystem), not movement.</summary>
    private const ushort StandardLockFrames = 48;

    /// <summary>How long this dummy waits after Power Attack's effect actually lands before it can act again.</summary>
    private const float IdleSecondsAfterAttack = 3f;

    public void Build(ComponentManager componentManager, int entityId)
    {
        componentManager.Merge(entityId, new DisplayTextComponent( Name, Description));
        componentManager.Merge(entityId, new GlyphComponent(Glyph, Color.Purple));
        componentManager.Merge(entityId, new SimpleHealthComponent(MaximumHealth, MaximumHealth));
        componentManager.Merge(entityId, new ActionLockComponent(standardLockFrames: StandardLockFrames, currentLockTotalFrames: 0, currentLockFramesRemaining: 0));
        componentManager.Merge(entityId, new TransformComponent(new Vector3Int(-1, -1, (int)MapLayer.Ground), new Vector2Byte(1, 1)));
        componentManager.Merge(entityId, new TestDummyComponent());
        componentManager.Merge(entityId, new ProcessingTierComponent(ProcessingTierLevel.Local));

        foreach (var abilityScoreType in Enum.GetValues<AbilityScoreType>())
        {
            AbilityScoreEffects.Grant(componentManager, entityId, abilityScoreType,
                abilityScoreType == AbilityScoreType.Constitution ? HighRegenConstitutionBaseValue : DefaultAbilityScoreBaseValue);
        }

        ActionGrantEffects.Grant(componentManager, entityId, PowerAttackAction.Id, manaCost: 0, overrideDefinition: BuildPowerAttackWithIdleCooldown(), cooldownFramesRemaining: 0);
    }

    /// <summary>
    /// ActionInstanceComponent.CooldownFramesRemaining starts counting the instant activation
    /// begins (ActionActivationSystem.StartCooldownIfAny fires the same tick TryActivateDelayed
    /// sets the windup lock, not once DelayedActionSystem later applies the effect) -- so granting
    /// PowerAttack's own CooldownFrames as just IdleSecondsAfterAttack would only leave
    /// (IdleSecondsAfterAttack - windup) of *actual* idle time once the attack lands. Adding the
    /// windup back in is what makes the observed post-completion idle exactly
    /// IdleSecondsAfterAttack, and keeps tracking correctly if PowerAttackAction's own windup ever
    /// changes.
    /// </summary>
    private static ActionDefinition BuildPowerAttackWithIdleCooldown()
    {
        var baseAction = PowerAttackAction.Build();
        if (baseAction.Activator is not DirectAction directAction)
        {
            return baseAction;
        }

        var windupFrames = directAction.Timing.ActionLockFrames ?? 0;
        var idleFrames = GameTiming.FramesForSeconds(IdleSecondsAfterAttack);
        var cooldownFrames = (ushort)(windupFrames + idleFrames);

        return baseAction with { Activator = directAction with { Timing = directAction.Timing with { CooldownFrames = cooldownFrames } } };
    }
}
