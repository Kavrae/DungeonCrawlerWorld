using Engine.Utilities;
using Game.Modules.AbilityScores;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Core.Components;
using Game.Modules.Health;
using Game.Modules.NpcBehavior.Behaviors;
using Microsoft.Xna.Framework;

namespace Game.Blueprints.NPCs.Generic;

/// <summary>A stationary training dummy for practising Dodge: it winds up Power Attack at whatever hostile stands beside it, then waits out a long cooldown.</summary>
/// <remarks>
/// The first stationary creature that fights: no MovementComponent, so MovementSystem never visits it, but a race of its
/// own -- hostility is a different race -- and one behavior, EngageMelee, so NpcDecisionSystem decides for it like any
/// creature. It swings only with a hostile beside it, and anything of another race beside it is hostile to it and it to
/// them. High Constitution for a fast health regen, so repeated test hits need no respawn; Power Attack is its only action.
/// </remarks>
public static class TestDummyBlueprint
{
    public static readonly Guid Id = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000105");

    public const string Name = "TestDummy";

    private const string Description = "A stationary training dummy. Winds up Power Attack at whoever stands beside it -- stand adjacent and practice dodging it.";

    private const string Glyph = "t";

    private const ushort MaximumHealth = 200;

    /// <summary>Flat default for every other NPC race (see TODO.md's Stats entry) -- this dummy only needs Constitution raised for regen, not the rest.</summary>
    private const ushort DefaultAbilityScoreBaseValue = 5;

    /// <summary>Maxes SimpleHealthRegenSystem's own Constitution-scaled regen ramp (MaxHealthRegenPerSecond at total 300) -- a training dummy should shrug off repeated test hits without needing a respawn.</summary>
    private const ushort HighRegenConstitutionBaseValue = 300;

    /// <summary>How long this dummy waits after Power Attack's effect actually lands before it can act again.</summary>
    private const float IdleSecondsAfterAttack = 3f;

    public static readonly BlueprintDefinition Definition = new(Id, Name)
    {
        Build = Build,
        UsesActions = [BuiltInActionIds.PowerAttack],
        Race = new RaceFacet(),
        Behaviors = [new(BuiltInBehaviorIds.EngageMelee, Rank: 3)],
        Appearance = new() { Name = Name, Description = Description, Glyph = Glyph, GlyphColor = Color.Purple, SpriteName = AppearanceFacet.NoSprite }
    };

    private static void Build(BlueprintContext context)
    {
        var componentManager = context.ComponentManager;
        var entityId = context.EntityId;

        MaximumHealthShift.MergeBaseMaximum(componentManager, entityId, MaximumHealth);
        componentManager.Merge(entityId, new ActionLockComponent(currentLockTotalFrames: 0, unlockedAtFrame: 0));

        foreach (var abilityScoreType in Enum.GetValues<AbilityScoreType>())
        {
            AbilityScoreEffects.Grant(componentManager, entityId, abilityScoreType,
                abilityScoreType == AbilityScoreType.Constitution ? HighRegenConstitutionBaseValue : DefaultAbilityScoreBaseValue);
        }

        ActionGrantEffects.Grant(componentManager, context.Actions, entityId, BuiltInActionIds.PowerAttack, overrideDefinition: context.Actions.GetOrCreateDerived("TestDummy.PowerAttack", BuiltInActionIds.PowerAttack, PowerAttackWithIdleCooldown));
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
    private static ActionDefinition PowerAttackWithIdleCooldown(ActionDefinition baseAction)
    {
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
