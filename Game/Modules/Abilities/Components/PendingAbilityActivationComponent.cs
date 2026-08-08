using Engine.Math;

namespace Game.Modules.Abilities.Components;

/// <summary>
/// Written by Presentation when the player confirms a targeted ability activation (left-click
/// on a valid tile, or a no-target activation) -- Presentation only ever queues this request;
/// AbilityActivationSystem is the only thing that actually applies gameplay effects, mirroring
/// how MapWindow.TryQueuePlayerMove queues a move for MovementSystem to apply rather than
/// mutating gameplay state directly. Consumed (removed) the same frame
/// AbilityActivationSystem processes it, whether or not the activation actually goes through
/// (e.g. blocked by the shared ActionLock or an on-cooldown FreeCast) -- a one-shot request,
/// not a standing intent to retry.
/// </summary>
public struct PendingAbilityActivationComponent(Guid abilityId, Vector3Int[] targetTiles)
{
    public Guid AbilityId { get; set; } = abilityId;
    public Vector3Int[] TargetTiles { get; set; } = targetTiles;

    public override readonly string ToString() => $"AbilityId : {AbilityId}\nTargetTiles : [{string.Join(", ", TargetTiles)}]";
}
