using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Modules.StatModifiers;

namespace Game.Modules.Abilities;

/// <summary>
/// TEMPORARY: registers two player-only test abilities exercising the stat-modifier system
/// (TargetShape.Self, StatModifierGrants) while it's being built -- not real game content.
/// QuickCast (FreeCast, no target, 10 second cooldown -- prevents restacking the buffs/debuffs on
/// top of themselves every frame) grants the caster four simultaneous OutgoingDamage modifiers
/// at once (+5 additive buff, -2 additive debuff, +100% multiplicative buff, -20% multiplicative
/// debuff -- deliberately mixed additive/multiplicative and buff/debuff, to exercise
/// StatModifierMath combining all four together rather than just one modifier at a time) for 10
/// seconds. Ranged Test Debuff Bolt (Delayed, Burst) deals 20 damage and grants every entity it
/// hits -100% HealthRegen for 5 seconds, on a 10 second cooldown. Remove once real self-buff/
/// debuff abilities supersede these, the same lifecycle PlayerTestAbilitiesModule's own doc
/// comment describes.
/// </summary>
public sealed class QuickCastTestModule : IGameModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000013");

    public IReadOnlyList<Type> Dependencies { get; } = [];

    public static readonly Guid QuickCastAbilityId = new("2b6d4f8a-1c3e-4a5d-9f7b-6e8c1a3d5f9b");
    public static readonly Guid RangedTestDebuffAbilityId = new("8e4a2c6f-3b5d-4e7a-8c1f-9d3b5a7c2e4f");

    private const int QuickCastBuffDurationFrames = 600;
    private const short QuickCastCooldownFrames = 600;

    // See StatModifierMath's own doc comment: a multiplicative Magnitude is the decimal delta
    // from 1.0 (+100% = 1.0, -20% = -0.2), while an additive Magnitude is a flat amount (+5, -2).
    private const float QuickCastFlatDamageBuff = 5f;
    private const float QuickCastFlatDamageDebuff = -2f;
    private const float QuickCastDamageMultiplierBuff = 1f;
    private const float QuickCastDamageMultiplierDebuff = -0.2f;

    private const int RangedTestDebuffRange = 10;
    private const int RangedTestDebuffAreaSize = 4;
    private const short RangedTestDebuffActionLockFrames = 60;
    private const short RangedTestDebuffCooldownFrames = 600;
    private const short RangedTestDebuffDamage = 20;
    private const int RangedTestDebuffDurationFrames = 300;

    public void Configure(GameModuleContext context)
    {
        context.Abilities.Register(new AbilityDefinition(
            QuickCastAbilityId,
            "Quick Cast",
            "+",
            new AbilityTargeting(TargetShape.Self, Range: 0),
            new AbilityTiming(ActionTimingCategory.FreeCast, ActionLockFrames: 0, CooldownFrames: QuickCastCooldownFrames),
            new AbilityEffect(DamageAmount: 0, StatusEffects: [], StatModifierGrants:
            [
                new StatModifierGrant(StatModifierTarget.OutgoingDamage, StatModifierOperation.Additive, StatModifierPolarity.Buff,
                    CanModify: true, Magnitude: QuickCastFlatDamageBuff, DurationFrames: QuickCastBuffDurationFrames),
                new StatModifierGrant(StatModifierTarget.OutgoingDamage, StatModifierOperation.Additive, StatModifierPolarity.Debuff,
                    CanModify: true, Magnitude: QuickCastFlatDamageDebuff, DurationFrames: QuickCastBuffDurationFrames),
                new StatModifierGrant(StatModifierTarget.OutgoingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
                    CanModify: true, Magnitude: QuickCastDamageMultiplierBuff, DurationFrames: QuickCastBuffDurationFrames),
                new StatModifierGrant(StatModifierTarget.OutgoingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Debuff,
                    CanModify: true, Magnitude: QuickCastDamageMultiplierDebuff, DurationFrames: QuickCastBuffDurationFrames),
            ])));

        context.Abilities.Register(new AbilityDefinition(
            RangedTestDebuffAbilityId,
            "Ranged Test Debuff Bolt",
            "%",
            new AbilityTargeting(TargetShape.Burst, RangedTestDebuffRange, RangedTestDebuffAreaSize),
            new AbilityTiming(ActionTimingCategory.Delayed, ActionLockFrames: RangedTestDebuffActionLockFrames, CooldownFrames: RangedTestDebuffCooldownFrames),
            new AbilityEffect(DamageAmount: RangedTestDebuffDamage, StatusEffects: [], StatModifierGrants:
            [
                new StatModifierGrant(StatModifierTarget.HealthRegen, StatModifierOperation.Multiplicative, StatModifierPolarity.Debuff,
                    CanModify: true, Magnitude: -1, DurationFrames: RangedTestDebuffDurationFrames),
            ])));
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
