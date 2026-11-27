using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Effects;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health;
using Game.Modules.TerrainContacts.Components;
using Game.Terrain;
using Game.World;

namespace Game.Modules.TerrainContacts.Systems;

/// <summary>Applies a terrain's contact (TerrainDefinition.Contact) to whatever stands on it: on stepping on, and again at the contact's interval for as long as the entity stays.</summary>
/// <remarks>
/// <para>
/// Knows no particular effect: a contact is a list of Effect, the same lists an action or item
/// holds, applied with no source entity and attributed to the terrain. Terrain cells are not
/// entities, so everything comes from the terrain's definition, read every time it is needed -- a
/// definition replaced during a session takes effect on the next application.
/// </para>
/// <para>
/// Stepping on is detected by draining MovementSystem's shared FrameEventBuffer&lt;EntityMovedEvent&gt;
/// at the start of each Update, not an EventBus subscription per move (see FrameEventBuffer). Every
/// buffered move landing on contact terrain applies it and restarts the repeat -- including a move
/// from one such cell to another, not just a fresh entry. Only a contact that repeats leaves
/// anything on the entity (TerrainContactExposureComponent, one per terrain), and those sit on a timer wheel: only
/// the ones due this frame are touched, on their exact frame at every processing tier.
/// </para>
/// <para>
/// Terrain that changes under an entity counts as stepping onto the new terrain (TerrainChangedEvent),
/// and every repeat reads the terrain where the entity stands rather than trusting what it stood on
/// last time, so no exposure outlives the terrain that caused it.
/// </para>
/// <para>
/// An entity stands on every tile of its footprint: each distinct terrain with a contact under it applies once, on
/// stepping on and at that terrain's own interval -- a large entity with one corner on lava burns, as a small one on
/// that tile does, and lava under several of its tiles still burns once.
/// </para>
/// </remarks>
public sealed class TerrainContactSystem : ISystem
{
    /// <summary>Every frame: this frame's moves must be drained this frame, and the wheel only touches exposures actually due.</summary>
    public byte StripeCount => 1;

    private readonly TerrainRegistry _terrain;
    private readonly MultiComponentPool<TerrainContactExposureComponent> _exposures;
    private readonly EffectServices _effectServices;
    private readonly IMapQuery _mapQuery;
    private readonly DirectComponentPool<TransformComponent> _transforms;
    private readonly FrameEventBuffer<EntityMovedEvent> _movedEntities;
    private readonly PackedComponentPool<DeadComponent> _deadEntities;
    private readonly MultiTimerWheel<TerrainContactExposureComponent> _wheel;
    private readonly SimulationClock _clock;
    private readonly SimulationScope _simulationScope;
    /// <summary>The most distinct terrains a footprint can stand on: one per tile of the largest footprint.</summary>
    private const int MaximumFootprintTerrains = Blueprints.ResolvedBlueprint.MaximumFootprintTiles * Blueprints.ResolvedBlueprint.MaximumFootprintTiles;

    /// <summary>The most occupants of a changed cell copied onto the stack; more are copied to the heap.</summary>
    private const int MaximumStackOccupants = 16;

    /// <summary>The terrains of the exposures an entity holds that no tile of its footprint is on any more.</summary>
    private readonly List<ushort> _staleExposureTerrainsScratch = [];

    // Cached once instead of passing the Tick method group every Update -- unlike a static
    // method group, an instance method group conversion allocates a fresh delegate on every
    // evaluation (the compiler can't cache a delegate that captures `this`), so passing `Tick`
    // directly there would allocate one every frame for no reason.
    private readonly TimerFired<TerrainContactExposureComponent> _tick;

    public TerrainContactSystem(
        TerrainRegistry terrain,
        MultiComponentPool<TerrainContactExposureComponent> exposures,
        EffectServices effectServices,
        IMapQuery mapQuery,
        DirectComponentPool<TransformComponent> transforms,
        FrameEventBuffer<EntityMovedEvent> movedEntities,
        SimulationClock simulationClock,
        SimulationScope simulationScope)
    {
        _terrain = terrain;
        _exposures = exposures;
        _effectServices = effectServices;
        _mapQuery = mapQuery;
        _transforms = transforms;
        _movedEntities = movedEntities;
        _deadEntities = effectServices.DeadEntities;
        _clock = simulationClock;
        _simulationScope = simulationScope;
        _tick = Tick;
        _wheel = new MultiTimerWheel<TerrainContactExposureComponent>(exposures, simulationScope);

        simulationScope.EntityResumed += OnEntityResumed;
        effectServices.EventBus.Subscribe<TerrainChangedEvent>(OnTerrainChanged);
    }

    /// <summary>
    /// Drains this frame's moves first (stepping onto, between or off contact terrain), then fires
    /// every exposure due a repeat this frame. The drain is not tier-gated -- see AuraSystem's own
    /// Update comment for why (already self-limiting to entities that moved this exact frame).
    /// </summary>
    public void Update(EngineTime time, byte stripeIndex)
    {
        foreach (var moved in _movedEntities.ItemSpan)
        {
            StepOnto(moved.EntityId, moved.NewPosition, moved.Size, time.FrameCount);
        }

        _wheel.Tick(time.FrameCount, _tick);
    }

    /// <summary>Applies the contact of each distinct terrain under the footprint at position to entityId as one stepping onto it, and starts, restarts or ends each repeat to match.</summary>
    private void StepOnto(int entityId, Vector3Int position, Vector2Byte size, long now)
    {
        if (_deadEntities.Has(entityId))
        {
            return;
        }

        Span<ushort> contactTerrains = stackalloc ushort[MaximumFootprintTerrains];
        contactTerrains = contactTerrains[..CollectContactTerrainsUnder(position, size, contactTerrains)];

        _staleExposureTerrainsScratch.Clear();
        for (var denseIndex = _exposures.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = _exposures.GetNextDenseIndex(denseIndex))
        {
            var exposedTerrainTypeId = _exposures.GetReadonlyByDenseIndex(denseIndex).TerrainTypeId;
            if (!contactTerrains.Contains(exposedTerrainTypeId))
            {
                _staleExposureTerrainsScratch.Add(exposedTerrainTypeId);
            }
        }

        foreach (var staleTerrainTypeId in _staleExposureTerrainsScratch)
        {
            _exposures.RemoveFirst(entityId, new TimerKeyMatcher<TerrainContactExposureComponent>(staleTerrainTypeId));
        }

        foreach (var terrainTypeId in contactTerrains)
        {
            StepOntoTerrain(entityId, terrainTypeId, now);
        }
    }

    /// <summary>Applies one terrain's contact to entityId as one stepping onto it, and starts or restarts its repeat, or ends it for a contact that doesn't repeat.</summary>
    private void StepOntoTerrain(int entityId, ushort terrainTypeId, long now)
    {
        if (!_terrain.TryGet(terrainTypeId, out var definition) || definition.Contact is not { } contact)
        {
            return;
        }

        var outcome = ApplyContact(entityId, terrainTypeId, definition, contact, now, announcesRefusal: true);
        var matcher = new TimerKeyMatcher<TerrainContactExposureComponent>(terrainTypeId);

        if (contact.RepeatEveryFrames is not { } repeatEveryFrames)
        {
            _exposures.RemoveFirst(entityId, matcher);
            return;
        }

        var nextTickFrame = FrameDeadline.AfterStaggered(now, repeatEveryFrames, entityId);
        var refused = outcome == EffectOutcome.Refused;

        using (var heldExposure = _exposures.TryWriteFirst(entityId, matcher))
        {
            if (heldExposure.Found)
            {
                heldExposure.Value.NextTickFrame = nextTickFrame;
                heldExposure.Value.Refused = refused;
                return;
            }
        }

        _exposures.Add(entityId, new TerrainContactExposureComponent(nextTickFrame, terrainTypeId) { Refused = refused });
    }

    /// <summary>Fills terrains with each distinct terrain that has a contact under any tile of the footprint at position, and returns how many.</summary>
    /// <remarks>Each caller holds its own buffer: applying a contact can change terrain, which steps occupants onto it again before the caller has finished.</remarks>
    private int CollectContactTerrainsUnder(Vector3Int position, Vector2Byte size, Span<ushort> terrains)
    {
        var count = 0;
        for (var deltaY = 0; deltaY < size.Y; deltaY++)
        {
            for (var deltaX = 0; deltaX < size.X; deltaX++)
            {
                var terrainTypeId = _mapQuery.GetTerrainAt(position + new Vector3Int(deltaX, deltaY, 0)).TypeId;
                if (_terrain.TryGetContact(terrainTypeId, out _) && !terrains[..count].Contains(terrainTypeId))
                {
                    terrains[count++] = terrainTypeId;
                }
            }
        }

        return count;
    }

    /// <summary>Whether any tile of the footprint at position is on terrainTypeId.</summary>
    private bool FootprintStandsOn(Vector3Int position, Vector2Byte size, ushort terrainTypeId)
    {
        for (var deltaY = 0; deltaY < size.Y; deltaY++)
        {
            for (var deltaX = 0; deltaX < size.X; deltaX++)
            {
                if (_mapQuery.GetTerrainAt(position + new Vector3Int(deltaX, deltaY, 0)).TypeId == terrainTypeId)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private EffectOutcome ApplyContact(int entityId, ushort terrainTypeId, TerrainDefinition definition, TerrainContact contact, long now, bool announcesRefusal)
    {
        var context = new EffectContext(_effectServices, ActionSource.FromTerrain(terrainTypeId), SourceEntityId: null, entityId, definition.Name, contact.Tags, now)
        {
            GroundContactBodyPartRule = new BodyPartTargetRule(contact.GroundContactBodyPart, BodyPartFallback.Bottommost),
            AnnouncesRefusal = announcesRefusal,
        };

        return EffectSequence.Apply(contact.Effects, in context);
    }

    /// <summary>Applies one repeat of the exposure's terrain contact, or ends the exposure. Returns whether the exposure should be removed -- see TimerFired's contract; the wheel removes that one instance, by terrain.</summary>
    /// <remarks>
    /// Removed when the entity is dead, has no position, no tile of its footprint stands on the exposure's terrain any
    /// more, or that terrain's contact no longer repeats. A repeat that is refused again after a refused one stays silent.
    /// </remarks>
    private bool Tick(int entityId, TerrainContactExposureComponent exposure, long now)
    {
        if (_deadEntities.Has(entityId) || !_transforms.TryGetReadonly(entityId, out var transform))
        {
            return true;
        }

        var terrainTypeId = exposure.TerrainTypeId;
        if (!FootprintStandsOn(transform.Position, transform.Size, terrainTypeId))
        {
            TakeUpRepeatingTerrainsUnder(entityId, transform.Position, transform.Size, now);
            return true;
        }

        if (!_terrain.TryGet(terrainTypeId, out var definition) || definition.Contact is not { } contact || contact.RepeatEveryFrames is not { } repeatEveryFrames)
        {
            return true;
        }

        var outcome = ApplyContact(entityId, terrainTypeId, definition, contact, now, announcesRefusal: !exposure.Refused);

        using var heldExposure = _exposures.TryWriteFirst(entityId, new TimerKeyMatcher<TerrainContactExposureComponent>(terrainTypeId));
        if (heldExposure.Found)
        {
            heldExposure.Value.Refused = outcome == EffectOutcome.Refused;
            heldExposure.Value.AdvanceOnePeriod(repeatEveryFrames);
        }

        return false;
    }

    /// <summary>For a repeat that found its terrain gone from under the footprint: every repeating terrain under it that the entity holds no exposure to applies now and starts its own repeat, so a repeat always reads the terrain where the entity stands.</summary>
    private void TakeUpRepeatingTerrainsUnder(int entityId, Vector3Int position, Vector2Byte size, long now)
    {
        Span<ushort> contactTerrains = stackalloc ushort[MaximumFootprintTerrains];
        foreach (var terrainTypeId in contactTerrains[..CollectContactTerrainsUnder(position, size, contactTerrains)])
        {
            if (_terrain.TryGetContact(terrainTypeId, out var contact) && contact.RepeatEveryFrames is not null
                && _exposures.CountMatching(entityId, new TimerKeyMatcher<TerrainContactExposureComponent>(terrainTypeId)) == 0)
            {
                StepOntoTerrain(entityId, terrainTypeId, now);
            }
        }
    }

    /// <summary>A cell's terrain changed: whoever stands on it has just stepped onto the terrain under its footprint.</summary>
    private void OnTerrainChanged(TerrainChangedEvent changed) =>
        StepOccupantsOnto(new Vector3Int(changed.X, changed.Y, (int)changed.TerrainLayer), _clock.CurrentFrame);

    private void StepOccupantsOnto(Vector3Int position, long now)
    {
        if (!_mapQuery.IsOnMap(position))
        {
            return;
        }

        // Copied first, into a buffer of this call's own: applying a contact can change who occupies the cell, or change
        // terrain again, which steps occupants onto it before this loop has finished.
        var occupantSpan = _mapQuery.GetOccupantEntityIdSpanAt(position);
        Span<int> occupantIds = occupantSpan.Length <= MaximumStackOccupants ? stackalloc int[MaximumStackOccupants] : new int[occupantSpan.Length];
        occupantIds = occupantIds[..occupantSpan.Length];
        occupantSpan.CopyTo(occupantIds);

        foreach (var occupantId in occupantIds)
        {
            // A frozen occupant is left alone: it steps onto whatever it stands on when it resumes (OnEntityResumed).
            if (_simulationScope.IsSimulated(occupantId) && _transforms.TryGetReadonly(occupantId, out var occupantTransform))
            {
                StepOnto(occupantId, occupantTransform.Position, occupantTransform.Size, now);
            }
        }
    }

    /// <summary>Picks a resumed entity back up: the repeats owed while it froze are skipped, not applied, and if the repeating terrains under its footprint are no longer the ones it was exposed to, it steps onto what is there now.</summary>
    /// <remarks>Runs after the wheel's own resume handler has rescheduled the stale deadlines; rewriting them here reschedules again and leaves those earlier entries to be dropped as stale, the wheel's ordinary lazy cancellation.</remarks>
    private void OnEntityResumed(int entityId)
    {
        var now = _clock.CurrentFrame;
        SkipOwedTicks(entityId, now);

        if (!_transforms.TryGetReadonly(entityId, out var transform) || !_mapQuery.IsOnMap(transform.Position))
        {
            return;
        }

        Span<ushort> contactTerrains = stackalloc ushort[MaximumFootprintTerrains];
        var repeatingTerrainCount = 0;
        var exposuresMatch = true;
        foreach (var terrainTypeId in contactTerrains[..CollectContactTerrainsUnder(transform.Position, transform.Size, contactTerrains)])
        {
            if (_terrain.TryGetContact(terrainTypeId, out var contact) && contact.RepeatEveryFrames is not null)
            {
                repeatingTerrainCount++;
                exposuresMatch &= _exposures.CountMatching(entityId, new TimerKeyMatcher<TerrainContactExposureComponent>(terrainTypeId)) > 0;
            }
        }

        if (!exposuresMatch || repeatingTerrainCount != _exposures.CountForEntity(entityId))
        {
            StepOnto(entityId, transform.Position, transform.Size, now);
        }
    }

    /// <summary>Moves each of the entity's exposures past every repeat owed while it was frozen, applying none of them: an exposure is an interaction with the surroundings, not an effect already in progress, so it does not accrue while frozen.</summary>
    private void SkipOwedTicks(int entityId, long now)
    {
        for (var denseIndex = _exposures.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = _exposures.GetNextDenseIndex(denseIndex))
        {
            var exposure = _exposures.GetReadonlyByDenseIndex(denseIndex);
            if (_terrain.TryGetContact(exposure.TerrainTypeId, out var contact) && contact.RepeatEveryFrames is { } repeatEveryFrames)
            {
                _exposures.UpdateByDenseIndex(denseIndex, new SkipOwedPeriodsUpdate<TerrainContactExposureComponent>(repeatEveryFrames, now));
            }
        }
    }
}
