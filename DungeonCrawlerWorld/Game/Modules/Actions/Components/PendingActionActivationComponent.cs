namespace Game.Modules.Actions.Components;

/// <summary>
/// Written by Presentation when the player confirms a targeted action activation (left-click
/// on a valid tile, or a no-target activation) -- Presentation only ever queues this request;
/// ActionActivationSystem is the only thing that actually applies gameplay effects, mirroring
/// how PlayerCommands.Flush queues a move for MovementSystem to apply rather than
/// mutating gameplay state directly. Consumed (removed) the same frame
/// ActionActivationSystem processes it, whether or not the activation actually goes through
/// (e.g. blocked by the shared ActionLock or an on-cooldown FreeCast) -- a one-shot request,
/// not a standing intent to retry.
/// </summary>
/// <param name="selection">What the caster aimed at; TargetResolution turns it into tiles.</param>
public struct PendingActionActivationComponent(Guid actionId, TargetSelection selection)
{
    public Guid ActionId { get; set; } = actionId;
    public TargetSelection Selection { get; set; } = selection;

    public override readonly string ToString() => $"ActionId : {ActionId}\nSelection : {Selection}";
}
