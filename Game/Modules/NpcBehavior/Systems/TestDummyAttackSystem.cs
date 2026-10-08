using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Definitions.DirectActions;
using Game.Modules.Core.Components;
using Game.Modules.NpcBehavior.Components;
using Game.World;

namespace Game.Modules.NpcBehavior.Systems;

/// <summary>
/// Drives TestDummyBlueprint's own attacker: whenever a TestDummyComponent entity's shared
/// ActionLockComponent clears, it unconditionally re-fires Power Attack against its own fixed
/// Adjacent ring -- no engage/chase/health-check decision-making at all, unlike
/// TestCombatBehaviorSystem, since a stationary training dummy has no use for any of that. An
/// empty ring (nothing standing adjacent) is a harmless no-op swing -- PowerAttackAction's shape is
/// a fixed ring, not an aimed single target, so there's no real "random empty tile" choice to make
/// (see TODO.md's Combat Overhaul: Dodge).
/// </summary>
public sealed class TestDummyAttackSystem : ISystem
{
    private const byte StripeCountValue = 1;

    public byte StripeCount => StripeCountValue;

    private readonly PackedComponentPool<TestDummyComponent> _testDummies;
    private readonly DirectComponentPool<TransformComponent> _transformPool;
    private readonly PackedComponentPool<ActionLockComponent> _actionLocks;
    private readonly PackedComponentPool<PendingActionActivationComponent> _pendingActivations;
    private readonly EntityStripeSet _stripeSet;

    /// <summary>Power Attack's Adjacent ring, which needs no aimed tile.</summary>
    private static readonly TargetingSpec PowerAttackTargeting = PowerAttackAction.Build().Activator.Targeting;

    public TestDummyAttackSystem(
        PackedComponentPool<TestDummyComponent> testDummies,
        DirectComponentPool<TransformComponent> transformPool,
        PackedComponentPool<ActionLockComponent> actionLocks,
        PackedComponentPool<PendingActionActivationComponent> pendingActivations)
    {
        _testDummies = testDummies;
        _transformPool = transformPool;
        _actionLocks = actionLocks;
        _pendingActivations = pendingActivations;

        _stripeSet = EntityStripeSet.CreateAndWire(StripeCount, testDummies);
    }

    public void Update(EngineTime time, byte stripeIndex)
    {
        foreach (var entityId in _stripeSet.GetBucket(stripeIndex))
        {
            if (ActionLockGate.IsBlocked(_actionLocks, entityId, time.FrameCount) || !_transformPool.TryGetReadonly(entityId, out var transform))
            {
                continue;
            }

            _pendingActivations.Merge(entityId, new PendingActionActivationComponent(PowerAttackAction.Id, TargetSelection.Ground(transform.Position, PowerAttackTargeting)));
        }
    }
}
