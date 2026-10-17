using Game.World;

namespace Game.Modules.Auras.Components;

/// <summary>Marks an aura anchor: an entity standing on a tile only to radiate an aura placed there, and what placed it.</summary>
/// <remarks>
/// An anchor is an ordinary entity (AuraAnchor blueprint) carrying the placed aura's source. It ends
/// when its last source does (AuraAnchors). The entity that placed it, if any, is its AuraAnchorOwnerLink:
/// destroying the owner destroys the anchor, and destroying an anchor a toggle holds switches the toggle off.
/// </remarks>
/// <param name="placedBy">What placed it -- the source its aura's effects are credited to.</param>
public readonly struct AuraAnchorComponent(ActionSource placedBy)
{
    public ActionSource PlacedBy { get; } = placedBy;

    public override string ToString() => $"PlacedBy : {PlacedBy}";
}
