using Engine.Math;

namespace Game.Modules.Inventory.Components;

/// <summary>
/// Written by Presentation when the player confirms a targeted (or double-tap self) consumable
/// activation -- mirrors PendingAbilityActivationComponent exactly: Presentation only ever queues
/// this request, ConsumableActivationSystem is the only thing that actually applies gameplay
/// effects. Consumed (removed) the same frame ConsumableActivationSystem processes it, whether or
/// not the activation actually goes through -- a one-shot request, not a standing intent to retry.
/// </summary>
public struct PendingConsumableActivationComponent(Guid itemDefinitionId, Vector3Int[] targetTiles)
{
    public Guid ItemDefinitionId { get; set; } = itemDefinitionId;
    public Vector3Int[] TargetTiles { get; set; } = targetTiles;
}
