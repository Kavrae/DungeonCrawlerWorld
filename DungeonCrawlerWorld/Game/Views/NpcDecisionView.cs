using Engine.Decisions;
using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Movement.Components;
using Game.Modules.NpcBehavior;
using Game.Modules.NpcBehavior.Components;
using Game.Spawning;
using Game.World;

namespace Game.Views;

/// <summary>Why an NPC isn't deciding right now, or that it is.</summary>
public enum NpcDecisionGateKind : byte
{
    /// <summary>It decides on its next visit.</summary>
    Free,

    /// <summary>It is the player: nothing decides for it.</summary>
    Player,

    Dead,

    /// <summary>It chose to idle, until UntilFrame.</summary>
    Idling,

    /// <summary>Its action lock holds it, until UntilFrame.</summary>
    Locked,

    /// <summary>It is still taking a step.</summary>
    Stepping,
}

/// <summary>What gates an NPC's next decision, and until when.</summary>
public readonly record struct NpcDecisionGate(NpcDecisionGateKind Kind, uint UntilFrame);

/// <summary>What an inspected NPC decides with and what it last decided, for the Admin Mode inspection readout.</summary>
/// <remarks>
/// Inspect names the one entity NpcDecisionSystem traces; the trace and the last choice describe that entity only, and
/// only from its first decision after it was named. Reads, never writes, the store.
/// </remarks>
public sealed class NpcDecisionView(ComponentManager componentManager, NpcDecisionState state, IPlayerQuery playerQuery, SimulationClock clock)
{
    private readonly PackedComponentPool<NpcDecisionComponent> _decisions = componentManager.GetPackedPool<NpcDecisionComponent>();
    private readonly PackedComponentPool<BehaviorCommitment> _commitments = componentManager.GetPackedPool<BehaviorCommitment>();
    private readonly PackedComponentPool<DeadComponent> _dead = componentManager.GetPackedPool<DeadComponent>();
    private readonly PackedComponentPool<ActionLockComponent> _actionLocks = componentManager.GetPackedPool<ActionLockComponent>();
    private readonly PackedComponentPool<MovementComponent> _movements = componentManager.GetPackedPool<MovementComponent>();
    private readonly DirectComponentPool<TransformComponent> _transforms = componentManager.GetDirectPool<TransformComponent>();
    private readonly DirectComponentPool<SpawnRecordComponent> _spawnRecords = componentManager.GetDirectPool<SpawnRecordComponent>();

    public long Now => clock.CurrentFrame;

    /// <summary>Has the decision system trace entityId from its next decision on.</summary>
    public void Inspect(int entityId) => state.Inspect(entityId);

    /// <summary>Whether entityId decides at all: it holds an NpcDecisionComponent.</summary>
    public bool Decides(int entityId) => _decisions.Has(entityId);

    /// <summary>The behavior set entityId decides with; for an unbuilt skeleton, the one its spawn blueprint resolves to.</summary>
    public BehaviorSet<NpcDecisionContext> SetOf(int entityId) =>
        _decisions.TryGetReadonly(entityId, out var decision)
            ? state.Sets.Get(decision.BehaviorSetId)
            : _spawnRecords.TryGetReadonly(entityId, out var record) ? state.Sets.Get(record.BlueprintId) : BehaviorSet<NpcDecisionContext>.Empty;

    /// <summary>What keeps entityId from deciding now, checked in the order the decision system checks.</summary>
    public NpcDecisionGate GateOf(int entityId)
    {
        var now = clock.CurrentFrame;
        if (entityId == playerQuery.PlayerEntityId)
        {
            return new NpcDecisionGate(NpcDecisionGateKind.Player, 0);
        }

        if (_dead.Has(entityId))
        {
            return new NpcDecisionGate(NpcDecisionGateKind.Dead, 0);
        }

        if (_decisions.TryGetReadonly(entityId, out var decision) && !FrameDeadline.IsReached(decision.WaitUntilFrame, now))
        {
            return new NpcDecisionGate(NpcDecisionGateKind.Idling, decision.WaitUntilFrame);
        }

        if (_actionLocks.TryGetReadonly(entityId, out var actionLock) && !FrameDeadline.IsReached(actionLock.UnlockedAtFrame, now))
        {
            return new NpcDecisionGate(NpcDecisionGateKind.Locked, actionLock.UnlockedAtFrame);
        }

        return _movements.TryGetReadonly(entityId, out var movement) && movement.NextMapPosition is { } step &&
               _transforms.TryGetReadonly(entityId, out var transform) && step != transform.Position
            ? new NpcDecisionGate(NpcDecisionGateKind.Stepping, 0)
            : new NpcDecisionGate(NpcDecisionGateKind.Free, 0);
    }

    /// <summary>entityId's live commitment, when it has one.</summary>
    public bool TryGetCommitment(int entityId, out BehaviorCommitment commitment, out string behaviorName)
    {
        behaviorName = string.Empty;
        if (!_commitments.TryGetReadonly(entityId, out commitment) || !commitment.IsLiveAt(clock.CurrentFrame))
        {
            return false;
        }

        behaviorName = state.Behaviors.BySessionId(commitment.BehaviorSessionId)?.Name ?? string.Empty;
        return true;
    }

    /// <summary>The frame of entityId's last decision since it was inspected; -1 when it is not the inspected entity or hasn't decided since.</summary>
    public long LastDecisionFrame(int entityId) => entityId == state.InspectedEntityId ? state.InspectedDecisionFrame : -1;

    /// <summary>The behavior entityId's last decision chose; null when nothing applied, or LastDecisionFrame is -1.</summary>
    public string? LastChoice(int entityId) => LastDecisionFrame(entityId) >= 0 ? state.InspectedChoice : null;

    /// <summary>The inspected entity's last decision, layer by layer; read it only while LastDecisionFrame is its.</summary>
    public DecisionTrace LastTrace => state.InspectedTrace;
}
