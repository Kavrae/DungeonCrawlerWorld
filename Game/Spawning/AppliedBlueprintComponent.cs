namespace Game.Spawning;

/// <summary>One blueprint part built onto an entity after it spawned (EntityFactory.Apply).</summary>
/// <remarks>
/// A Multi pool entry per applied part, beside the spawn record: together they are everything the
/// entity was built from, so a part is never built onto it twice, and an applied part's definition
/// data -- its actions, its class name, its name suffix -- is read through here the way a spawned
/// part's is read through the spawn record. Sparse: an entity nothing was applied to holds none.
/// Order is kept explicitly because the pool hands entries back in whatever order its chain holds.
/// </remarks>
/// <param name="BlueprintId">The applied part, as a BlueprintRegistry id.</param>
/// <param name="Order">Its place among the entity's applied parts, counting from 0: a later one wins where parts disagree.</param>
/// <cleanupVersion>1</cleanupVersion>
public readonly record struct AppliedBlueprintComponent(ushort BlueprintId, ushort Order);
