using Engine.ECS.Entities;
using Engine.ECS.Relationships;

namespace Game.Modules.Auras.Components;

/// <summary>Links an aura anchor to the entity that placed it, and to the toggle of that entity's holding it.</summary>
/// <remarks>
/// A relationship (AurasModule registers it with DestroySources): an owner destroyed takes its anchors with it.
/// An anchor nothing owns has no link. AuraAnchors finds an owner's anchors through the relationship, and an
/// anchor destroyed while a toggle holds it switches that toggle off (AurasModule.WireAnchors).
/// </remarks>
/// <param name="TargetKey">The entity that placed the anchor.</param>
/// <param name="HeldGrantKey">The key of the owner's toggle holding it (EffectContext.HeldGrantKey), or AuraSourceComponent.NoHeldGrantKey for a timed one.</param>
public readonly record struct AuraAnchorOwnerLink(EntityKey TargetKey, uint HeldGrantKey) : IRelationshipLink
{
    public override string ToString() => $"Owner : {TargetKey}\nHeldGrantKey : {HeldGrantKey}";
}
