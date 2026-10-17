namespace Game.Modules.Containers.Components;

/// <summary>
/// Marks an entity as a container (e.g. a treasure chest): lootable via the same "Loot" context
/// menu option/SecondaryInventoryWindow a corpse uses, but -- unlike a corpse -- lootable while
/// still alive (see MapWindow.AddEntityGroup's own doc comment). If the entity dies,
/// ContainerDestructionSystem clears its inventory and renames it "Destroyed" instead of leaving
/// its items and name intact the way a creature's corpse does.
/// </summary>
public readonly record struct ContainerComponent
{
    public override readonly string ToString() => nameof(ContainerComponent);
}
