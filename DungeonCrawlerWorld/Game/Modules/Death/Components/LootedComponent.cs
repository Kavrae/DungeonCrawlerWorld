namespace Game.Modules.Death.Components;

/// <summary>
/// Marks an entity (a corpse, or a container such as a treasure chest) as having had its loot
/// opened at least once, by anyone -- the player's loot window opening, or an NPC with the rights to
/// it starting to loot it (NpcCorpseLooting) -- regardless of whether anything is actually taken.
/// Distinct from "has loot" (stacks or currency): drives whether MapWindow draws the LootBag-Red
/// badge grey, and keeps NPCs from returning to a corpse already opened.
/// </summary>
public readonly record struct LootedComponent
{
    public override readonly string ToString() => nameof(LootedComponent);
}
