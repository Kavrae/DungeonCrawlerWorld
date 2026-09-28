using Engine.ECS.Entities;
using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Inventory.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.Inventory.Systems;
using Game.Modules.Mana.Components;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffectAura.Components;
using Game.Modules.StatusEffects;
using Game.World;
using Game.Blueprints;
using Game.Modules.Core;
using Game.Modules.StatModifiers;
using Game.Modules.Death;
using Game.Modules.Mana;
using Game.Modules.AbilityScores;
using Game.Modules.StatusEffectAura;
using Game.Modules.Race;

namespace Game.Modules.Inventory;

public sealed class InventoryModule : IGameModule
{
    private BlueprintRegistry _creatures = null!;

    public static readonly Guid ModuleId = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000019");

    public Guid Id => ModuleId;

    public IReadOnlyList<Guid> Requires { get; } = [ActionsModule.ModuleId, CoreModule.ModuleId, HealthModule.ModuleId, StatModifiersModule.ModuleId, DeathModule.ModuleId, ManaModule.ModuleId, AbilityScoresModule.ModuleId, StatusEffectAuraModule.ModuleId, ProcessingTierModule.ModuleId, RaceModule.ModuleId];

    private ItemCatalog _itemCatalog = null!;
    private ActionCatalog _actionCatalog = null!;
    private IMapQuery _mapQuery = null!;
    private EventBus _eventBus = null!;
    private MathUtility _mathUtility = null!;
    private StatusEffectAuraApplierRegistry _statusEffectAppliers = null!;
    private IPlayerQuery _playerQuery = null!;
    private EntityKeys _entityKeys = null!;

    public void Configure(GameModuleContext context)
    {
        _creatures = context.Definitions;
        _itemCatalog = context.Items;
        _actionCatalog = context.Actions;
        _mapQuery = context.MapQuery;
        _eventBus = context.EventBus;
        _mathUtility = context.MathUtility;
        _statusEffectAppliers = context.StatusEffectAuraAppliers;
        _playerQuery = context.PlayerQuery;
        _entityKeys = context.EntityKeys;
    }

    public void RegisterComponents(ComponentManager componentManager)
    {
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

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
        var statModifiers = componentManager.GetMultiPool<StatModifierComponent>();
        var deadEntities = componentManager.GetPackedPool<DeadComponent>();
        var mana = componentManager.GetPackedPool<ManaComponent>();
        var hotkeyExpansionUnlocks = componentManager.GetPackedPool<HotkeyExpansionUnlockComponent>();
        var abilityScores = componentManager.GetPackedPool<AbilityScoresComponent>();
        var auraSources = componentManager.GetMultiPool<StatusEffectAuraSourceComponent>();
        var itemHotkeyBindings = componentManager.GetMultiPool<ItemHotkeyBindingComponent>();
        var bodyParts = EntityBodyParts.For(componentManager, _creatures);

        systemManager.Register(new ConsumableActivationSystem(
            componentManager.GetPackedPool<PendingConsumableActivationComponent>(),
            componentManager.GetPackedPool<ActionLockComponent>(),
            componentManager.GetPackedPool<PotionCooldownComponent>(),
            componentManager.GetPackedPool<SimpleHealthComponent>(),
            _itemCatalog,
            _actionCatalog,
            _mapQuery,
            _eventBus,
            _mathUtility,
            componentManager,
            _entityKeys,
            statModifiers,
            deadEntities,
            mana,
            hotkeyExpansionUnlocks,
            abilityScores,
            auraSources,
            itemHotkeyBindings,
            bodyParts,
            new ProcessingTierQuery(componentManager.GetDirectPool<ProcessingTierComponent>()),
            _playerQuery,
            _statusEffectAppliers,
            _creatures));
    }
}
