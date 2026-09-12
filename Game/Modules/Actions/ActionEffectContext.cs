using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Events;
using Engine.Math;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Actions.Components;
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
/// ChainedEffect, guarding against a proc that (directly or via a longer cycle) triggers
/// itself. DurationScaleMultiplier defaults to 1.0 (no scaling); ConsumableActivationSystem sets
/// it to a caster-Intelligence-derived value for a ScrollActivator activation (see
/// ScrollScalingEffects) -- a duration-bearing entry (StatModifierGrant today) multiplies
/// its own base duration by this rather than the System pre-computing an absolute frame count.
///
/// Now is the simulation frame the activation happens on -- required, not defaulted, because an
/// entry that starts a timer (DodgeActivation, AuraSourceGrant, a status effect) writes an
/// absolute deadline from it (FrameDeadline.After(context.Now, frames)), and a silently-wrong
/// default would schedule that deadline from the wrong frame.
/// </summary>
public sealed record ActionEffectContext(
    int SourceEntityId,
    int TargetEntityId,
    PackedComponentPool<SimpleHealthComponent> Health,
    EventBus EventBus,
    MathUtility MathUtility,
    ComponentManager ComponentManager,
    string ActivatorName,
    IReadOnlyList<Tag> ActivatorTags,
    long Now,
    MultiComponentPool<StatModifierComponent>? StatModifiers = null,
    MultiComponentPool<AbilityScoreComponent>? AbilityScores = null,
    PackedComponentPool<ManaComponent>? Mana = null,
    PackedComponentPool<HotkeyExpansionUnlockComponent>? HotkeyExpansionUnlocks = null,
    StatusEffectAuraApplierRegistry? StatusEffectAppliers = null,
    PackedComponentPool<DeadComponent>? DeadEntities = null,
    MultiComponentPool<StatusEffectAuraSourceComponent>? AuraSources = null,
    MultiComponentPool<BodyPartComponent>? BodyParts = null,
    IPlayerQuery? PlayerQuery = null,
    float DurationScaleMultiplier = 1.0f,
    byte ChainDepth = 0);
