using Game.Modules.Actions;

namespace Game.Modules.Inventory.Components;

/// <summary>
/// Written by Presentation when the player confirms a targeted (or double-tap self) item
/// activation -- mirrors PendingActionActivationComponent exactly: Presentation only ever queues
/// this request, ItemActivationSystem is the only thing that actually applies gameplay
/// effects. Consumed (removed) the same frame ItemActivationSystem processes it, whether or
/// not the activation actually goes through -- a one-shot request, not a standing intent to retry.
/// References the exact stack being activated by StackInstanceId, not ItemDefinitionId -- the
/// same per-slot item divergence reasoning ItemHotkeyBindingComponent's own doc comment gives.
/// </summary>
/// <param name="selection">What the caster aimed at; TargetResolution turns it into tiles.</param>
/// <param name="activatedFromSlot">The hotkey slot the activation came from, or null when it came from anywhere else (the inventory, an NPC). When the activated unit changes stack, this is the slot that follows it.</param>
public struct PendingItemActivationComponent(uint stackInstanceId, TargetSelection selection, HotkeySlot? activatedFromSlot = null)
{
    public uint StackInstanceId { get; set; } = stackInstanceId;
    public TargetSelection Selection { get; set; } = selection;

    /// <summary>The hotkey slot the activation came from, or null.</summary>
    public HotkeySlot? ActivatedFromSlot { get; set; } = activatedFromSlot;

    public override readonly string ToString() => $"StackInstanceId : {StackInstanceId}\nSelection : {Selection}\nActivatedFromSlot : {ActivatedFromSlot}";
}
