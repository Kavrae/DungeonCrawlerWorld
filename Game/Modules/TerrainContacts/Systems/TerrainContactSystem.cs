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
/// anything on the entity (TerrainContactExposureComponent), and those sit on a timer wheel: only
/// the ones due this frame are touched, on their exact frame at every processing tier.
/// </para>
/// <para>
/// Terrain that changes under an entity counts as stepping onto the new terrain
/// (TerrainChangedEvent, and TerrainRegistry.DefinitionChanged for a terrain that gains a contact),
/// and every repeat reads the terrain where the entity stands rather than trusting what it stood on
/// last time, so no exposure outlives the terrain that caused it.
/// </para>
/// </remarks>
public sealed class TerrainContactSystem : ISystem
{
    /// <summary>Every frame: this frame's moves must be drained this frame, and the wheel only touches exposures actually due.</summary>
    public byte StripeCount => 1;

    private readonly TerrainRegistry _terrain;
    private readonly PackedComponentPool<TerrainContactExposureComponent> _exposures;
    private readonly EffectServices _effectServices;
    private readonly IMapQuery _mapQuery;
    private readonly DirectComponentPool<TransformComponent> _transforms;
    private readonly FrameEventBuffer<EntityMovedEvent> _movedEntities;
    private readonly PackedComponentPool<DeadComponent> _deadEntities;
    private readonly PackedTimerWheel<TerrainContactExposureComponent> _wheel;
    private readonly SimulationClock _clock;
    private readonly SimulationScope _simulationScope;
    private readonly List<int> _occupantIdsScratch = [];

    // Cached once instead of passing the Tick method group every Update -- unlike a static
    // method group, an instance method group conversion allocates a fresh delegate on every
    // evaluation (the compiler can't cache a delegate that captures `this`), so passing `Tick`
    // directly there would allocate one every frame for no reason.
    private readonly TimerFired<TerrainContactExposureComponent> _tick;

    public TerrainContactSystem(
        TerrainRegistry terrain,
        PackedComponentPool<TerrainContactExposureComponent> exposures,
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
        _wheel = new PackedTimerWheel<TerrainContactExposureComponent>(exposures, simulationScope);

        simulationScope.EntityResumed += OnEntityResumed;
        effectServices.EventBus.Subscribe<TerrainChangedEvent>(OnTerrainChanged);
        terrain.DefinitionChanged += OnTerrainDefinitionChanged;
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
            StepOnto(moved.EntityId, moved.NewPosition, time.FrameCount);
        }

        _wheel.Tick(time.FrameCount, _tick);
    }

    /// <summary>Applies the contact of the terrain at position to entityId as one stepping onto it, and starts, restarts or ends its repeat to match.</summary>
    private void StepOnto(int entityId, Vector3Int position, long now)
    {
        if (_deadEntities.Has(entityId))
        {
            return;
        }

        var terrainTypeId = _mapQuery.GetTerrainAt(position).TypeId;
        if (!_terrain.TryGet(terrainTypeId, out var definition) || definition.Contact is not { } contact)
        {
            _exposures.Remove(entityId);
            return;
        }

        var outcome = ApplyContact(entityId, terrainTypeId, definition, contact, now, announcesRefusal: true);

        if (contact.RepeatEveryFrames is not { } repeatEveryFrames)
        {
            _exposures.Remove(entityId);
            return;
        }

        var exposure = new TerrainContactExposureComponent(FrameDeadline.AfterStaggered(now, repeatEveryFrames, entityId), terrainTypeId)
        {
            Refused = outcome == EffectOutcome.Refused,
        };

        if (_exposures.Has(entityId))
        {
            _exposures.TryUpdate(entityId, exposure, static (ref TerrainContactExposureComponent held, TerrainContactExposureComponent started) =>
            {
                held.NextTickFrame = started.NextTickFrame;
                held.TerrainTypeId = started.TerrainTypeId;
                held.Refused = started.Refused;
            });
        }
        else
        {
            _exposures.Add(entityId, exposure);
        }
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

    /// <summary>Applies one repeat of the contact the entity is standing on, or ends the exposure. Returns whether the exposure should be removed -- see TimerFired's contract.</summary>
    /// <remarks>
    /// Removed when the entity is dead, has no position, or no longer stands on terrain whose contact
    /// repeats. The terrain is read where the entity stands now, so one that changed since the last
    /// repeat is the one applied. A repeat that is refused again after a refused one stays silent.
    /// </remarks>
    private bool Tick(int entityId, TerrainContactExposureComponent exposure, long now)
    {
        if (_deadEntities.Has(entityId) || !_transforms.TryGetReadonly(entityId, out var transform))
        {
            return true;
        }

        var terrainTypeId = _mapQuery.GetTerrainAt(transform.Position).TypeId;
        if (!_terrain.TryGet(terrainTypeId, out var definition) || definition.Contact is not { } contact || contact.RepeatEveryFrames is not { } repeatEveryFrames)
        {
            return true;
        }

        var alreadyRefused = exposure.Refused && terrainTypeId == exposure.TerrainTypeId;
        var outcome = ApplyContact(entityId, terrainTypeId, definition, contact, now, announcesRefusal: !alreadyRefused);

        _exposures.TryUpdate(entityId, (terrainTypeId, repeatEveryFrames, Refused: outcome == EffectOutcome.Refused),
            static (ref TerrainContactExposureComponent held, (ushort TerrainTypeId, ushort PeriodFrames, bool Refused) state) =>
            {
                held.TerrainTypeId = state.TerrainTypeId;
                held.Refused = state.Refused;
                held.RepeatEvery(state.PeriodFrames);
            });

        return false;
    }

    /// <summary>A cell's terrain changed: whoever stands on it has just stepped onto the new terrain.</summary>
    private void OnTerrainChanged(TerrainChangedEvent changed) =>
        StepOccupantsOnto(new Vector3Int(changed.X, changed.Y, (int)changed.TerrainLayer), _clock.CurrentFrame);

    /// <summary>A terrain gained a contact, or its contact started repeating: whoever stands on that terrain holds no exposure to it yet, so each steps onto it now.</summary>
    /// <remarks>Any other change needs nothing here: a contact's effects, interval and tags are read from the definition at every application, and an exposure to a contact that went away ends on its next repeat.</remarks>
    private void OnTerrainDefinitionChanged(ushort terrainTypeId, TerrainDefinition previous, TerrainDefinition current)
    {
        if (current.Contact is null)
        {
            return;
        }

        var gainedContact = previous.Contact is null;
        var startedRepeating = previous.Contact is { RepeatEveryFrames: null } && current.Contact.RepeatEveryFrames is not null;
        if (!gainedContact && !startedRepeating)
        {
            return;
        }

        var now = _clock.CurrentFrame;
        TerrainCells.ForEachOfType(_mapQuery, terrainTypeId, position => StepOccupantsOnto(position, now));
    }

    private void StepOccupantsOnto(Vector3Int position, long now)
    {
        if (!_mapQuery.IsOnMap(position))
        {
            return;
        }

        // Copied first: applying a contact can change who occupies the cell.
        _occupantIdsScratch.Clear();
        _occupantIdsScratch.AddRange(_mapQuery.GetOccupantEntityIdSpanAt(position));

        foreach (var occupantId in _occupantIdsScratch)
        {
            // A frozen occupant is left alone: it steps onto whatever it stands on when it resumes (OnEntityResumed).
            if (_simulationScope.IsSimulated(occupantId))
            {
                StepOnto(occupantId, position, now);
            }
        }
    }

    /// <summary>Picks a resumed entity back up: the repeats owed while it froze are skipped, not applied, and if the terrain under it is no longer the one it was exposed to, it steps onto what is there now.</summary>
    /// <remarks>Runs after the wheel's own resume handler has rescheduled the stale deadline; rewriting it here reschedules again and leaves that earlier entry to be dropped as stale, the wheel's ordinary lazy cancellation.</remarks>
    private void OnEntityResumed(int entityId)
    {
        var now = _clock.CurrentFrame;
        SkipOwedTicks(entityId, now);

        if (!_transforms.TryGetReadonly(entityId, out var transform) || !_mapQuery.IsOnMap(transform.Position))
        {
            return;
        }

        var terrainTypeId = _mapQuery.GetTerrainAt(transform.Position).TypeId;
        var exposedTerrainTypeId = _exposures.TryGetReadonly(entityId, out var exposure) ? exposure.TerrainTypeId : TerrainRegistry.None;
        var repeats = _terrain.TryGetContact(terrainTypeId, out var contact) && contact.RepeatEveryFrames is not null;

        if (terrainTypeId != exposedTerrainTypeId && (repeats || exposedTerrainTypeId != TerrainRegistry.None))
        {
            StepOnto(entityId, transform.Position, now);
        }
    }

    /// <summary>Moves the entity's exposure past every repeat owed while it was frozen, applying none of them: an exposure is an interaction with the surroundings, not an effect already in progress, so it does not accrue while frozen.</summary>
    private void SkipOwedTicks(int entityId, long now)
    {
        if (!_exposures.TryGetReadonly(entityId, out var exposure)
            || !_terrain.TryGetContact(exposure.TerrainTypeId, out var contact)
            || contact.RepeatEveryFrames is not { } repeatEveryFrames)
        {
            return;
        }

        _exposures.TryUpdate(entityId, (repeatEveryFrames, now), static (ref TerrainContactExposureComponent e, (ushort PeriodFrames, long Now) state) => e.SkipOwedPeriods(state.PeriodFrames, state.Now));
    }
}
