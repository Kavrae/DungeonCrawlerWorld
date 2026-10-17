namespace Game.Modules.Actions;

/// <summary>An action a blueprint definition gives every entity built from it, with the per-definition override it uses.</summary>
/// <remarks>
/// Shared, not stored per entity: every Goblin knows the same three actions with the same overrides,
/// which as one ActionInstanceComponent each cost 34 MB for 13 distinct values at the entity counts
/// GameLoop.InitialEntityCapacity is sized for. An entity's actions are these plus whatever was
/// granted to it alone (a wand, a learned scroll) -- see EntityActions, which reads both as one set.
/// </remarks>
/// <param name="Override">The definition to use instead of the catalog's, or null to use the catalog's unchanged -- see ActionInstanceComponent.Override.</param>
/// <cleanupVersion>1</cleanupVersion>
public sealed record ActionGrant(Guid ActionId, ActionDefinition? Override = null);
