using Engine.Utilities;
using Game.Modules.AbilityScores;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Definitions.DirectActions;
using Game.Modules.Core.Components;
using Game.Modules.Health.Components;
using Game.Modules.NpcBehavior.Components;
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
/// </summary>
public static class TestDummyBlueprint
{
    public static readonly Guid Id = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000105");

    public const string Name = "TestDummy";

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

    public static readonly BlueprintDefinition Definition = new(Id, Name)
    {
        Build = Build,
        Appearance = new() { Name = Name, Description = Description, Glyph = Glyph, GlyphColor = Color.Purple, SpriteName = AppearanceFacet.NoSprite }
    };

    private static void Build(BlueprintContext context)
    {
        var componentManager = context.ComponentManager;
        var entityId = context.EntityId;

        componentManager.Merge(entityId, new SimpleHealthComponent(MaximumHealth, MaximumHealth));
        componentManager.Merge(entityId, new ActionLockComponent(standardLockFrames: StandardLockFrames, currentLockTotalFrames: 0, unlockedAtFrame: 0));
        componentManager.Merge(entityId, new TestDummyComponent());

        foreach (var abilityScoreType in Enum.GetValues<AbilityScoreType>())
        {
            AbilityScoreEffects.Grant(componentManager, entityId, abilityScoreType,
                abilityScoreType == AbilityScoreType.Constitution ? HighRegenConstitutionBaseValue : DefaultAbilityScoreBaseValue);
        }

        ActionGrantEffects.Grant(componentManager, context.Actions, entityId, PowerAttackAction.Id, overrideDefinition: BuildPowerAttackWithIdleCooldown());
    }

    /// <summary>
    /// ActionInstanceComponent.CooldownReadyAtFrame starts counting the instant activation
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
