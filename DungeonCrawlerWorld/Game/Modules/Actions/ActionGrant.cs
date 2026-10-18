namespace Game.Modules.Actions;

/// <summary>An action a blueprint definition gives every entity built from it, with the per-definition override it uses.</summary>
/// <remarks>
/// Shared, not stored per entity: every Goblin knows the same three actions with the same overrides,
/// which as one ActionInstanceComponent each cost 34 MB for 13 distinct values at the entity counts
/// GameLoop.InitialEntityCapacity is sized for. An entity's actions are these plus whatever was
/// granted to it alone (a wand, a learned scroll) -- see EntityActions, which reads both as one set.
///
/// An override is either a fixed definition (Override) or derived from the catalog's own (Derived): a blueprint is
/// static and exists before any session, while the catalog's definitions are content loaded per session, so a race's
/// "the catalog's Quick Attack with this damage" is a recipe resolved through the session's ActionCatalog
/// (ResolveOverride) -- one instance per session, shared by every creature.
/// </remarks>
/// <param name="Override">A fixed definition to use instead of the catalog's, or null -- see ActionInstanceComponent.Override.</param>
/// <cleanupVersion>1</cleanupVersion>
public sealed record ActionGrant(Guid ActionId, ActionDefinition? Override = null)
{
    /// <summary>The key the derived override is kept under in ActionCatalog.GetOrCreateDerived; null for none.</summary>
    public string? DerivedOverrideKey { get; private init; }

    /// <summary>Makes the override from the catalog's definition of ActionId; null for none.</summary>
    public Func<ActionDefinition, ActionDefinition>? DeriveOverride { get; private init; }

    /// <summary>A grant whose override is derive applied to the session's own definition of actionId.</summary>
    /// <param name="overrideKey">Unique to this override across the build (e.g. "Goblin.QuickAttack").</param>
    public static ActionGrant Derived(Guid actionId, string overrideKey, Func<ActionDefinition, ActionDefinition> derive) =>
        new(actionId) { DerivedOverrideKey = overrideKey, DeriveOverride = derive };

    /// <summary>The definition to use instead of the catalog's own, or null to use the catalog's unchanged.</summary>
    public ActionDefinition? ResolveOverride(ActionCatalog actionCatalog) =>
        Override ?? (DeriveOverride is { } derive ? actionCatalog.GetOrCreateDerived(DerivedOverrideKey!, ActionId, derive) : null);
}
