using Engine.ECS.Entities;
using Engine.ECS.Systems;
using Game.World;

namespace Game.Spawning;

/// <summary>Records each spawn as a move from a cell to the same cell, this frame if its moves are still unread, otherwise first thing next frame.</summary>
/// <remarks>
/// A spawn counts as a move (see EntityMovedEvent) so aura and contact-damage exposures are granted
/// the moment something appears. A spawn can happen at any point in the frame -- population between
/// frames, a system mid-frame, a skeleton built in the tier transition drain, an admin command from
/// Presentation -- and once this frame's moves have been read, recording one would lose it (see
/// FrameEventBuffer.Record). Those wait here and are recorded by Update, which is registered first in
/// the frame, ahead of every reader. Each carries its EntityKey, so a spawn whose entity was destroyed
/// in between -- its id possibly already reused -- is skipped rather than searched for on every
/// destruction.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class SpawnMoves(FrameEventBuffer<EntityMovedEvent> movedEntities, EntityKeys entityKeys) : ISystem
{
    private readonly FrameEventBuffer<EntityMovedEvent> _movedEntities = movedEntities ?? throw new ArgumentNullException(nameof(movedEntities));
    private readonly EntityKeys _entityKeys = entityKeys ?? throw new ArgumentNullException(nameof(entityKeys));
    private readonly List<(EntityKey Key, EntityMovedEvent Spawn)> _pending = [];

    public byte StripeCount => 1;

    public void Record(EntityMovedEvent spawn)
    {
        if (!_movedEntities.TryRecord(spawn))
        {
            _pending.Add((_entityKeys.GetKey(spawn.EntityId), spawn));
        }
    }

    public void Update(EngineTime time, byte stripeIndex)
    {
        foreach (var (key, spawn) in _pending)
        {
            if (_entityKeys.TryGetEntityId(key, out var entityId) && entityId == spawn.EntityId)
            {
                _movedEntities.Record(spawn);
            }
        }

        _pending.Clear();
    }
}
