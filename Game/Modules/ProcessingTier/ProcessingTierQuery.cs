using Engine.ECS.Components.Stores;
using Game.Modules.ProcessingTier.Components;

namespace Game.Modules.ProcessingTier;

/// <summary>Reads an entity's processing tier, for the rules that depend on it rather than on being visited at it -- the seam.</summary>
/// <remarks>
/// <para>
/// Nothing may target across the simulated/frozen boundary: a frozen entity resolves no effects,
/// takes no turn and cannot answer, so letting something hit it would apply damage nobody will ever
/// see the consequences of. The plan's stricter "the player interacts with Local only" rule needs no
/// code: Local is a radius of 80 on the player's own layer, every action's range is a fraction of
/// that, and target tiles resolve on the player's layer -- so a target the player can reach is Local
/// by construction. A longer-ranged action would be the thing that changes that.
/// </para>
/// <para>
/// An entity with no tier counts as simulated, matching the SimulationScope policy this backs (see
/// GameBuildPass.WireSimulationScope): the entities that go untiered are the ones never placed on
/// the map, and refusing to resolve effects on them would break every test double and every
/// inventory-only entity.
/// </para>
/// </remarks>
public sealed class ProcessingTierQuery(DirectComponentPool<ProcessingTierComponent> tiers)
{

    /// <summary>Whether entityId is simulated at all -- Local or Neighborhood today. True for an untiered entity.</summary>
    public bool IsSimulated(int entityId) =>
        !tiers.TryGetReadonly(entityId, out var tier) || IsSimulatedTier(tier.Tier);

    /// <summary>Whether tier is simulated at all, by SystemManager and by every timer wheel.</summary>
    public static bool IsSimulatedTier(ProcessingTierLevel tier) => (int)tier < ProcessingTierDivisors.SimulatedTierCount;
}
