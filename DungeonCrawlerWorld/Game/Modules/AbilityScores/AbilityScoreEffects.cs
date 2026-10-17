using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Events;
using Game.Modules.AbilityScores.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Game.Modules.AbilityScores;

/// <summary>
/// Write surface for ability scores -- mirrors StatModifierEffects' static style, fetching its
/// own pools from ComponentManager rather than taking them as parameters. Grant/GrantDefaults
/// set up an entity's starting scores (blueprint-build time); GrantModifier is what any future
/// consumer (equipment, level-up, buffs -- see TODO.md) must call instead of raw
/// StatModifierEffects.Apply when the target is an ability score, so Total stays precomputed
/// (see AbilityScoresComponent's own doc comment for why this is eager, not StatModifierMath's
/// lazy-on-read convention). The other half of that guarantee -- keeping Total in sync when a
/// temporary ability-score modifier expires -- lives in AbilityScoresModule's
/// StatModifierExpiredEvent subscription, which goes through RecomputeIfAbilityScore below so
/// the actual recompute logic (RecomputeAbilityScore) exists exactly once.
/// </summary>
public static class AbilityScoreEffects
{
    private static readonly AbilityScoreType[] AllTypes = Enum.GetValues<AbilityScoreType>();

    public static void Grant(ComponentManager componentManager, int entityId, AbilityScoreType type, ushort baseValue)
    {
        var clampedBase = AbilityScoreMath.ClampBaseValue(baseValue);
        var total = AbilityScoreMath.ComputeTotal(StatModifiersOf(componentManager), entityId, type, clampedBase);

        Write(componentManager.GetPackedPool<AbilityScoresComponent>(), entityId, (type, clampedBase, total), static (ref AbilityScoresComponent scores, (AbilityScoreType Type, ushort Base, ushort Total) granted) =>
            scores.Set(granted.Type, granted.Base, granted.Total));
    }

    public static void GrantDefaults(ComponentManager componentManager, int entityId, ushort baseValue)
    {
        var clampedBase = AbilityScoreMath.ClampBaseValue(baseValue);
        var statModifiers = StatModifiersOf(componentManager);

        var granted = default(AbilityScoresComponent);
        foreach (var type in AllTypes)
        {
            granted.Set(type, clampedBase, AbilityScoreMath.ComputeTotal(statModifiers, entityId, type, clampedBase));
        }

        componentManager.GetPackedPool<AbilityScoresComponent>().Merge(entityId, granted);
    }

    /// <summary>
    /// Entry point for any future code (equipment, level-up, buffs -- see TODO.md) granting a
    /// modifier that targets an ability score. Takes AbilityScoreType rather than
    /// StatModifierTarget on purpose: a modifier that doesn't target an ability score has no
    /// business going through this class at all -- callers granting e.g. an IncomingDamage
    /// modifier should call StatModifierEffects.Apply directly, the same as every other
    /// existing call site does. Restricting the parameter this way makes it impossible to call
    /// this method for a target RecomputeAbilityScore couldn't do anything with.
    /// </summary>
    public static void GrantModifier(
        ComponentManager componentManager,
        int entityId,
        AbilityScoreType type,
        StatModifierOperation operation,
        StatModifierPolarity polarity,
        bool canModify,
        float magnitude,
        uint expiresAtFrame,
        ActionSource source)
    {
        StatModifierEffects.Apply(componentManager, entityId, AbilityScoreMath.ToStatModifierTarget(type), operation, polarity, canModify, magnitude, expiresAtFrame, source);
        RecomputeAbilityScore(componentManager, entityId, type);
    }

    /// <summary>
    /// Entry point for any future code that permanently raises an entity's base ability score
    /// after creation -- level-up, an "item of divine suffering", etc. (see TODO.md) -- as
    /// opposed to GrantModifier, which only ever layers a temporary/removable modifier on top of
    /// BaseValue. Publishes AbilityScoreBaseValueChangedEvent so the base-score milestone
    /// achievements (Game/Modules/Achievements/Definitions/) can react without polling every
    /// entity's ability scores every frame. No-ops if the entity was never granted that score.
    /// </summary>
    public static void SetBaseValue(ComponentManager componentManager, EventBus eventBus, int entityId, AbilityScoreType type, ushort newBaseValue)
    {
        var abilityScores = componentManager.GetPackedPool<AbilityScoresComponent>();
        var denseIndex = abilityScores.GetDenseIndex(entityId);
        if (denseIndex < 0 || !abilityScores.GetReadonlyByDenseIndex(denseIndex).Has(type))
        {
            return;
        }

        var clampedBase = AbilityScoreMath.ClampBaseValue(newBaseValue);
        var newTotal = AbilityScoreMath.ComputeTotal(StatModifiersOf(componentManager), entityId, type, clampedBase);

        abilityScores.UpdateByDenseIndex(denseIndex, (type, clampedBase, newTotal), static (ref AbilityScoresComponent scores, (AbilityScoreType Type, ushort Base, ushort Total) values) =>
            scores.Set(values.Type, values.Base, values.Total));
        eventBus.Publish(new AbilityScoreBaseValueChangedEvent(entityId, type, clampedBase));
    }

    /// <summary>
    /// Maps target to an AbilityScoreType and recomputes if it is one, a no-op otherwise --
    /// the bridge AbilityScoresModule's StatModifierExpiredEvent subscription needs, since that
    /// event is generic (published for every expired modifier, not just ability-score ones) and
    /// has no equivalent of GrantModifier's compile-time restriction to lean on.
    /// </summary>
    public static void RecomputeIfAbilityScore(ComponentManager componentManager, int entityId, StatModifierTarget target)
    {
        var type = AbilityScoreMath.FromStatModifierTarget(target);
        if (type is not null)
        {
            RecomputeAbilityScore(componentManager, entityId, type.Value);
        }
    }

    /// <summary>
    /// Recomputes and stores Total for one ability score, if the entity was granted it -- shared
    /// by GrantModifier (called inline, right after adding the modifier, with a type it already
    /// knows) and by RecomputeIfAbilityScore above (called once StatModifiersModule's expiry
    /// event resolves to a type).    /// </summary>
    private static void RecomputeAbilityScore(ComponentManager componentManager, int entityId, AbilityScoreType type)
    {
        var abilityScores = componentManager.GetPackedPool<AbilityScoresComponent>();
        var denseIndex = abilityScores.GetDenseIndex(entityId);
        if (denseIndex < 0 || !abilityScores.GetReadonlyByDenseIndex(denseIndex).TryGet(type, out var score))
        {
            return;
        }

        var newTotal = AbilityScoreMath.ComputeTotal(StatModifiersOf(componentManager), entityId, type, score.BaseValue);
        abilityScores.UpdateByDenseIndex(denseIndex, (type, newTotal), static (ref AbilityScoresComponent scores, (AbilityScoreType Type, ushort Total) values) =>
            scores.SetTotal(values.Type, values.Total));
    }

    private static MultiComponentPool<StatModifierComponent> StatModifiersOf(ComponentManager componentManager) =>
        componentManager.GetMultiPool<StatModifierComponent>();

    private static void Write<TState>(PackedComponentPool<AbilityScoresComponent> abilityScores, int entityId, TState state, PackedComponentPool<AbilityScoresComponent>.ComponentUpdater<TState> updater)
    {
        if (abilityScores.TryUpdate(entityId, state, updater))
        {
            return;
        }

        var scores = default(AbilityScoresComponent);
        updater(ref scores, state);
        abilityScores.Add(entityId, scores);
    }
}
