using Engine.Math;

namespace Game.Modules.Inventory.Components;

/// <summary>
/// Written by Presentation when the player confirms a targeted (or double-tap self) consumable
/// activation -- mirrors PendingActionActivationComponent exactly: Presentation only ever queues
/// this request, ConsumableActivationSystem is the only thing that actually applies gameplay
/// effects. Consumed (removed) the same frame ConsumableActivationSystem processes it, whether or
/// not the activation actually goes through -- a one-shot request, not a standing intent to retry.
/// References the exact stack being activated by StackInstanceId, not ItemDefinitionId -- the
/// same per-slot item divergence reasoning ItemHotkeyBindingComponent's own doc comment gives.
/// </summary>
public struct PendingConsumableActivationComponent(Guid stackInstanceId, Vector3Int[] targetTiles)
{
    public Guid StackInstanceId { get; set; } = stackInstanceId;
    public Vector3Int[] TargetTiles { get; set; } = targetTiles;

    public override readonly string ToString() => $"StackInstanceId : {StackInstanceId}\nTargetTiles : [{string.Join(", ", TargetTiles)}]";
}
