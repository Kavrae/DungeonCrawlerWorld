using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Inventory.Components;
using Game.Modules.Movement;
using Game.Modules.Movement.Components;
using Game.Modules.NpcBehavior.Components;
using Game.Modules.NpcBehavior.Systems;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.Race.Components;
using Game.World;
using Game.Blueprints;
using Game.Modules.Core;
using Game.Modules.Inventory;
using Game.Modules.Death;
using Game.Modules.Race;

namespace Game.Modules.NpcBehavior;

/// <summary>
/// Owns TestCombatBehaviorSystem and TestDummyAttackSystem (plus TestDummyComponent, the only
/// component either of them introduces) -- no dedicated home for either exists otherwise (RaceModule
/// explicitly owns no systems of its own, and folding this into MovementModule/ActionsModule/
/// InventoryModule would give each an unrelated coupling in the wrong direction). Runs before
/// MovementModule so TestCombatBehaviorSystem.Update runs before MovementSystem.Update every
/// frame -- see TestCombatBehaviorSystem's own doc comment for why that ordering matters.
/// </summary>
public sealed class NpcBehaviorModule : IGameModule
{
    public static readonly Guid ModuleId = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000018");

    public Guid Id => ModuleId;

    public IReadOnlyList<Guid> Requires { get; } = [CoreModule.ModuleId, HealthModule.ModuleId, InventoryModule.ModuleId, ActionsModule.ModuleId, DeathModule.ModuleId, MovementModule.ModuleId, ProcessingTierModule.ModuleId, RaceModule.ModuleId, BlueprintsModule.ModuleId];

    public IReadOnlyList<Guid> RunsBefore { get; } = [MovementModule.ModuleId];

    private IMapQuery _mapQuery = null!;
    private MathUtility _mathUtility = null!;
    private ActionCatalog _actionCatalog = null!;
    private BlueprintRegistry _creatures = null!;
    private ProcessingTierEvents _processingTierEvents = null!;

    public void Configure(GameModuleContext context)
    {
        _mapQuery = context.MapQuery;
        _mathUtility = context.MathUtility;
        _processingTierEvents = context.ProcessingTierEvents;
        _actionCatalog = context.Actions;
        _creatures = context.Definitions;
    }

    public void RegisterComponents(ComponentManager componentManager) =>
        componentManager.RegisterPackedPool<TestDummyComponent>(static (ref existing, incoming) => existing = incoming);

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
        var deadEntities = componentManager.GetPackedPool<DeadComponent>();

        systemManager.Register(new TestCombatBehaviorSystem(
            componentManager.GetPackedPool<MovementComponent>(),
            componentManager.GetDirectPool<TransformComponent>(),
            componentManager.GetPackedPool<ActionLockComponent>(),
            componentManager.GetPackedPool<SimpleHealthComponent>(),
            EntityBodyParts.For(componentManager, _creatures),
            componentManager.GetMultiPool<InventoryItemStackComponent>(),
            EntityActions.For(componentManager, _actionCatalog, _creatures),
            componentManager.GetPackedPool<RaceSlotsComponent>(),
            componentManager.GetPackedPool<PendingActionActivationComponent>(),
            componentManager.GetPackedPool<PendingConsumableActivationComponent>(),
            _mapQuery,
            _mathUtility,
            componentManager.GetDirectPool<ProcessingTierComponent>(),
            _processingTierEvents,
            deadEntities));

        systemManager.Register(new TestDummyAttackSystem(
            componentManager.GetPackedPool<TestDummyComponent>(),
            componentManager.GetDirectPool<TransformComponent>(),
            componentManager.GetPackedPool<ActionLockComponent>(),
            componentManager.GetPackedPool<PendingActionActivationComponent>(),
            _mapQuery));
    }
}
