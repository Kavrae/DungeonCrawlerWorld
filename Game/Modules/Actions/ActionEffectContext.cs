using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Events;
using Engine.Math;
using Game.Modules.Actions.Components;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.Modules.Mana.Components;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffectAura.Components;
using Game.Modules.StatusEffects;
using Game.World;

namespace Game.Modules.Actions;

/// <summary>
/// Everything an IActionEffectEntry might need to apply itself to one target. Built once per
/// activation with the source-side fields fixed, then varied per target via
/// `context with { TargetEntityId = id }`. MathUtility is required -- unlike the feature-gated
/// pools below, it's a base Engine utility always available at composition time, the same way
/// Health/EventBus already are. ChainDepth defaults to 0 and is only ever incremented by
/// ChainedEffectEntry, guarding against a proc that (directly or via a longer cycle) triggers
/// itself.
/// </summary>
public sealed record ActionEffectContext(
    int SourceEntityId,
    int TargetEntityId,
    PackedComponentPool<HealthComponent> Health,
    EventBus EventBus,
    MathUtility MathUtility,
    ComponentManager ComponentManager,
    string ActivatorName,
    IReadOnlyList<Tag> ActivatorTags,
    MultiComponentPool<StatModifierComponent>? StatModifiers = null,
    MultiComponentPool<AbilityScoreComponent>? AbilityScores = null,
    PackedComponentPool<ManaComponent>? Mana = null,
    PackedComponentPool<HotkeyExpansionUnlockComponent>? HotkeyExpansionUnlocks = null,
    StatusEffectAuraApplierRegistry? StatusEffectAppliers = null,
    PackedComponentPool<DeadComponent>? DeadEntities = null,
    PackedComponentPool<StatusEffectAuraSourceComponent>? AuraSources = null,
    IPlayerQuery? PlayerQuery = null,
    short? DamageOverride = null,
    int ChainDepth = 0);
