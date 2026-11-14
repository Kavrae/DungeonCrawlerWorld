using Engine.Decisions;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.Modules.Core.Components;
using Game.Modules.NpcBehavior.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;

namespace Game.Modules.NpcBehavior.Systems;

/// <summary>Decides what every NPC does: each entity holding an NpcDecisionComponent, when it is free to, through the dual-utility arbiter over its behavior set.</summary>
/// <remarks>
/// Gates first, cheapest first, before the arbiter is ever asked: the player (never decided for, whatever its race),
/// dead, idling (NpcDecisionComponent.WaitUntilFrame), action-locked, or still mid-step. Every act locks the entity again
/// -- an attack for its action's lock, a step for the movement lock, an idle for its wait -- so a free NPC decides about
/// once per act, not on every visit.
///
/// Each bucket is two passes: every due entity chooses (BehaviorArbiter.Choose, which writes nothing), then each choice
/// acts, in entity order. So no creature's choice depends on another's act, and running the first pass on workers later
/// changes no seeded world. What a choice found for its act travels with it as a plain value (NpcDecisionFindings). A
/// commitment the act starts is kept in the entity's BehaviorCommitment, found or added and never removed.
///
/// Tiered like MovementSystem, with the same StripeCount and divisors, so a mover lands in the same bucket of the same
/// tier in both and is decided and moved on the same frame (NpcBehaviorModule runs before Movement). Its population is the
/// decision pool, not the movement pool, so a stationary creature decides too, and MovementSystem never visits it.
/// </remarks>
public sealed class NpcDecisionSystem : ITieredSystem
{
    private const byte StripeCountValue = 15;

    private readonly NpcDecisionServices _services;
    private readonly NpcDecisionState _state;
    private readonly PackedComponentPool<BehaviorCommitment> _commitments;
    private readonly BehaviorArbiter<NpcDecisionContext> _arbiter;
    private readonly TieredEntityStripeSet _tieredStripeSet;
    private readonly List<NpcDecisionRecord> _records = new(256);

    public NpcDecisionSystem(
        NpcDecisionServices services,
        NpcDecisionState state,
        PackedComponentPool<BehaviorCommitment> commitments,
        BehaviorArbiterOptions arbiterOptions,
        DirectComponentPool<ProcessingTierComponent> processingTiers,
        ProcessingTierEvents processingTierEvents)
    {
        _services = services;
        _state = state;
        _commitments = commitments;
        _arbiter = new BehaviorArbiter<NpcDecisionContext>(arbiterOptions);
        _tieredStripeSet = ProcessingTierWiring.CreateAndWire(StripeCountValue, services.Decisions, processingTiers, processingTierEvents);
    }

    public byte StripeCount => StripeCountValue;

    public TieredEntityStripeSet Tiers => _tieredStripeSet;

    public void Update(EngineTime time, byte stripeIndex) => TieredSystemRunner.Run(this, time);

    public void UpdateBucket(EngineTime time, ReadOnlySpan<int> entityIds, ushort framesPerVisit)
    {
        var now = time.FrameCount;
        _records.Clear();
        foreach (var entityId in entityIds)
        {
            if (TryChoose(entityId, now, out var record))
            {
                _records.Add(record);
            }
        }

        foreach (var record in _records)
        {
            Act(record, now);
        }
    }

    private bool TryChoose(int entityId, long now, out NpcDecisionRecord record)
    {
        record = default;
        var services = _services;
        if (entityId == services.Player.PlayerEntityId || services.Dead.Has(entityId))
        {
            return false;
        }

        var decision = services.Decisions.GetReadonly(entityId);
        if (!FrameDeadline.IsReached(decision.WaitUntilFrame, now) ||
            ActionLockGate.IsBlocked(services.ActionLocks, entityId, now) ||
            !services.Transforms.TryGetReadonly(entityId, out var transform))
        {
            return false;
        }

        var canMove = services.Movements.TryGetReadonly(entityId, out var movement);
        if (canMove && movement.NextMapPosition is { } step && step != transform.Position)
        {
            return false;
        }

        var set = _state.Sets.Get(decision.BehaviorSetId);
        var commitment = CommitmentOf(set, entityId);
        var trace = entityId == _state.InspectedEntityId ? _state.InspectedTrace : null;
        var seed = NpcDecisionContext.SeedFor(services, entityId, now);
        var context = new NpcDecisionContext(services, entityId, now, in transform, canMove, seed, isActing: false);
        var choice = _arbiter.Choose(ref context, set, commitment, trace);
        _state.Decisions++;
        if (trace is not null)
        {
            _state.InspectedDecisionFrame = now;
            _state.InspectedChoice = choice.IsNone ? null : set[choice.EntryIndex].Behavior.Name;
        }

        record = new NpcDecisionRecord(entityId, decision.BehaviorSetId, canMove, seed, choice, context.Findings);
        return true;
    }

    private void Act(in NpcDecisionRecord record, long now)
    {
        var entityId = record.EntityId;
        if (record.Findings.RetryCorpsesLater)
        {
            _services.Looting.RetryLater(entityId, now);
        }

        if (record.Choice.IsNone || !_services.Transforms.TryGetReadonly(entityId, out var transform))
        {
            return;
        }

        var set = _state.Sets.Get(record.SetId);
        var commitment = CommitmentOf(set, entityId);
        var context = new NpcDecisionContext(_services, entityId, now, in transform, record.CanMove, record.Seed, isActing: true, record.Findings);
        var decision = _arbiter.Act(ref context, set, record.Choice, commitment);
        if (decision.StartedCommitment)
        {
            Store(entityId, decision.Commitment);
            _state.CommitmentsStarted++;
        }
    }

    /// <remarks>A set none of whose grants commits never started a commitment, so its stored one isn't read.</remarks>
    private BehaviorCommitment CommitmentOf(BehaviorSet<NpcDecisionContext> set, int entityId) =>
        set.CanCommit && _commitments.TryGetReadonly(entityId, out var held) ? held : default;

    private void Store(int entityId, BehaviorCommitment started)
    {
        using (var commitment = _commitments.TryWrite(entityId))
        {
            if (commitment.Found)
            {
                commitment.Value.BehaviorSessionId = started.BehaviorSessionId;
                commitment.Value.CommittedUntilFrame = started.CommittedUntilFrame;
                commitment.Value.Target = started.Target;
                return;
            }
        }

        _commitments.Add(entityId, started);
    }

    /// <summary>One due entity's choice, kept from the choose pass to the act pass.</summary>
    private readonly record struct NpcDecisionRecord(int EntityId, ushort SetId, bool CanMove, ulong Seed, BehaviorChoice Choice, NpcDecisionFindings Findings);
}
