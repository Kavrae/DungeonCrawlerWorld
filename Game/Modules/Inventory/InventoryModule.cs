using Engine.Modules;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.Core;
using Game.Modules.Core.Components;
using Game.Modules.Death;
using Game.Modules.Death.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Inventory.Components;
using Game.Modules.Inventory.Systems;
using Game.Modules.Mana;
using Game.Modules.Mana.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.Race;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffectAura;
using Game.Modules.StatusEffectAura.Components;

namespace Game.Modules.Inventory;

public sealed class InventoryModule : IGameModule
{
    public static readonly Guid ModuleId = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000019");

    public Guid Id => ModuleId;

    public IReadOnlyList<Guid> Requires { get; } = [ActionsModule.ModuleId, CoreModule.ModuleId, HealthModule.ModuleId, StatModifiersModule.ModuleId, DeathModule.ModuleId, ManaModule.ModuleId, AbilityScoresModule.ModuleId, StatusEffectAuraModule.ModuleId, ProcessingTierModule.ModuleId, RaceModule.ModuleId];

    public void RegisterComponents(ComponentRegistration registration)
    {
        var componentManager = registration.ComponentManager;

        componentManager.RegisterMultiPool<InventoryItemStackComponent>();

        // Rare -- generally only once at a time on the player, but could be more via a lock-down status effect.
        // Registered as Packed rather than Direct: Direct pool is reserved for genuinely near-universal components
        // (Transform/Sprite/etc.).
        componentManager.RegisterPackedPool<InventoryDisabledComponent>(
            static (ref existing, incoming) => existing.IsDisabled = incoming.IsDisabled, initialCapacity: 16);

        componentManager.RegisterPackedPool<PendingConsumableActivationComponent>(static (ref existing, incoming) => existing = incoming);

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

        var statModifiers = componentManager.GetMultiPool<StatModifierComponent>();
        var deadEntities = componentManager.GetPackedPool<DeadComponent>();
        var mana = componentManager.GetPackedPool<ManaComponent>();
        var hotkeyExpansionUnlocks = componentManager.GetPackedPool<HotkeyExpansionUnlockComponent>();
        var abilityScores = componentManager.GetPackedPool<AbilityScoresComponent>();
        var auraSources = componentManager.GetMultiPool<StatusEffectAuraSourceComponent>();
        var itemHotkeyBindings = componentManager.GetMultiPool<ItemHotkeyBindingComponent>();
        var bodyParts = EntityBodyParts.For(componentManager, context.Definitions);

        systemManager.Register(new ConsumableActivationSystem(
            componentManager.GetPackedPool<PendingConsumableActivationComponent>(),
            componentManager.GetPackedPool<ActionLockComponent>(),
            componentManager.GetPackedPool<PotionCooldownComponent>(),
            componentManager.GetPackedPool<SimpleHealthComponent>(),
            context.Items,
            context.Actions,
            context.MapQuery,
            context.EventBus,
            context.MathUtility,
            componentManager,
            context.EntityKeys,
            statModifiers,
            deadEntities,
            mana,
            hotkeyExpansionUnlocks,
            abilityScores,
            auraSources,
            itemHotkeyBindings,
            bodyParts,
            new ProcessingTierQuery(componentManager.GetDirectPool<ProcessingTierComponent>()),
            context.PlayerQuery,
            context.StatusEffectAuraAppliers,
            context.Definitions,
            context.FloatingTextFeed));
    }
}
