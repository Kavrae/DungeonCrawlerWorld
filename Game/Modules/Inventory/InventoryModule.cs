using Engine.Modules;
using Game.Modules.AbilityScores;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.BodyPartEffects;
using Game.Modules.BodyPartEffects.Components;
using Game.Modules.Core;
using Game.Modules.Core.Components;
using Game.Modules.Death;
using Game.Modules.Health;
using Game.Modules.Inventory.Components;
using Game.Modules.Inventory.Systems;
using Game.Modules.Mana;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.Race;
using Game.Modules.StatModifiers;
using Game.Modules.Auras;

namespace Game.Modules.Inventory;

public sealed class InventoryModule : IGameModule
{
    public static readonly Guid ModuleId = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000019");

    public Guid Id => ModuleId;

    public IReadOnlyList<Guid> Requires { get; } = [ActionsModule.ModuleId, CoreModule.ModuleId, HealthModule.ModuleId, StatModifiersModule.ModuleId, DeathModule.ModuleId, ManaModule.ModuleId, AbilityScoresModule.ModuleId, BodyPartEffectsModule.ModuleId, AurasModule.ModuleId, ProcessingTierModule.ModuleId, RaceModule.ModuleId];

    public void RegisterComponents(ComponentRegistration registration)
    {
        var componentManager = registration.ComponentManager;

        componentManager.RegisterMultiPool<InventoryItemStackComponent>();

        // Rare -- generally only once at a time on the player, but could be more via a lock-down status effect.
        // Registered as Packed rather than Direct: Direct pool is reserved for genuinely near-universal components
        // (Transform/Sprite/etc.).
        componentManager.RegisterPackedPool<InventoryDisabledComponent>(
            static (ref existing, incoming) => existing.IsDisabled = incoming.IsDisabled, initialCapacity: 16);

        componentManager.RegisterPackedPool<PendingItemActivationComponent>(static (ref existing, incoming) => existing = incoming);

        // Player-only in practice (see MaxStackSizeComponent's own doc comment) -- Packed for the same reason InventoryDisabledComponent above is.
        componentManager.RegisterPackedPool<MaxStackSizeComponent>(static (ref existing, incoming) => existing = incoming, initialCapacity: 2);

        // Player-only, 24 hotkey slots total -- dense capacity matches the slot count.
        componentManager.RegisterMultiPool<ItemHotkeyBindingComponent>(initialCapacity: 24);

        componentManager.RegisterPackedPool<InventoryComponent>(static (ref existing, incoming) => existing = incoming);
    }

    public void RegisterBehavior(BehaviorRegistration<GameModuleContext> registration)
    {
        var context = registration.Context;
        var systemManager = registration.SystemManager;
        var componentManager = registration.ComponentManager;

        systemManager.Register(new ItemActivationSystem(
            componentManager.GetPackedPool<PendingItemActivationComponent>(),
            componentManager.GetPackedPool<ActionLockComponent>(),
            componentManager.GetPackedPool<PotionCooldownComponent>(),
            context.EffectServices,
            context.Items,
            context.Actions,
            context.MapQuery,
            componentManager.GetPackedPool<MeleeDisabledComponent>(),
            componentManager.GetMultiPool<ItemHotkeyBindingComponent>(),
            new ProcessingTierQuery(componentManager.GetDirectPool<ProcessingTierComponent>()),
            context.Toggles,
            componentManager.GetPackedPool<PendingWindupComponent>(),
            context.TargetResolution));

        var toggleItemHolderSync = new ToggleItemHolderSync(
            componentManager.GetMultiPool<InventoryItemStackComponent>(),
            componentManager.GetMultiPool<ActiveToggleComponent>(),
            context.Toggles,
            context.Items,
            context.EntityManager,
            context.SimulationClock,
            componentManager);
        context.Toggles.RegisterOwner(ActivatableKind.Item, toggleItemHolderSync);
        context.WindupResolvers.RegisterItemResolver(new ToggleItemWindupResolver(componentManager, context.Items));
    }
}
