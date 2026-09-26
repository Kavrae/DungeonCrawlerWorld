namespace Game.Modules.Actions.Components;

/// <summary>One action granted to one entity by itself -- a wand, a learned scroll, an admin grant.</summary>
/// <remarks>
/// Sparse: the actions a creature has from its race or class are not here (see ActionGrant, held once
/// per definition), so an entity holds one of these only for an action granted to it alone.
/// EntityActions reads both as one set, and one of these wins over a definition's grant of the same
/// action.
///
/// Override lives here, per instance, rather than on the shared ActionDefinition it points to
/// via ActionId -- multiple entities can share one catalog ActionDefinition while each hitting for a
/// different amount or diverging in any other way (targeting, ManaCost, Tags). Mirrors
/// InventoryItemStackComponent.Override's shape exactly: a full, nullable clone of the catalog
/// definition built via `with`, resolved by EntityActions.TryGetEffectiveAction (Override if set,
/// else the plain catalog lookup by ActionId) -- null means "no override," so the granted action's
/// own catalog Effects apply unmodified (e.g. PlayerKit's Punch grant, which rolls
/// DirectDamage's own MinFlatDamage..MaxFlatDamage range rather than a fixed number).
///
/// Cooldowns are not here either: they live in ActionCooldownComponent, written only for an action
/// the entity actually used, since a definition-granted action has no per-entity component to write
/// a deadline into.
/// </remarks>
public struct ActionInstanceComponent(Guid actionId, ActionDefinition? overrideDefinition)
{
    public Guid ActionId { get; } = actionId;

    public ActionDefinition? Override { get; set; } = overrideDefinition;

    public override readonly string ToString() => $"ActionId : {ActionId}\nOverride : {(Override is null ? "none" : Override.Name)}";
}
