using Engine.ECS.Components.Stores;
using Engine.Math;
using Game.Modules.Core.Components;

namespace Game.World;

/// <summary>The in-memory game world: the map and bookkeeping for entities placed on it.</summary>
public sealed class World(Map map) : IMapQuery, IPlayerQuery
{
    public Map Map { get; set; } = map ?? throw new ArgumentNullException(nameof(map));

    /// <summary>
    /// Defaults to -1 (the same "no entity" sentinel Map.GetEntityId/IMapQuery.GetEntityIdAt
    /// already use), not the type's own default of 0 -- 0 is a real, valid entity id (likely
    /// the very first entity TestMapBuilder creates), so leaving this at the bare default would
    /// have every PlayerEntityId reader (MapWindow's camera-snap, the HUD content classes, etc.)
    /// silently treat that unrelated entity as "the player" for however long elapses before
    /// FloorBuilder.CreatePlayer actually runs and assigns the real value (see GameLoop, which
    /// now spawns the player on its first live Update() tick rather than during Initialize()).
    /// </summary>
    public int PlayerEntityId { get; set; } = -1;

    private static readonly Vector2Byte TransformSize1 = new(1, 1);

    /// <summary>
    /// Set once ComponentManager exists (World itself is constructed before it, so these can't
    /// be constructor dependencies -- see GameLoop.cs). Null means "nobody has occupancy data
    /// yet," which IsBlocking treats as "everyone is Blocking," matching every pre-Occupancy
    /// test and blueprint unchanged.
    /// </summary>
    public MultiComponentPool<NonBlockingComponent>? NonBlockingComponents { get; set; }

    /// <inheritdoc cref="NonBlockingComponents"/>
    public MultiComponentPool<ForceBlockingComponent>? ForceBlockingComponents { get; set; }

    /// <summary>
    /// Moves entityId's map-index presence from transformComponent.Position to newPosition.
    /// No-ops (leaves the map's index untouched) if either footprint is off the map, or if
    /// the destination footprint is already occupied by a different Blocking entity -- both
    /// should be impossible given MovementSystem's own CanMove gate re-checking immediately
    /// before this is reached, but MoveEntity is a public method any future caller (including
    /// a mod's own Game-layer code, which can call it directly) can reach without going
    /// through that gate, so it defends itself rather than trusting the caller blindly. The
    /// free-space defense only applies to Blocking entities -- a Tiny/Phasing entity is exempt
    /// from map occupancy entirely and must never be refused here just because some other
    /// Blocking entity already occupies the destination. Map writes are skipped altogether for
    /// non-Blocking entities (see IsBlocking); transformComponent.Position still updates for
    /// everyone via the caller (WorldEventSync), since map-index presence and transform
    /// position are tracked independently.
    /// </summary>
    public void MoveEntity(int entityId, Vector3Int newPosition, TransformComponent transformComponent)
    {
        var size = transformComponent.Size;
        var extent = new Vector3Int(size.X, size.Y, 1); // A footprint never spans more than one MapLayer.
        var oldPosition = transformComponent.Position;
        var newCube = new CubeInt(newPosition, extent);

        var isBlocking = IsBlocking(entityId);

        if (!IsOnMap(newCube) || (isBlocking && !IsFootprintFreeFor(entityId, newCube)))
        {
            return;
        }

        if (!isBlocking)
        {
            RemoveNonBlockingFootprint(entityId, oldPosition, size);
            AddNonBlockingFootprint(entityId, newPosition, size);
            return;
        }

        var oldZ = oldPosition.Z;
        var oldMaxX = oldPosition.X + size.X;
        var oldMaxY = oldPosition.Y + size.Y;
        for (var x = oldPosition.X; x < oldMaxX; x++)
        {
            for (var y = oldPosition.Y; y < oldMaxY; y++)
            {
                Map.ClearIfOccupiedBy(new Vector3Int(x, y, oldZ), entityId);
            }
        }

        var newZ = newPosition.Z;
        var newMaxX = newPosition.X + size.X;
        var newMaxY = newPosition.Y + size.Y;
        for (var x = newPosition.X; x < newMaxX; x++)
        {
            for (var y = newPosition.Y; y < newMaxY; y++)
            {
                Map.SetEntityId(new Vector3Int(x, y, newZ), entityId);
            }
        }
    }

    /// <summary>
    /// Same map-index update as MoveEntity, minus the free-space/bounds re-validation --
    /// internal, and safe ONLY when the caller has already validated the destination footprint
    /// moments earlier in the same single-threaded call with nothing else able to mutate the
    /// map in between (today: WorldEventSync.SyncMove, invoked directly by MovementSystem
    /// immediately after MovementSystem.CanMove already checked this exact footprint). Any
    /// other caller -- a mod, a future spawner -- should use the public, defensive MoveEntity
    /// instead.
    /// </summary>
    internal void MoveEntityUnchecked(int entityId, Vector3Int newPosition, TransformComponent transformComponent)
    {
        var size = transformComponent.Size;
        var oldPosition = transformComponent.Position;

        if (!IsBlocking(entityId))
        {
            RemoveNonBlockingFootprint(entityId, oldPosition, size);
            AddNonBlockingFootprint(entityId, newPosition, size);
            return;
        }

        var oldZ = oldPosition.Z;
        var oldMaxX = oldPosition.X + size.X;
        var oldMaxY = oldPosition.Y + size.Y;
        for (var x = oldPosition.X; x < oldMaxX; x++)
        {
            for (var y = oldPosition.Y; y < oldMaxY; y++)
            {
                Map.ClearIfOccupiedBy(new Vector3Int(x, y, oldZ), entityId);
            }
        }

        var newZ = newPosition.Z;
        var newMaxX = newPosition.X + size.X;
        var newMaxY = newPosition.Y + size.Y;
        for (var x = newPosition.X; x < newMaxX; x++)
        {
            for (var y = newPosition.Y; y < newMaxY; y++)
            {
                Map.SetEntityId(new Vector3Int(x, y, newZ), entityId);
            }
        }
    }

    /// <summary>True if every cell in cube is either empty or already occupied by entityId.</summary>
    private bool IsFootprintFreeFor(int entityId, CubeInt cube)
    {
        var z = cube.Position.Z;
        var maxX = cube.Position.X + cube.Size.X;
        var maxY = cube.Position.Y + cube.Size.Y;
        for (var x = cube.Position.X; x < maxX; x++)
        {
            for (var y = cube.Position.Y; y < maxY; y++)
            {
                var occupyingEntityId = Map.GetEntityId(new Vector3Int(x, y, z));
                if (occupyingEntityId != -1 && occupyingEntityId != entityId)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Fulfills IMapQuery.IsBlocking. ForceBlockingComponent wins if present (an effect
    /// forcing an otherwise-exempt entity solid); otherwise NonBlockingComponent exempts it;
    /// otherwise the default is Blocking. Both are Multi pools -- Has() means "at least one
    /// source is still active" -- so overlapping sources (two independent effects granting
    /// the same exemption) are handled correctly: one expiring doesn't affect the other.
    /// Absence of a pool (not wired up yet) is treated as "no sources," i.e. Blocking,
    /// matching every pre-Occupancy test and blueprint unchanged.
    /// </summary>
    public bool IsBlocking(int entityId)
    {
        if (ForceBlockingComponents is { } forceBlocking && forceBlocking.Has(entityId))
        {
            return true;
        }

        if (NonBlockingComponents is { } nonBlocking && nonBlocking.Has(entityId))
        {
            return false;
        }

        return true;
    }

    // Note: Position resets to (0,0,0) rather than a sentinel like Map's own -1 EntityId --
    // an inconsistency worth fixing once real despawn logic exercises this.
    public void RemoveEntityFromMap(int entityId, ref TransformComponent transformComponent)
    {
        if (IsOnMap(transformComponent.Position))
        {
            if (IsBlocking(entityId))
            {
                if (transformComponent.Size == TransformSize1)
                {
                    Map.SetEntityId(transformComponent.Position, -1);
                }
                else
                {
                    var z = transformComponent.Position.Z;
                    for (var x = transformComponent.Position.X; x < transformComponent.Position.X + transformComponent.Size.X; x++)
                    {
                        for (var y = transformComponent.Position.Y; y < transformComponent.Position.Y + transformComponent.Size.Y; y++)
                        {
                            Map.SetEntityId(new Vector3Int(x, y, z), -1);
                        }
                    }
                }
            }
            else
            {
                RemoveNonBlockingFootprint(entityId, transformComponent.Position, transformComponent.Size);
            }
        }

        transformComponent.Position = new Vector3Int();
    }

    /// <summary>
    /// Transitions a currently-Blocking entity to non-Blocking at its own current position --
    /// for a corpse (DeathSystem), which stops physically blocking movement but must stay
    /// findable/renderable at the same spot, unlike RemoveEntityFromMap (a full despawn, which
    /// also zeroes transformComponent.Position -- see that method's own doc comment). Caller
    /// must already have added whatever component makes World.IsBlocking(entityId) return
    /// false (e.g. NonBlockingComponent) before calling this, and must only call this for an
    /// entity that actually held the Blocking slot -- calling it for an already-non-Blocking
    /// entity (e.g. a Phasing Ghost, which may be sharing this tile with a real Blocking
    /// occupant) would incorrectly clear that other occupant's Blocking registration. This
    /// method only fixes up Map's own spatial index; it doesn't decide blocking state itself.
    /// </summary>
    public void ConvertToNonBlocking(int entityId, ref TransformComponent transformComponent)
    {
        if (!IsOnMap(transformComponent.Position))
        {
            return;
        }

        if (transformComponent.Size == TransformSize1)
        {
            Map.SetEntityId(transformComponent.Position, -1);
        }
        else
        {
            var z = transformComponent.Position.Z;
            for (var x = transformComponent.Position.X; x < transformComponent.Position.X + transformComponent.Size.X; x++)
            {
                for (var y = transformComponent.Position.Y; y < transformComponent.Position.Y + transformComponent.Size.Y; y++)
                {
                    Map.SetEntityId(new Vector3Int(x, y, z), -1);
                }
            }
        }

        AddNonBlockingFootprint(entityId, transformComponent.Position, transformComponent.Size);
    }

    public void PlaceEntityOnMap(int entityId, Vector3Int newPosition, ref TransformComponent transformComponent)
    {
        var size = transformComponent.Size;
        if (!IsOnMap(new CubeInt(newPosition, new Vector3Int(size.X, size.Y, 1)))) // A footprint never spans more than one MapLayer.
        {
            return;
        }

        if (IsBlocking(entityId))
        {
            if (transformComponent.Size == TransformSize1)
            {
                Map.SetEntityId(newPosition, entityId);
            }
            else
            {
                var z = newPosition.Z;
                for (var x = newPosition.X; x < newPosition.X + transformComponent.Size.X; x++)
                {
                    for (var y = newPosition.Y; y < newPosition.Y + transformComponent.Size.Y; y++)
                    {
                        Map.SetEntityId(new Vector3Int(x, y, z), entityId);
                    }
                }
            }
        }
        else
        {
            AddNonBlockingFootprint(entityId, newPosition, size);
        }

        transformComponent.Position = newPosition;
    }

    /// <summary>Shared footprint iteration for a non-Blocking entity's placement/arrival -- every cell of its X/Y extent at the given Z, matching how the Blocking branches above loop over Size.X/Size.Y.</summary>
    private void AddNonBlockingFootprint(int entityId, Vector3Int position, Vector2Byte size)
    {
        var z = position.Z;
        for (var x = position.X; x < position.X + size.X; x++)
        {
            for (var y = position.Y; y < position.Y + size.Y; y++)
            {
                Map.AddNonBlockingEntityId(new Vector3Int(x, y, z), entityId);
            }
        }
    }

    /// <summary>Shared footprint iteration for a non-Blocking entity's departure -- see AddNonBlockingFootprint.</summary>
    private void RemoveNonBlockingFootprint(int entityId, Vector3Int position, Vector2Byte size)
    {
        var z = position.Z;
        for (var x = position.X; x < position.X + size.X; x++)
        {
            for (var y = position.Y; y < position.Y + size.Y; y++)
            {
                Map.RemoveNonBlockingEntityId(new Vector3Int(x, y, z), entityId);
            }
        }
    }

    /// <summary>
    /// Places a terrain entity (the floor beneath UnderGround/Ground -- never Flying, which
    /// has no floor). Terrain is always 1x1, never moves, and never blocks, so none of the
    /// footprint/occupancy logic above applies -- it writes directly to Map's separate terrain
    /// store instead of the creature-occupancy one. Also sets transformComponent.Position,
    /// mirroring PlaceEntityOnMap -- without this, a terrain entity's own Transform stays
    /// whatever placeholder its blueprint hardcoded, and nothing can answer "given this
    /// terrain entity, where is it" (only the reverse, via Map.GetTerrainEntityId). A
    /// source-driven effect (an aura or tint source that happens to be terrain) needs exactly
    /// that entity-to-position direction to find itself.
    ///
    /// Z is (int)terrainLayer, NOT always 0 -- TerrainLayer's values are defined to line up
    /// with MapLayer's (UnderGround=0, Ground=1), so a Ground-layer terrain entity must report
    /// Z=1 to land on the same plane as the Ground-layer (Z=1) creatures standing on it. An
    /// earlier version of this hardcoded Z=0, which silently broke every terrain-anchored
    /// aura/tint source placed via TerrainLayer.Ground (the common case -- Ground is what
    /// players/creatures actually walk on): the source's contribution was baked into the
    /// wrong Z-plane (UnderGround) and so was invisible to anything querying at Z=1.
    /// </summary>
    public void PlaceTerrainOnMap(int entityId, int x, int y, TerrainLayer terrainLayer, ref TransformComponent transformComponent)
    {
        if (!IsOnMap(new Vector3Int(x, y, 0)))
        {
            return;
        }

        Map.SetTerrainEntityId(x, y, terrainLayer, entityId);
        transformComponent.Position = new Vector3Int(x, y, (int)terrainLayer);
    }

    public bool IsOnMap(Vector3Int coordinates) =>
        coordinates.X >= 0 && coordinates.Y >= 0 && coordinates.Z >= 0
        && coordinates.X < Map.Size.X && coordinates.Y < Map.Size.Y && coordinates.Z < Map.Size.Z;

    /// <summary>
    /// True only if the whole cube is on the map, so multi-tile entities never move
    /// partially off it. Size is an extent, not an inclusive far corner -- a cube occupies
    /// cells [Position, Position + Size), so the last actually-occupied cell is
    /// Position + Size - 1, not Position + Size. Checking Position + Size directly would
    /// reject a footprint sitting flush against the map's far edge (e.g. a 1x1x1 cube at the
    /// map's last valid row/column) even though every cell it occupies is on the map.
    /// </summary>
    public bool IsOnMap(CubeInt cube) => IsOnMap(cube.Position) && IsOnMap(cube.Position + cube.Size - new Vector3Int(1, 1, 1));

    /// <inheritdoc cref="IMapQuery"/>
    public Vector3Int MapSize => Map.Size;

    /// <inheritdoc cref="IMapQuery"/>
    public int GetEntityIdAt(Vector3Int position) => Map.GetEntityId(position);

    /// <inheritdoc cref="IMapQuery"/>
    public IReadOnlyList<int> GetNonBlockingEntityIdsAt(Vector3Int position) => Map.GetNonBlockingEntityIdsAt(position);

    /// <inheritdoc cref="IMapQuery"/>
    public int GetTerrainEntityIdAt(Vector3Int position) =>
        Map.TerrainLayerFor(position.Z) is { } terrainLayer ? Map.GetTerrainEntityId(position.X, position.Y, terrainLayer) : -1;

    /// <summary>
    /// Row-major (X fastest-varying) batched occupant scan -- box.Size.Z is ignored, every
    /// cell is read at box.Position.Z. One batched call instead of one interface call per
    /// cell, for anything scanning a falloff radius (aura range) instead of a fixed 3x3
    /// neighborhood.
    /// </summary>
    /// <inheritdoc cref="IMapQuery"/>
    public void GetEntityIdsInBox(CubeInt box, Span<int> entityIds)
    {
        var z = box.Position.Z;
        var index = 0;
        for (var y = box.Position.Y; y < box.Position.Y + box.Size.Y; y++)
        {
            for (var x = box.Position.X; x < box.Position.X + box.Size.X; x++)
            {
                var position = new Vector3Int(x, y, z);
                entityIds[index] = IsOnMap(position) ? Map.GetEntityId(position) : -1;
                index++;
            }
        }
    }
}