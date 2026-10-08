using Engine.ECS.Entities;
using Game.World;

namespace Game.Modules.Auras.Components;

/// <summary>Marks an aura anchor: an entity standing on a tile only to radiate an aura placed there, and who placed it.</summary>
/// <remarks>
/// An anchor is an ordinary entity (AuraAnchor blueprint) carrying the placed aura's source. It ends
/// when its last source does (AuraAnchors), and is cancelled -- its source taken away, and the toggle
/// holding it switched off -- when its owner is destroyed or it is (its neighborhood unloading).
/// </remarks>
/// <param name="placedBy">What placed it -- the source its aura's effects are credited to.</param>
/// <param name="ownerKey">The entity that placed it, or EntityKey.None for an anchor nothing owns.</param>
/// <param name="heldGrantKey">The key of the owner's toggle holding it (EffectContext.HeldGrantKey), or AuraSourceComponent.NoHeldGrantKey for a timed one.</param>
public readonly struct AuraAnchorComponent(ActionSource placedBy, EntityKey ownerKey, uint heldGrantKey)
{
    public ActionSource PlacedBy { get; } = placedBy;

    public EntityKey OwnerKey { get; } = ownerKey;

    public uint HeldGrantKey { get; } = heldGrantKey;

    public override string ToString() => $"PlacedBy : {PlacedBy}\nOwnerKey : {OwnerKey}\nHeldGrantKey : {HeldGrantKey}";
}
