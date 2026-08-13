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
        componentManager.RegisterDirectPool<InventoryDisabledComponent>(static (ref existing, incoming) => existing.IsDisabled = incoming.IsDisabled);
        componentManager.RegisterPackedPool<PendingConsumableActivationComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterMultiPool<ItemHotkeyBindingComponent>();
        componentManager.RegisterPackedPool<InventoryComponent>(static (ref existing, incoming) => existing = incoming);
    }

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
        if (!componentManager.IsRegistered<HealthComponent>())
        {
            return;
        }

        var statModifiers = componentManager.IsRegistered<StatModifierComponent>()
            ? componentManager.GetMultiPool<StatModifierComponent>()
            : null;
        var deadEntities = componentManager.IsRegistered<DeadComponent>()
            ? componentManager.GetPackedPool<DeadComponent>()
            : null;
        var mana = componentManager.IsRegistered<ManaComponent>()
            ? componentManager.GetPackedPool<ManaComponent>()
            : null;
        var hotkeyExpansionUnlocks = componentManager.IsRegistered<HotkeyExpansionUnlockComponent>()
            ? componentManager.GetPackedPool<HotkeyExpansionUnlockComponent>()
            : null;
        var abilityScores = componentManager.IsRegistered<AbilityScoreComponent>()
            ? componentManager.GetMultiPool<AbilityScoreComponent>()
            : null;
        var auraSources = componentManager.IsRegistered<StatusEffectAuraSourceComponent>()
            ? componentManager.GetMultiPool<StatusEffectAuraSourceComponent>()
            : null;

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
            auraSources));
    }
}
