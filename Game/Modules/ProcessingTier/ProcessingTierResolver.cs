using Engine.ECS.Components.Stores;
using Engine.Math;
using Game.Modules.Core.Components;
using Game.Modules.ProcessingTier.Components;

namespace Game.Modules.ProcessingTier;

/// <summary>
/// The single place an entity's ProcessingTierComponent is decided and written -- shared by the
/// spawn sequence (which assigns a tier before an entity joins any tiered pool) and by
/// ProcessingTierSystem (which moves it afterwards). See PLAN-processing-tier-rework.md.
/// </summary>
/// <remarks>
/// <para>
/// Owns the tier <b>reference position</b>: the point every non-pinned entity's tier is computed
/// from. That is the player's position once the player exists, the player's intended spawn
/// position before then (set by the spawn sequence ahead of population, so terrain and NPCs are
/// born correctly tiered), and the player's last on-map position while the player is off the map
/// (a player removed from the map is returned to that same position, so tiers computed against it
/// are correct on return rather than merely plausible while away).
/// </para>
/// <para>
/// Three ways a tier gets written, and why each exists:
/// <list type="bullet">
/// <item><see cref="CreateEntityAt"/> -- <b>tier-first</b>, silently, as the entity is created, before its
/// blueprint is built. Every TieredEntityStripeSet reads an entity's tier at the moment the entity
/// joins its driving pool (OnMemberAdded), so a tier already in place is picked up for free, with no
/// TierChanged event and no bucket migration. This is the cheap path, and the one bulk population
/// uses. Silent only because nothing can hold a just-created entity -- see its own remarks.</item>
/// <item><see cref="EnsureTiered"/> -- after placement, raising TierChanged if the tier is new or
/// different. The catch-all: World.EntityPlaced drives it, so any placement path gets a correct tier
/// without anyone having to remember to ask for one, including spawn paths that do not exist yet.
/// For an entity already given the right tier by <see cref="CreateEntityAt"/> it is a
/// read-and-compare, no write.</item>
/// <item><see cref="Retier"/> -- ProcessingTierSystem's path for an entity that moved or that a
/// player move may have carried across a boundary. Same semantics as EnsureTiered.</item>
/// </list>
/// </para>
/// <para>
/// Pinned entities (the player) are Local permanently and are never recomputed by any of the
/// three -- their Chebyshev distance from the reference is by definition not a question worth
/// asking, and "not checked or updated again" is the requirement.
/// </para>
/// </remarks>
public sealed class ProcessingTierResolver
{
    /// <summary>Chebyshev distance at or within which a non-Local entity is promoted to Local.</summary>
    public const int LocalRadiusTiles = 80;

    /// <summary>Hysteresis: once Local, an entity stays Local until it is beyond LocalRadiusTiles + this. Keeps an entity pacing along the boundary from flapping between tiers -- the "grace range" of spatial-hash area-of-interest schemes.</summary>
    public const int LocalExitBufferTiles = 16;

    /// <summary>Chebyshev distance beyond which a Local entity is demoted.</summary>
    public const int LocalExitRadiusTiles = LocalRadiusTiles + LocalExitBufferTiles;

    /// <summary>Neighborhood is the fixed grid cell of this size the reference position occupies -- an absolute cell, not a radius, which is what makes Neighborhood/Borough/Beyond change only on a cell crossing.</summary>
    public const int NeighborhoodSizeTiles = 1000;

    /// <summary>Borough is the fixed 2x2-neighborhood grid cell the reference position occupies.</summary>
    public const int BoroughSizeTiles = 2000;

    private readonly HashSet<int> _pinnedLocal = [];

    private DirectComponentPool<ProcessingTierComponent>? _tiers;
    private DirectComponentPool<TransformComponent>? _transforms;
    private ProcessingTierEvents? _events;

    /// <summary>The position every non-pinned entity's tier is computed from. Null until the spawn sequence (or the first observation of the player) establishes one -- see this class's own remarks.</summary>
    public Vector3Int? ReferencePosition { get; private set; }

    /// <summary>Connects the resolver to the pools it reads and writes. Called once from ProcessingTierModule.RegisterSystems, after components are registered -- the same shape as LocalTierRoster.Wire.</summary>
    public void Wire(DirectComponentPool<ProcessingTierComponent> tiers, DirectComponentPool<TransformComponent> transforms, ProcessingTierEvents events)
    {
        ArgumentNullException.ThrowIfNull(tiers);
        ArgumentNullException.ThrowIfNull(transforms);
        ArgumentNullException.ThrowIfNull(events);

        _tiers = tiers;
        _transforms = transforms;
        _events = events;
    }

    /// <summary>Sets the reference position. The spawn sequence calls this with the player's intended spawn position before population; ProcessingTierSystem keeps it current as the player moves.</summary>
    public void SetReferencePosition(Vector3Int position) => ReferencePosition = position;

    /// <summary>Whether entityId is pinned Local -- see <see cref="PinLocal"/>.</summary>
    public bool IsPinned(int entityId) => _pinnedLocal.Contains(entityId);

    /// <summary>
    /// Pins entityId to Local permanently, silently if it has no tier yet. Called for the player at
    /// spawn, before its blueprint is built, so every tiered pool it joins sees Local from the start.
    /// Never recomputed afterwards by anything in this class, including after the entity leaves the
    /// map.
    /// </summary>
    /// <returns>Whether the tier actually changed -- an entity that already existed at a different tier needs the caller to raise TierChanged so existing stripe-set memberships follow. <see cref="PinLocalAndNotify"/> does that for you.</returns>
    private bool PinLocal(int entityId)
    {
        var tiers = RequireWired();
        _pinnedLocal.Add(entityId);

        if (tiers.TryGetReadonly(entityId, out var existing) && existing.Tier == ProcessingTierLevel.Local)
        {
            return false;
        }

        Write(tiers, entityId, ProcessingTierLevel.Local);
        return true;
    }

    /// <summary>
    /// Pins entityId to Local permanently, raising TierChanged if its tier changed. The only public
    /// pin: a silent pin on an entity already in tiered pools would leave them holding it at Beyond,
    /// the same trap CreateEntityAt exists to avoid, and one event is free by comparison.
    /// </summary>
    public void PinLocalAndNotify(int entityId)
    {
        if (PinLocal(entityId))
        {
            _events!.RaiseTierChanged(entityId, ProcessingTierLevel.Local);
        }
    }

    /// <summary>
    /// Creates a new entity and gives it its tier as its very first component, computed for the
    /// position it is about to be placed at -- before its blueprint adds anything else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the tier-first path, and the reason it creates the entity itself rather than taking
    /// an id is that the silent write is only correct for an entity no tiered consumer holds yet.
    /// Each consumer reads the tier in its own OnMemberAdded as the blueprint adds components, so a
    /// tier already in place is picked up with no TierChanged event and no bucket migration. On an
    /// entity that is *already* in tiered pools the same silent write would be a trap: every
    /// consumer would keep holding it at the fail-open Beyond default, and EnsureTiered could not
    /// rescue it -- it compares against the stored tier, which this would already have written, and
    /// so would see nothing to change. Owning creation makes that misuse unrepresentable instead of
    /// merely documented: a freshly created entity cannot be in any pool.
    /// </para>
    /// <para>
    /// Before a reference position exists this simply creates the entity untiered; EnsureTiered
    /// (via World.EntityPlaced) tiers it with an event once placed.
    /// </para>
    /// </remarks>
    /// <returns>The new entity's id.</returns>
    public int CreateEntityAt(Engine.ECS.Entities.EntityManager entityManager, Vector3Int plannedPosition)
    {
        ArgumentNullException.ThrowIfNull(entityManager);
        var tiers = RequireWired();

        var entityId = entityManager.CreateEntity();
        if (ReferencePosition is { } reference)
        {
            Write(tiers, entityId, ComputeTier(plannedPosition, reference, previousTier: null));
        }

        return entityId;
    }

    /// <summary>
    /// Makes entityId's tier correct for the position it was just placed at, raising TierChanged if
    /// it was missing or different. The catch-all driven by World.EntityPlaced, which supplies the
    /// position itself rather than leaving this to re-read a TransformComponent the placing caller
    /// may have passed as a local copy.
    /// </summary>
    public void EnsureTiered(int entityId, Vector3Int placedPosition) => RetierAt(entityId, placedPosition);

    /// <summary>
    /// Recomputes entityId's tier from its current TransformComponent position against the
    /// reference, writing it and raising TierChanged only if it changed. No-op for entities
    /// without a TransformComponent -- see RetierAt for the rest.
    /// </summary>
    public void Retier(int entityId)
    {
        if (_transforms is not null && _transforms.TryGetReadonly(entityId, out var transform))
        {
            RetierAt(entityId, transform.Position);
        }
    }

    /// <summary>
    /// The shared core of Retier and EnsureTiered. No-op for pinned entities, before a reference
    /// position exists, and before the resolver is wired.
    /// </summary>
    /// <remarks>
    /// Tolerates being unwired rather than throwing like CreateEntityAt does, because EnsureTiered
    /// is hooked into World.EntityPlaced and so runs on every placement in the game -- if a mod ever
    /// replaced ProcessingTierModule without wiring this, a throw here would take down population.
    /// CreateEntityAt still throws: calling it is an explicit request for a tier, not a side effect.
    /// </remarks>
    private void RetierAt(int entityId, Vector3Int position)
    {
        if (_tiers is not { } tiers || ReferencePosition is not { } reference || _pinnedLocal.Contains(entityId))
        {
            return;
        }

        var hasExisting = tiers.TryGetReadonly(entityId, out var existing);
        var tier = ComputeTier(position, reference, hasExisting ? existing.Tier : (ProcessingTierLevel?)null);

        if (hasExisting && existing.Tier == tier)
        {
            return;
        }

        Write(tiers, entityId, tier);

        // An entity with no tier yet is held by every consumer at the fail-open Beyond default (see
        // ProcessingTierWiring), so landing on Beyond is not a change any of them needs to hear about.
        if (hasExisting || tier != ProcessingTierLevel.Beyond)
        {
            _events!.RaiseTierChanged(entityId, tier);
        }
    }

    /// <summary>
    /// Classifies a position relative to the reference. A different MapLayer (Z) is always Beyond --
    /// never visible to the player regardless of X/Y. Local uses hysteresis: entering requires
    /// Chebyshev distance &lt;= LocalRadiusTiles, leaving requires &gt; LocalExitRadiusTiles.
    /// Neighborhood and Borough are fixed absolute grid cells, not radii.
    /// </summary>
    public static ProcessingTierLevel ComputeTier(Vector3Int position, Vector3Int reference, ProcessingTierLevel? previousTier)
    {
        if (position.Z != reference.Z)
        {
            return ProcessingTierLevel.Beyond;
        }

        var distance = ChebyshevDistance(position, reference);
        var localRadius = previousTier == ProcessingTierLevel.Local ? LocalExitRadiusTiles : LocalRadiusTiles;

        if (distance <= localRadius)
        {
            return ProcessingTierLevel.Local;
        }

        if (SameCell(position, reference, NeighborhoodSizeTiles))
        {
            return ProcessingTierLevel.Neighborhood;
        }

        return SameCell(position, reference, BoroughSizeTiles) ? ProcessingTierLevel.Borough : ProcessingTierLevel.Beyond;
    }

    /// <summary>Chebyshev (X/Y) distance -- Z is handled separately by ComputeTier.</summary>
    public static int ChebyshevDistance(Vector3Int a, Vector3Int b) =>
        System.Math.Max(System.Math.Abs(a.X - b.X), System.Math.Abs(a.Y - b.Y));

    /// <summary>Map positions are always non-negative, so plain integer division floors correctly.</summary>
    public static bool SameCell(Vector3Int a, Vector3Int b, int cellSize) =>
        a.X / cellSize == b.X / cellSize && a.Y / cellSize == b.Y / cellSize;

    private static void Write(DirectComponentPool<ProcessingTierComponent> tiers, int entityId, ProcessingTierLevel tier)
    {
        if (tiers.Has(entityId))
        {
            tiers.TrySet(entityId, new ProcessingTierComponent(tier));
        }
        else
        {
            tiers.Add(entityId, new ProcessingTierComponent(tier));
        }
    }

    private DirectComponentPool<ProcessingTierComponent> RequireWired() =>
        _tiers ?? throw new InvalidOperationException($"{nameof(ProcessingTierResolver)} used before {nameof(Wire)} -- ProcessingTierModule.RegisterSystems wires it.");
}
