using System.Diagnostics;
using Engine.Decisions;
using Engine.ECS.Entities;
using Engine.Math;
using Engine.Tags;
using Engine.Tiles;
using Game.Modules.Actions;
using Game.Modules.Core.Components;
using Game.Modules.Health;
using Game.Modules.Inventory;
using Game.Modules.Movement;
using Game.Modules.StatModifiers;

namespace Game.Modules.NpcBehavior;

/// <summary>One NPC's decision: who it is and when, what it perceives (each read once, on first ask), what its behaviors found, and the only way they write.</summary>
/// <remarks>
/// Built twice per decision by NpcDecisionSystem: once to choose, once to act on the choice. While choosing it refuses
/// every write and every roll a behavior makes (in Debug), so an Evaluate or a consideration that writes fails where it
/// does so; NextUnitRandom, the arbiter's draw, is the one roll made while choosing. Every roll reads a sequence derived
/// from the entity's key and the frame -- one for choosing, another for acting -- so no creature's choice depends on
/// another's. What Evaluate finds for Act is kept in Findings, which the system carries from one to the other.
///
/// Hostility is one rule, IsHostile (a different primary race), held here for factions to replace. Every write goes
/// through IntentWriter or NpcLooting; the idle wait is the decision's own (NpcDecisionComponent.WaitUntilFrame).
/// </remarks>
public ref struct NpcDecisionContext : IDecisionContext
{
    private const ulong ActingSalt = 1;

    /// <summary>How far, in tiles from its footprint, a creature takes note of hostiles it can see (HostilesInSight): what Flee runs from.</summary>
    public const byte HostileSightRadius = 3;

    private readonly NpcDecisionServices _services;
    private readonly bool _isActing;
    private byte _hasOtherOccupantNear;
    private byte _hasHostileAdjacent;
    private float _healthFraction;
    private int _hostilesInSight;
    private EntityKey _nearestHostileInSight;
    private int _alliesCounted;
    private byte _alliesRadius;

    /// <param name="canMove">Whether it holds a MovementComponent, as the decision's gate found.</param>
    /// <param name="decisionSeed">The decision's seed (SeedFor), which the choosing and acting sequences are derived from.</param>
    /// <param name="isActing">False to choose, true to act on a choice.</param>
    public NpcDecisionContext(NpcDecisionServices services, int entityId, long now, in TransformComponent transform, bool canMove, ulong decisionSeed, bool isActing, NpcDecisionFindings findings = default)
    {
        _services = services;
        _isActing = isActing;
        _healthFraction = float.NaN;
        _hostilesInSight = -1;
        _alliesCounted = -1;
        EntityId = entityId;
        Now = now;
        Transform = transform;
        CanMove = canMove;
        Findings = findings;
        services.Random.Reseed(isActing ? SeedMix.Mix(decisionSeed, ActingSalt) : decisionSeed);
    }

    /// <summary>The seed of entityId's decision at frame now: the behavior stream's seed mixed with its key and the frame, so it is the same whoever decided before it.</summary>
    public static ulong SeedFor(NpcDecisionServices services, int entityId, long now) =>
        SeedMix.Mix(SeedMix.Mix(services.BehaviorStreamSeed, services.EntityKeys.GetKey(entityId).Value), (ulong)now);

    public int EntityId { get; }

    public long Now { get; }

    public TransformComponent Transform { get; }

    /// <summary>Whether it holds a MovementComponent: a stationary creature never steps.</summary>
    public bool CanMove { get; }

    public NpcDecisionFindings Findings;

    public EntityKey CommitmentTarget { get; set; }

    public readonly NpcDecisionServices Services => _services;

    /// <summary>What a behavior's Act rolls with.</summary>
    public readonly MathUtility Rolls
    {
        get
        {
            EnsureActing(_isActing, "roll");
            return _services.Rolls;
        }
    }

    public readonly float NextUnitRandom() => _services.Random.NextSingle();

    /// <summary>The product of its effective modifier on each drive the behavior serves (NpcDrives.RuntimeTargets), from a base of 1, never below 0.</summary>
    public readonly float RuntimeWeightMultiplier(GameplayTagSet behaviorTags)
    {
        var multiplier = 1f;
        foreach (var (drive, target) in NpcDrives.RuntimeTargets)
        {
            if (behaviorTags.Has(drive))
            {
                multiplier *= MathF.Max(0f, StatModifierMath.GetEffectiveValue(_services.Effects.StatModifiers, EntityId, target, 1f));
            }
        }

        return multiplier;
    }

    /// <summary>Its total health as a share of its maximum with its MaximumHealth modifiers applied (a Boss's doubled health included); 1 for an entity with none.</summary>
    public float HealthFraction()
    {
        if (float.IsNaN(_healthFraction))
        {
            _healthFraction = 1f;
            if (HealthQueries.TryGetTotals(_services.Health, _services.BodyParts, _services.Effects.StatModifiers, EntityId, out var current, out var maximum) && maximum > 0)
            {
                _healthFraction = current / maximum;
            }
        }

        return _healthFraction;
    }

    /// <summary>Whether anything else stands on its footprint or a tile beside it, on its layer: where a melee target or loot can be.</summary>
    /// <remarks>One pass over that rectangle, asked first by every behavior that needs a neighbor, so the common "nobody near" decision reads nothing more.</remarks>
    public bool HasOtherOccupantNear()
    {
        if (_hasOtherOccupantNear == 0)
        {
            _hasOtherOccupantNear = ScanForOtherOccupant() ? (byte)2 : (byte)1;
        }

        return _hasOtherOccupantNear == 2;
    }

    /// <summary>Whether something hostile to it stands on a tile its Adjacent shape reaches.</summary>
    public bool HasHostileAdjacent()
    {
        if (_hasHostileAdjacent == 0)
        {
            _hasHostileAdjacent = HasOtherOccupantNear() && ScanForHostileAdjacent() ? (byte)2 : (byte)1;
        }

        return _hasHostileAdjacent == 2;
    }

    /// <summary>How many hostiles stand within HostileSightRadius tiles of its footprint, on its layer, in its sight.</summary>
    /// <remarks>Sight is the targeting rule (LineOfSight over World.BlocksSight), so a hostile behind a wall isn't counted. Read at most once per decision.</remarks>
    public int HostilesInSight()
    {
        if (_hostilesInSight < 0)
        {
            _hostilesInSight = CountHostilesInSight(out _nearestHostileInSight);
        }

        return _hostilesInSight;
    }

    /// <summary>The nearest hostile HostilesInSight counted; EntityKey.None when it counted none.</summary>
    public EntityKey NearestHostileInSight()
    {
        HostilesInSight();
        return _nearestHostileInSight;
    }

    /// <summary>How many allies -- of its race, alive and simulated, itself excluded -- stand within radius tiles of its footprint, on its layer.</summary>
    /// <remarks>Counted without sight: a pack is heard as much as seen. Kept for the last radius asked, which is the only one any grant asks today.</remarks>
    public int AlliesWithin(byte radius)
    {
        if (_alliesCounted < 0 || _alliesRadius != radius)
        {
            _alliesRadius = radius;
            _alliesCounted = CountAllies(radius);
        }

        return _alliesCounted;
    }

    /// <summary>Whether otherEntityId is hostile to it.</summary>
    public readonly bool IsHostile(int otherEntityId) =>
        otherEntityId != EntityId && _services.TryGetRaceId(EntityId, out var raceId) && _services.IsHostileTo(otherEntityId, raceId);

    /// <summary>Queues its activation of action against selection.</summary>
    public readonly void QueueAction(Guid actionId, TargetSelection selection)
    {
        EnsureActing(_isActing, "queue an action");
        _services.Intents.QueueAction(EntityId, actionId, selection);
    }

    /// <summary>Queues its activation of its stack stackInstanceId against selection.</summary>
    public readonly void QueueItemActivation(uint stackInstanceId, TargetSelection selection)
    {
        EnsureActing(_isActing, "queue an item activation");
        _services.Intents.QueueItemActivation(EntityId, stackInstanceId, selection);
    }

    /// <summary>Has it step to tile next; null stands it still.</summary>
    public readonly void Step(Vector3Int? tile)
    {
        EnsureActing(_isActing, "step");
        _services.Intents.Step(EntityId, tile);
    }

    /// <summary>Has it decide nothing for MovementCandidates.FramesToWaitIfNoOptions frames.</summary>
    public readonly void Idle()
    {
        EnsureActing(_isActing, "idle");
        using var decision = _services.Decisions.TryWrite(EntityId);
        if (decision.Found)
        {
            decision.Value.WaitUntilFrame = Engine.ECS.Systems.FrameDeadline.After(Now, MovementCandidates.FramesToWaitIfNoOptions);
        }
    }

    /// <summary>Loots target, if it is still its to loot (NpcLooting.TryLoot).</summary>
    public readonly bool TryLoot(EntityKey target)
    {
        EnsureActing(_isActing, "loot");
        return _services.Looting.TryLoot(EntityId, target, Now);
    }

    [Conditional("DEBUG")]
    private static void EnsureActing(bool isActing, string attempt)
    {
        if (!isActing)
        {
            throw new InvalidOperationException($"A behavior tried to {attempt} while choosing: Evaluate and considerations only read, and only Act writes or rolls.");
        }
    }

    private readonly bool ScanForOtherOccupant()
    {
        var position = Transform.Position;
        var size = Transform.Size;
        for (var y = position.Y - 1; y <= position.Y + size.Y; y++)
        {
            for (var x = position.X - 1; x <= position.X + size.X; x++)
            {
                foreach (var occupantEntityId in _services.Map.GetOccupantEntityIdSpanAt(new Vector3Int(x, y, position.Z)))
                {
                    if (occupantEntityId != EntityId)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <remarks>Every occupant of each tile, Blocking or not: a non-Blocking hostile beside it can still be hit.</remarks>
    private readonly bool ScanForHostileAdjacent()
    {
        if (!_services.TryGetRaceId(EntityId, out var raceId))
        {
            return false;
        }

        var tiles = _services.AdjacentTiles;
        TargetShapeResolver.Resolve(TargetShape.Adjacent, TargetAnchor.Caster, Transform.Position, Transform.Size, Transform.Position, default, _services.Map.Bounds, tiles);
        foreach (var tile in tiles)
        {
            foreach (var occupantEntityId in _services.Map.GetOccupantEntityIdSpanAt(tile))
            {
                if (occupantEntityId != EntityId && _services.IsHostileTo(occupantEntityId, raceId))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <remarks>The nearest is by Chebyshev distance from its footprint; the first found wins a tie, so nothing is rolled.</remarks>
    private readonly int CountHostilesInSight(out EntityKey nearest)
    {
        nearest = EntityKey.None;
        if (!_services.TryGetRaceId(EntityId, out var raceId))
        {
            return 0;
        }

        var position = Transform.Position;
        var size = Transform.Size;
        var counted = _services.CountedEntityIds;
        counted.Clear();
        var nearestDistance = int.MaxValue;
        for (var y = position.Y - HostileSightRadius; y < position.Y + size.Y + HostileSightRadius; y++)
        {
            for (var x = position.X - HostileSightRadius; x < position.X + size.X + HostileSightRadius; x++)
            {
                var tile = new Vector3Int(x, y, position.Z);
                foreach (var occupantEntityId in _services.Map.GetOccupantEntityIdSpanAt(tile))
                {
                    if (occupantEntityId == EntityId || counted.Contains(occupantEntityId) || !_services.IsHostileTo(occupantEntityId, raceId) ||
                        !LineOfSight.IsVisibleFromFootprint(position, size, tile, _services.SightBlockers))
                    {
                        continue;
                    }

                    counted.Add(occupantEntityId);
                    var distance = DistanceFromFootprint(tile);
                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        nearest = _services.EntityKeys.GetKey(occupantEntityId);
                    }
                }
            }
        }

        return counted.Count;
    }

    private readonly int CountAllies(byte radius)
    {
        if (!_services.TryGetRaceId(EntityId, out var raceId))
        {
            return 0;
        }

        var position = Transform.Position;
        var size = Transform.Size;
        var counted = _services.CountedEntityIds;
        counted.Clear();
        for (var y = position.Y - radius; y < position.Y + size.Y + radius; y++)
        {
            for (var x = position.X - radius; x < position.X + size.X + radius; x++)
            {
                foreach (var occupantEntityId in _services.Map.GetOccupantEntityIdSpanAt(new Vector3Int(x, y, position.Z)))
                {
                    if (occupantEntityId != EntityId && _services.IsAllyOf(occupantEntityId, raceId))
                    {
                        counted.Add(occupantEntityId);
                    }
                }
            }
        }

        return counted.Count;
    }

    /// <summary>How many tiles tile is from its footprint, Chebyshev: 0 on it, 1 beside it.</summary>
    public readonly int DistanceFromFootprint(Vector3Int tile)
    {
        var position = Transform.Position;
        var size = Transform.Size;
        var distanceX = System.Math.Max(0, System.Math.Max(position.X - tile.X, tile.X - (position.X + size.X - 1)));
        var distanceY = System.Math.Max(0, System.Math.Max(position.Y - tile.Y, tile.Y - (position.Y + size.Y - 1)));
        return System.Math.Max(distanceX, distanceY);
    }
}
