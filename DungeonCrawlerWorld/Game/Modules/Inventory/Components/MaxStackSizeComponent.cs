namespace Game.Modules.Inventory.Components;

/// <summary>
/// An entity's own override of InventoryActions.DefaultMaxStackSize (999) -- every item stack that
/// entity holds shares this one cap, uniformly across every ItemDefinition (items no longer carry
/// their own MaxStackSize -- splitting a stack that would otherwise overflow it is a separate,
/// not-yet-built TODO item). Absent means the entity uses the default; only ever set explicitly
/// when it differs -- today only the player, raised to 1000 by ObsessiveCollectorAchievement's
/// reward (see Game/Modules/Achievements/Definitions/ObsessiveCollectorAchievement.cs). Lives in
/// Inventory, not Achievements, so InventoryActions can read it without depending on the
/// Achievements module -- Achievements writes it via IAchievementDefinition.ApplyReward, Inventory
/// only ever reads it.
/// </summary>
public readonly struct MaxStackSizeComponent(ushort value)
{
    public ushort Value { get; } = value;

    public override readonly string ToString() => $"Value : {Value}";
}
