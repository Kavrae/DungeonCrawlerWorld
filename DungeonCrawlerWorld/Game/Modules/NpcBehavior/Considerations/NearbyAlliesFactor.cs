using Engine.Decisions;

namespace Game.Modules.NpcBehavior.Considerations;

/// <summary>Scales a behavior by the allies around the creature: 1 + PerAlly x n, or its inverse.</summary>
/// <remarks>
/// n counts allies within Radius of its footprint on its layer (NpcDecisionContext.AlliesWithin: of its race, alive,
/// simulated, itself excluded), capped at MaxAllies. Not inverse, more allies make the behavior likelier (a pack's
/// courage); inverse, less likely (its fear of running alone fades in company). Asked only once the behavior has weight.
/// </remarks>
public sealed record NearbyAlliesFactor(byte Radius, float PerAlly, byte MaxAllies, bool Inverse) : IBehaviorConsideration<NpcDecisionContext>
{
    public float Evaluate(ref NpcDecisionContext context)
    {
        var factor = 1f + PerAlly * System.Math.Min(context.AlliesWithin(Radius), MaxAllies);
        return Inverse ? 1f / factor : factor;
    }
}
