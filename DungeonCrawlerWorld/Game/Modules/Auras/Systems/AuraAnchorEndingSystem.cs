using Engine.ECS.Systems;

namespace Game.Modules.Auras.Systems;

/// <summary>Destroys the aura anchors whose last source has gone since its last update.</summary>
/// <remarks>A plain system with no population: the anchors are queued by AuraAnchors as their sources are removed, often inside another system's timer callback, where destroying an entity isn't safe. An emptied anchor lasts until this runs, radiating nothing.</remarks>
public sealed class AuraAnchorEndingSystem(AuraAnchors anchors) : ISystem
{
    public byte StripeCount => 1;

    public void Update(EngineTime time, byte stripeIndex) => anchors.EndEmptiedAnchors();
}
