using Engine.ECS.Components;
using Engine.Modules;
using Game.Blueprints;
using Game.Modules.AbilityScores;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Systems;
using Game.Modules.BodyPartEffects;
using Game.Modules.BodyPartEffects.Components;
using Game.Modules.Core;
using Game.Modules.Core.Components;
using Game.Modules.Death;
using Game.Modules.Health;
using Game.Modules.Mana;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.Race;
using Game.Modules.StatModifiers;
using Game.Modules.Auras;
using Game.World;

namespace Game.Modules.Actions;

/// <summary>
/// Doesn't require StatusEffectsModule:
/// GameModuleContext.StatusEffectAppliers is always a live, shared registry regardless of
/// which effect modules (if any) are loaded -- ActionEffectResolver's StatusEffects grant is a
/// graceful no-op (TryGet returning false) for any StatusEffectType nothing registered an
/// applier for.
///
/// Also owns PotionCooldownComponent/PotionCooldownSystem (Game.Modules.Actions.Activators/
/// Systems) -- that bookkeeping is a property of a PotionActivator-kind activation happening, not
/// of Inventory storage/stacking, so it lives with the rest of the activation machinery here
/// rather than in InventoryModule. ScrollMasteryComponent gets the same treatment for the same
/// reason, one level up from PotionActivator specifically to ScrollActivator activations in
/// general. Scroll of Torch's own map-coloring effect, by contrast, lives entirely in
/// Game.Modules.Auras (AuraSourceGrant/AuraSourceExpiryComponent/
/// AuraSourceExpirySystem) -- it's a AuraSourceComponent grant like any other, not a
/// bespoke Actions-owned component, specifically so MapWindow never needs ability-specific
/// rendering knowledge (see AuraGlowView, which already draws any aura generically).
/// </summary>
public sealed class ActionsModule : IGameModule
{
    public static readonly Guid ModuleId = new("d9f6a1c4-8b2e-4f3a-9c1d-00000000000c");

    public Guid Id => ModuleId;

    public IReadOnlyList<Guid> Requires { get; } = [CoreModule.ModuleId, HealthModule.ModuleId, StatModifiersModule.ModuleId, DeathModule.ModuleId, ManaModule.ModuleId, AbilityScoresModule.ModuleId, AurasModule.ModuleId, BodyPartEffectsModule.ModuleId, ProcessingTierModule.ModuleId, RaceModule.ModuleId, BlueprintsModule.ModuleId];

    public void RegisterComponents(ComponentRegistration registration)
    {
        var componentManager = registration.ComponentManager;

        componentManager.RegisterMultiPool<ActionInstanceComponent>(initialCapacity: 64);

        // Sparse: written the first time an entity uses an action that has a cooldown at all.
        componentManager.RegisterMultiPool<ActionCooldownComponent>(initialCapacity: 64);
        componentManager.RegisterPackedPool<PendingDelayedActionComponent>(
            static (ref PendingDelayedActionComponent existing, PendingDelayedActionComponent incoming) => existing = incoming);
        componentManager.RegisterPackedPool<DodgingComponent>(
            static (ref DodgingComponent existing, DodgingComponent incoming) => existing = incoming);
        componentManager.RegisterPackedPool<PendingActionActivationComponent>(
            static (ref PendingActionActivationComponent existing, PendingActionActivationComponent incoming) => existing = incoming);
        // Player-only, 24 hotkey slots total -- dense capacity matches the slot count.
        componentManager.RegisterMultiPool<ActionHotkeyBindingComponent>(initialCapacity: 24);
        // Player-only, only 4 expansions exist.
        componentManager.RegisterPackedPool<HotkeyExpansionUnlockComponent>(
            static (ref existing, incoming) => existing = incoming, initialCapacity: 4);
        componentManager.RegisterPackedPool<PotionCooldownComponent>(static (ref existing, incoming) => existing = incoming);
        // Player-only, exceedingly rare (hours between masteries) -- starts small, grows organically.
        componentManager.RegisterMultiPool<ScrollMasteryComponent>(initialCapacity: 8);
    }

    public void RegisterBehavior(BehaviorRegistration<GameModuleContext> registration)
    {
        var context = registration.Context;
        var systemManager = registration.SystemManager;
        var componentManager = registration.ComponentManager;
        // No cooldown system: an action's cooldown is a deadline (ActionInstanceComponent.
        // CooldownReadyAtFrame), read against the current frame rather than walked down.

        systemManager.Register(new PotionCooldownSystem(componentManager.GetPackedPool<PotionCooldownComponent>()));

        var meleeDisabled = componentManager.GetPackedPool<MeleeDisabledComponent>();
        var dodgingEntities = componentManager.GetPackedPool<DodgingComponent>();

        var processingTiers = new ProcessingTierQuery(componentManager.GetDirectPool<ProcessingTierComponent>());

        systemManager.Register(new DodgeExpirySystem(dodgingEntities));

        WireStagger(componentManager, context);

        systemManager.Register(new DelayedActionSystem(
            componentManager.GetPackedPool<PendingDelayedActionComponent>(),
            EntityActions.For(componentManager, context.Actions, context.Definitions),
            context.EffectServices,
            context.Actions,
            context.MapQuery,
            dodgingEntities,
            processingTiers,
            context.SimulationScope,
            context.ProcessingTierEvents));

        systemManager.Register(new ActionActivationSystem(
            componentManager.GetPackedPool<PendingActionActivationComponent>(),
            componentManager.GetPackedPool<ActionLockComponent>(),
            EntityActions.For(componentManager, context.Actions, context.Definitions),
            componentManager.GetPackedPool<PendingDelayedActionComponent>(),
            context.EffectServices,
            context.Actions,
            context.MapQuery,
            meleeDisabled,
            dodgingEntities,
            processingTiers));
    }

    /// <summary>A staggered entity loses its windup, and with it the time the windup already cost: the lock it set is kept.</summary>
    private static void WireStagger(ComponentManager componentManager, GameModuleContext context)
    {
        var pendingDelayedActions = componentManager.GetPackedPool<PendingDelayedActionComponent>();
        var actionLocks = componentManager.GetPackedPool<ActionLockComponent>();

        context.EventBus.Subscribe<EntityStaggeredEvent>(staggered =>
            WindupCancel.TryCancel(pendingDelayedActions, actionLocks, staggered.EntityId, context.SimulationClock.CurrentFrame, releaseLock: false));
    }
}
