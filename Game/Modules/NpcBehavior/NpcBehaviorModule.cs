using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Modules.Actions.Components;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.Modules.Inventory.Components;
using Game.Modules.Movement.Components;
using Game.Modules.NpcBehavior.Systems;
using Game.Modules.Race.Components;
using Game.World;

namespace Game.Modules.NpcBehavior;

/// <summary>
/// Owns TestCombatBehaviorSystem -- no dedicated home for it exists otherwise (RaceModule
/// explicitly owns no systems of its own, and folding this into MovementModule/ActionsModule/
/// InventoryModule would give each an unrelated coupling in the wrong direction). No components
/// of its own -- reads/writes components every other built-in module already registers.
/// Registered in GameBootstrapper.builtInModules *before* MovementModule specifically so
/// TestCombatBehaviorSystem.Update runs before MovementSystem.Update every frame -- see
/// TestCombatBehaviorSystem's own doc comment for why that ordering matters.
/// </summary>
public sealed class NpcBehaviorModule : IGameModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000018");

    public IReadOnlyList<Type> Dependencies { get; } = [];

    private IMapQuery _mapQuery = null!;
    private MathUtility _mathUtility = null!;
    private IPlayerQuery? _playerQuery;

    public void Configure(GameModuleContext context)
    {
        _mapQuery = context.MapQuery;
        _mathUtility = context.MathUtility;
        _playerQuery = context.PlayerQuery;
    }

    public void RegisterComponents(ComponentManager componentManager)
    {
        // No components of its own -- see class doc comment.
    }

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
        if (!componentManager.IsRegistered<SimpleHealthComponent>() ||
            !componentManager.IsRegistered<InventoryItemStackComponent>() ||
            !componentManager.IsRegistered<ActionInstanceComponent>() ||
            !componentManager.IsRegistered<PendingActionActivationComponent>() ||
            !componentManager.IsRegistered<PendingConsumableActivationComponent>())
        {
            return;
        }

        var deadEntities = componentManager.IsRegistered<DeadComponent>()
            ? componentManager.GetPackedPool<DeadComponent>()
            : null;

        systemManager.Register(new TestCombatBehaviorSystem(
            componentManager.GetPackedPool<MovementComponent>(),
            componentManager.GetDirectPool<TransformComponent>(),
            componentManager.GetPackedPool<ActionLockComponent>(),
            componentManager.GetPackedPool<SimpleHealthComponent>(),
            componentManager.GetMultiPool<InventoryItemStackComponent>(),
            componentManager.GetMultiPool<ActionInstanceComponent>(),
            componentManager.GetMultiPool<RaceComponent>(),
            componentManager.GetPackedPool<PendingActionActivationComponent>(),
            componentManager.GetPackedPool<PendingConsumableActivationComponent>(),
            _mapQuery,
            _mathUtility,
            _playerQuery,
            deadEntities));
    }
}
