using Engine.ECS.Components.Stores;
using Engine.ECS.Entities;
using Engine.Math;
using Game.Modules.Core.Components;

namespace Game.World;

/// <summary>The in-memory game world</summary>
/// <remarks>The map and bookkeeping for entities placed on it.</remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class World(Map map) : IMapQuery, IPlayerQuery
{
    public Map Map { get; set; } = map ?? throw new ArgumentNullException(nameof(map));

    /// <summary>The player character's entity id</summary>
    /// <remarks>Defaults to -1 as the standard sentinel</remarks>
    public int PlayerEntityId { get; set; } = -1;

    private static readonly Vector2Byte TransformSize1 = new(1, 1);

    /// <summary>Tracks the components that temporarily change a blocking entity to non-blocking</summary>
    public MultiComponentPool<NonBlockingComponent>? NonBlockingComponents { get; set; }

    /// <summary>Tracks the components that temporarily change a non-blocking entity to blocking</summary>
    public MultiComponentPool<ForceBlockingComponent>? ForceBlockingComponents { get; set; }

    /// <summary>Used by PlaceTerrainOnMap to destroy any terrain entity it replaces.</summary>
    /// <remarks>World is constructed before Bootstrapper.Build produces an EntityManager (see NonBlockingComponents/ForceBlockingComponents above for why), so this can't be a constructor dependency either -- wired up the same way, post-construction.</remarks>
    public EntityManager? EntityManager { get; set; }

    /// <summary> Moves entityId's map-index presence from transformComponent.Position to newPosition.</summary>
    /// <remarks>
    /// No-ops (leaves the map's index untouched) if either footprint is off the map, or if
    /// the destination footprint is already occupied by a different Blocking entity 
    /// A nonblocking entity is exempt from map occupancy.
    /// </remarks>
    public void MoveEntity(int entityId, Vector3Int newPosition, TransformComponent transformComponent)
    {
        var size = transformComponent.Size;
        var newCube = new CubeInt(newPosition, new Vector3Int(size.X, size.Y, 1)); // A footprint never spans more than one MapLayer.
        var isBlocking = IsBlocking(entityId);

        if (!IsValidDestination(entityId, newCube, isBlocking))
        {
            return;
        }

        MoveEntityUnchecked(entityId, newPosition, transformComponent, isBlocking);
    }

    /// <summary>
    /// Same map-index update as MoveEntity, minus the free-space/bounds re-validation --
    /// internal, and safe ONLY when the caller has already validated the destination footprint
    /// moments earlier in the same single-threaded call with nothing else able to mutate the
    /// map in between (today: MoveEntity itself, immediately above, plus WorldEventSync.SyncMove,
    /// invoked directly by MovementSystem immediately after MovementSystem.CanMove already
    /// checked this exact footprint). Any other caller -- a mod, a future spawner -- should use
    /// the public, defensive MoveEntity instead.
    /// </summary>
    /// <remarks>
    /// isBlocking is a caller-supplied parameter, not re-derived internally via IsBlocking(entityId)
    /// -- both callers already computed it moments earlier for their own IsValidDestination check,
    /// so re-querying here would just repeat the same MultiComponentPool lookups for the identical
    /// answer. It also decides only which NEW footprint kind to register at newPosition; departure
    /// from oldPosition goes through RemoveFootprint, which no longer needs to know or guess which
    /// kind was actually registered there (see RemoveFootprint's own doc comment).
    /// </remarks>
    internal void MoveEntityUnchecked(int entityId, Vector3Int newPosition, TransformComponent transformComponent, bool isBlocking)
    {
        var size = transformComponent.Size;
        var oldPosition = transformComponent.Position;

        RemoveFootprint(entityId, oldPosition, size);

        if (isBlocking)
        {
            AddBlockingFootprint(entityId, newPosition, size);
        }
        else
        {
            AddNonBlockingFootprint(entityId, newPosition, size);
        }
    }

    /// <summary>Whether newCube is a legal destination for entityId -- on the map, and (only when isBlocking) not occupied by a different Blocking entity.</summary>
    private bool IsValidDestination(int entityId, CubeInt newCube, bool isBlocking) =>
        IsOnMap(newCube) && (!isBlocking || IsFootprintFreeFor(entityId, newCube));

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
                var occupyingEntityId = Map.GetBlockingEntityId(new Vector3Int(x, y, z));
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

    // Note: Position resets to (0,0,0). TODO replace with persistent entity storage
    public void RemoveEntityFromMap(int entityId, ref TransformComponent transformComponent)
    {
        if (IsOnMap(transformComponent.Position))
        {
            RemoveFootprint(entityId, transformComponent.Position, transformComponent.Size);
        }

        transformComponent.Position = new Vector3Int();
    }

    /// <summary> Transitions a currently-Blocking entity to non-Blocking at its own current position </summary>
    public void ConvertToNonBlocking(int entityId, ref TransformComponent transformComponent)
    {
        if (!IsOnMap(transformComponent.Position))
        {
            return;
        }

        RemoveFootprint(entityId, transformComponent.Position, transformComponent.Size);
        AddNonBlockingFootprint(entityId, transformComponent.Position, transformComponent.Size);
    }

    /// <summary>Places an entity on the map at the specified position.</summary>
    /// <param name="entityId">The ID of the entity to place.</param>
    /// <param name="newPosition">The position at which to place the entity.</param>
    /// <param name="transformComponent">The transform component of the entity.</param>
    /// <remarks>No-ops if the footprint is off the map, or if a Blocking entity's destination footprint is already occupied by a different Blocking entity -- mirrors MoveEntity's guard. A nonblocking entity is exempt from map occupancy.</remarks>
    public void PlaceEntityOnMap(int entityId, Vector3Int newPosition, ref TransformComponent transformComponent)
    {
        var size = transformComponent.Size;
        var newCube = new CubeInt(newPosition, new Vector3Int(size.X, size.Y, 1)); // A footprint never spans more than one MapLayer.
        var isBlocking = IsBlocking(entityId);
        if (!IsValidDestination(entityId, newCube, isBlocking))
        {
            return;
        }

        if (isBlocking)
        {
            AddBlockingFootprint(entityId, newPosition, size);
        }
        else
        {
            AddNonBlockingFootprint(entityId, newPosition, size);
        }

        transformComponent.Position = newPosition;
    }

    /// <summary>Shared footprint iteration for a Blocking entity's placement/arrival -- every cell of its X/Y extent at the given Z, with a single-cell fast path for a 1x1 footprint instead of entering the loop.</summary>
    /// <remarks>Writes both the O(1) Blocking fast-path index and the occupant index -- a Blocking entity is discoverable both ways (see Map's class doc comment).</remarks>
    private void AddBlockingFootprint(int entityId, Vector3Int position, Vector2Byte size)
    {
        if (size == TransformSize1)
        {
            Map.SetBlockingEntityId(position, entityId);
            Map.AddOccupantEntityId(position, entityId);
            return;
        }

        var z = position.Z;
        for (var x = position.X; x < position.X + size.X; x++)
        {
            for (var y = position.Y; y < position.Y + size.Y; y++)
            {
                var cell = new Vector3Int(x, y, z);
                Map.SetBlockingEntityId(cell, entityId);
                Map.AddOccupantEntityId(cell, entityId);
            }
        }
    }

    /// <summary>
    /// Shared footprint iteration for ANY entity's departure -- Blocking or not -- clearing
    /// every index it might actually be registered in at position, rather than trusting the
    /// entity's current IsBlocking() to say which single index applies.
    /// </summary>
    /// <remarks>
    /// Unconditionally attempts both: Map.ClearBlockingIfOccupiedBy (a no-op if entityId isn't
    /// the cell's recorded Blocking occupant -- e.g. it never held that slot to begin with) and
    /// Map.RemoveOccupantEntityId (a no-op if it isn't recorded there either). This makes
    /// removal correct based on what Map actually has stored, not on a live re-query of the
    /// entity's current blocking status -- those two can only diverge if something changes an
    /// entity's NonBlockingComponent/ForceBlockingComponent state without going through
    /// ConvertToNonBlocking first, but there's no need to depend on that never happening: this
    /// removal is safe either way. Previously named RemoveBlockingFootprint and called only when
    /// IsBlocking(entityId) was still true; every caller (MoveEntityUnchecked, RemoveEntityFromMap,
    /// ConvertToNonBlocking) now calls this same method regardless of current blocking status.
    /// </remarks>
    private void RemoveFootprint(int entityId, Vector3Int position, Vector2Byte size)
    {
        if (size == TransformSize1)
        {
            Map.ClearBlockingIfOccupiedBy(position, entityId);
            Map.RemoveOccupantEntityId(position, entityId);
            return;
        }

        var z = position.Z;
        for (var x = position.X; x < position.X + size.X; x++)
        {
            for (var y = position.Y; y < position.Y + size.Y; y++)
            {
                var cell = new Vector3Int(x, y, z);
                Map.ClearBlockingIfOccupiedBy(cell, entityId);
                Map.RemoveOccupantEntityId(cell, entityId);
            }
        }
    }

    /// <summary>Shared footprint iteration for a non-Blocking entity's placement/arrival -- every cell of its X/Y extent at the given Z, matching how AddBlockingFootprint above loops over Size.X/Size.Y.</summary>
    /// <remarks>Only writes the occupant index -- a non-Blocking entity never touches the Blocking fast-path array.</remarks>
    private void AddNonBlockingFootprint(int entityId, Vector3Int position, Vector2Byte size)
    {
        var z = position.Z;
        for (var x = position.X; x < position.X + size.X; x++)
        {
            for (var y = position.Y; y < position.Y + size.Y; y++)
            {
                Map.AddOccupantEntityId(new Vector3Int(x, y, z), entityId);
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
    /// Placement is a permanent replacement, not a merge -- if x/y/terrainLayer already holds a
    /// DIFFERENT terrain entity, that entity is destroyed (EntityManager.DestroyEntity) before
    /// the new one takes its place, so the old one can't linger as an orphan with no Map cell
    /// pointing to it. The != entityId check guards re-placing the same entity onto its own
    /// current cell -- without it, that call would destroy the very entity it's placing. A no-op
    /// if EntityManager hasn't been wired up (see its own doc comment for why it's an optional,
    /// post-construction dependency like NonBlockingComponents/ForceBlockingComponents above) --
    /// today's only real caller, FloorBuilder, always sets it first.
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

        var existingTerrainEntityId = Map.GetTerrainEntityId(x, y, terrainLayer);
        if (existingTerrainEntityId != -1 && existingTerrainEntityId != entityId)
        {
            EntityManager?.DestroyEntity(existingTerrainEntityId);
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
    public int GetEntityIdAt(Vector3Int position) => Map.GetBlockingEntityId(position);

    /// <inheritdoc cref="IMapQuery"/>
    public IReadOnlyList<int> GetOccupantEntityIdsAt(Vector3Int position) => Map.GetOccupantEntityIdsAt(position);

    /// <inheritdoc cref="IMapQuery"/>
    /// <remarks>Explicitly implemented rather than left as IMapQuery's own default -- a default interface method is only callable through an IMapQuery-typed reference, not a concrete World one, and MapWindow (this method's first caller) holds World directly.</remarks>
    public bool IsPositionOccupied(Vector3Int position) => Map.GetOccupantEntityIdsAt(position).Count > 0;

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
                entityIds[index] = IsOnMap(position) ? Map.GetBlockingEntityId(position) : -1;
                index++;
            }
        }
    }
}