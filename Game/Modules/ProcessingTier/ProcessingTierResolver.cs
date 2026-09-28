using Engine.ECS.Components.Stores;
using Engine.Math;
using Game.Modules.Core.Components;
using Game.Modules.ProcessingTier.Components;
using Game.World;

namespace Game.Modules.ProcessingTier;

/// <summary>
/// The single place an entity's ProcessingTierComponent is decided and written -- shared by the
/// spawn sequence (which assigns a tier before an entity joins any tiered pool) and by
/// ProcessingTierSystem (which moves it afterwards).
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
public sealed class ProcessingTierResolver(DirectComponentPool<ProcessingTierComponent> tiers, DirectComponentPool<TransformComponent> transforms, ProcessingTierEvents events)
{
    /// <summary>Chebyshev distance at or within which a non-Local entity is promoted to Local.</summary>
    public const int LocalRadiusTiles = 80;

    /// <summary>Hysteresis: once Local, an entity stays Local until it is beyond LocalRadiusTiles + this. Keeps an entity pacing along the boundary from flapping between tiers -- the "grace range" of spatial-hash area-of-interest schemes.</summary>
    public const int LocalExitBufferTiles = 16;

    /// <summary>Chebyshev distance beyond which a Local entity is demoted.</summary>
    public const int LocalExitRadiusTiles = LocalRadiusTiles + LocalExitBufferTiles;

    private readonly HashSet<int> _pinnedLocal = [];

    /// <summary>The position every non-pinned entity's tier is computed from. Null until the spawn sequence (or the first observation of the player) establishes one -- see this class's own remarks.</summary>
    public Vector3Int? ReferencePosition { get; private set; }

    /// <summary>Every tiered entity by neighborhood and MapLayer, kept current by every path in this class that computes a tier from a position.</summary>
    /// <remarks>Indexed regardless of pinning or whether a reference exists yet, so an entity created before the reference is still found by the first neighborhood walk that needs it.</remarks>
    public NeighborhoodMembershipIndex Membership { get; } = new();

    /// <summary>Neighborhoods whose entities a window shift changed the tier of, drained by ProcessingTierSystem under its per-frame budget -- see ProcessingTierTransitionQueue.</summary>
    /// <remarks>Kept here with the window rather than on the system, so anything waiting for a shift to settle can ask HasPending.</remarks>
    public ProcessingTierTransitionQueue Transitions { get; } = new();

    /// <summary>Forgets entityId: drops it from the membership index and unpins it, for an entity being destroyed.</summary>
    public void Forget(int entityId)
    {
        Membership.Remove(entityId);
        _pinnedLocal.Remove(entityId);
    }

    /// <summary>Sets the reference position. The spawn sequence calls this with the player's intended spawn position before population; ProcessingTierSystem keeps it current as the player moves.</summary>
    public void SetReferencePosition(Vector3Int position) => ReferencePosition = position;

    /// <summary>How far past the window centre's edge, in tiles, the player walks before the window moves to the player's neighborhood.</summary>
    public const int WindowShiftGraceTiles = 64;

    /// <summary>The neighborhood at the centre of the loaded window, which non-Local tiers are measured from; null when there is no window and the reference's own neighborhood is the centre.</summary>
    public (int CellX, int CellY)? WindowCenter { get; private set; }

    /// <summary>Raised by ShiftWindowTo with the previous and the new centre, after WindowCenter has changed.</summary>
    public event Action<(int CellX, int CellY), (int CellX, int CellY)>? WindowShifted;

    /// <summary>While true, ProcessingTierSystem starts no neighborhood's promotion into a simulated tier (the transition queue's thaw band); everything else keeps draining. Null: never held.</summary>
    /// <remarks>Set by the composition root to "the neighborhoods leaving the window are still unloading", so the creatures a promotion builds reuse the storage the unloaded ones free instead of growing it.</remarks>
    public Func<bool>? PromotionsHeld { get; set; }

    /// <summary>Starts a window centred on (cellX, cellY) without raising WindowShifted -- the spawn sequence, before population, so everything is born tiered against it.</summary>
    public void SetWindowCenter(int cellX, int cellY) => WindowCenter = (cellX, cellY);

    /// <summary>Moves the window centre and raises WindowShifted. Retiering what changed is ProcessingTierSystem's job.</summary>
    public void ShiftWindowTo(int cellX, int cellY)
    {
        var previous = WindowCenter ?? throw new InvalidOperationException("No window to shift -- SetWindowCenter starts one.");
        WindowCenter = (cellX, cellY);
        WindowShifted?.Invoke(previous, (cellX, cellY));
    }

    /// <summary>The window centre once the player is at player: unchanged until the player is WindowShiftGraceTiles or more beyond its edge, then the player's own neighborhood.</summary>
    /// <remarks>Hysteresis: a player pacing along a border never moves the window, and walking back moves it only once they are as far into the old centre.</remarks>
    public static (int CellX, int CellY) NextWindowCenter((int CellX, int CellY) center, Vector3Int player) =>
        Neighborhoods.DistanceToArea(player, center.CellX, center.CellY) >= WindowShiftGraceTiles
            ? (Neighborhoods.CellOf(player.X), Neighborhoods.CellOf(player.Y))
            : center;

    /// <summary>The neighborhood non-Local tiers are measured from for this reference: the window centre, or with no window the reference's own neighborhood.</summary>
    private (int CellX, int CellY) CenterFor(Vector3Int reference) =>
        WindowCenter ?? (Neighborhoods.CellOf(reference.X), Neighborhoods.CellOf(reference.Y));

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
            events.RaiseTierChanged(entityId, ProcessingTierLevel.Local);
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

        var entityId = entityManager.CreateEntity();
        Membership.Set(entityId, plannedPosition);

        if (ReferencePosition is { } reference)
        {
            var center = CenterFor(reference);
            Write(tiers, entityId, ComputeTier(plannedPosition, reference, center.CellX, center.CellY, previousTier: null));
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
        if (transforms.TryGetReadonly(entityId, out var transform))
        {
            RetierAt(entityId, transform.Position);
        }
    }

    /// <summary>
    /// The shared core of Retier and EnsureTiered. No-op for pinned entities and before a reference
    /// position exists.
    /// </summary>
    /// <exception cref="InvalidOperationException">The resolver hasn't been wired.</exception>
    private void RetierAt(int entityId, Vector3Int position)
    {
        Membership.Set(entityId, position);

        if (ReferencePosition is not { } reference || _pinnedLocal.Contains(entityId))
        {
            return;
        }

        var hasExisting = tiers.TryGetReadonly(entityId, out var existing);
        var center = CenterFor(reference);
        var tier = ComputeTier(position, reference, center.CellX, center.CellY, hasExisting ? existing.Tier : (ProcessingTierLevel?)null);

        if (hasExisting && existing.Tier == tier)
        {
            return;
        }

        Write(tiers, entityId, tier);

        // An entity with no tier yet is held by every consumer at the fail-open Beyond default (see
        // ProcessingTierWiring), so landing on Beyond is not a change any of them needs to hear about.
        if (hasExisting || tier != ProcessingTierLevel.Beyond)
        {
            events.RaiseTierChanged(entityId, tier);
        }
    }

    /// <summary>ComputeTier with no window: the reference's own neighborhood is the centre.</summary>
    public static ProcessingTierLevel ComputeTier(Vector3Int position, Vector3Int reference, ProcessingTierLevel? previousTier) =>
        ComputeTier(position, reference, Neighborhoods.CellOf(reference.X), Neighborhoods.CellOf(reference.Y), previousTier);

    /// <summary>
    /// Classifies a position relative to the reference and the window centre. Local is the reference's own MapLayer only,
    /// with hysteresis: entering requires Chebyshev distance &lt;= LocalRadiusTiles, leaving requires
    /// &gt; LocalExitRadiusTiles. Otherwise the tier is the position's neighborhood's: the centre
    /// neighborhood is Neighborhood, the 8 around it Borough, anything further Beyond -- on every
    /// MapLayer alike, since another layer is classified by its X/Y, just never as Local.
    /// </summary>
    public static ProcessingTierLevel ComputeTier(Vector3Int position, Vector3Int reference, int centerCellX, int centerCellY, ProcessingTierLevel? previousTier)
    {
        if (position.Z == reference.Z)
        {
            var localRadius = previousTier == ProcessingTierLevel.Local ? LocalExitRadiusTiles : LocalRadiusTiles;
            if (ChebyshevDistance(position, reference) <= localRadius)
            {
                return ProcessingTierLevel.Local;
            }
        }

        return NeighborhoodTier(System.Math.Max(System.Math.Abs(Neighborhoods.CellOf(position.X) - centerCellX), System.Math.Abs(Neighborhoods.CellOf(position.Y) - centerCellY)));
    }

    /// <summary>The non-Local tier of a neighborhood this many neighborhoods from the reference's.</summary>
    public static ProcessingTierLevel NeighborhoodTier(int neighborhoodDistance) => neighborhoodDistance switch
    {
        0 => ProcessingTierLevel.Neighborhood,
        1 => ProcessingTierLevel.Borough,
        _ => ProcessingTierLevel.Beyond,
    };

    /// <summary>Chebyshev (X/Y) distance -- Z is handled separately by ComputeTier.</summary>
    public static int ChebyshevDistance(Vector3Int a, Vector3Int b) =>
        System.Math.Max(System.Math.Abs(a.X - b.X), System.Math.Abs(a.Y - b.Y));

    /// <summary>Writes entityId's tier.</summary>
    private void Write(DirectComponentPool<ProcessingTierComponent> tiers, int entityId, ProcessingTierLevel tier)
    {
        var component = new ProcessingTierComponent(tier);
        if (tiers.Has(entityId))
        {
            tiers.TrySet(entityId, component);
        }
        else
        {
            tiers.Add(entityId, component);
        }
    }
}
