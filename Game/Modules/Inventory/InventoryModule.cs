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
using Game.Modules.Health.Components;
using Game.Modules.Inventory.Components;
using Game.Modules.Inventory.Systems;
using Game.Modules.Mana.Components;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffectAura.Components;
using Game.Modules.StatusEffects;
using Game.World;

namespace Game.Modules.Inventory;

public sealed class InventoryModule : IGameModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000010");

    public IReadOnlyList<Type> Dependencies { get; } = [typeof(ActionsModule)];

    private ItemCatalog _itemCatalog = null!;
    private ActionCatalog _actionCatalog = null!;
    private IMapQuery _mapQuery = null!;
    private EventBus _eventBus = null!;
    private MathUtility _mathUtility = null!;
    private StatusEffectAuraApplierRegistry _statusEffectAppliers = null!;
    private IPlayerQuery? _playerQuery;

    public void Configure(GameModuleContext context)
    {
        _itemCatalog = context.Items;
        _actionCatalog = context.Actions;
        _mapQuery = context.MapQuery;
        _eventBus = context.EventBus;
        _mathUtility = context.MathUtility;
        _statusEffectAppliers = context.StatusEffectAuraAppliers;
        _playerQuery = context.PlayerQuery;
    }

    public void RegisterComponents(ComponentManager componentManager)
    {
        componentManager.RegisterMultiPool<InventoryItemStackComponent>();

        // Rare -- generally only once at a time on the player, but could be more via a lock-down status effect.
        // Registered as Packed rather than Direct: Direct pool is reserved for genuinely near-universal components
        // (Transform/Sprite/etc.), and only Packed offers a dense-side capacity reduction alongside the entity-index one.
        componentManager.RegisterPackedPool<InventoryDisabledComponent>(
            static (ref existing, incoming) => existing.IsDisabled = incoming.IsDisabled, maximumEntityCount: 16, initialCapacity: 16);

        componentManager.RegisterPackedPool<PendingConsumableActivationComponent>(static (ref existing, incoming) => existing = incoming);

        // Player-only, 24 hotkey slots total -- small entity-index seed, dense capacity matches the slot count.
        componentManager.RegisterMultiPool<ItemHotkeyBindingComponent>(maximumEntityCount: 2, initialCapacity: 24);

        componentManager.RegisterPackedPool<InventoryComponent>(static (ref existing, incoming) => existing = incoming);
    }

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
        if (!componentManager.IsRegistered<HealthComponent>())
        {
            return;
        }

        var statModifiers = componentManager.GetOptionalMultiPool<StatModifierComponent>();
        var deadEntities = componentManager.GetOptionalPackedPool<DeadComponent>();
        var mana = componentManager.GetOptionalPackedPool<ManaComponent>();
        var hotkeyExpansionUnlocks = componentManager.GetOptionalPackedPool<HotkeyExpansionUnlockComponent>();
        var abilityScores = componentManager.GetOptionalMultiPool<AbilityScoreComponent>();
        var auraSources = componentManager.GetOptionalMultiPool<StatusEffectAuraSourceComponent>();
        var itemHotkeyBindings = componentManager.GetOptionalMultiPool<ItemHotkeyBindingComponent>();

        systemManager.Register(new ConsumableActivationSystem(
            componentManager.GetPackedPool<PendingConsumableActivationComponent>(),
            componentManager.GetPackedPool<ActionLockComponent>(),
            componentManager.GetPackedPool<PotionCooldownComponent>(),
            componentManager.GetPackedPool<HealthComponent>(),
            _itemCatalog,
            _actionCatalog,
            _mapQuery,
            _eventBus,
            _mathUtility,
            componentManager,
            statModifiers,
            deadEntities,
            mana,
            hotkeyExpansionUnlocks,
            abilityScores,
            _statusEffectAppliers,
            _playerQuery,
            auraSources,
            itemHotkeyBindings));
    }
}
