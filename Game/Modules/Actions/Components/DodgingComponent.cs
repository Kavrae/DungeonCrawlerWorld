namespace Game.Modules.Actions.Components;

/// <summary>
/// Written by DodgeAction's own effect (DodgeActivation) while the entity is immune to any
/// Dodgeable-tagged action's effects -- see ActionEffectResolver.Apply's own skip check. Ticked
/// down and removed by DodgeExpirySystem once FramesRemaining reaches 0. At most one per entity
/// (a fresh Dodge simply refreshes the window via Merge's replace policy -- see ActionsModule's
/// own registration).
/// </summary>
public struct DodgingComponent(ushort framesRemaining)
{
    public ushort FramesRemaining { get; set; } = framesRemaining;

    public override readonly string ToString() => $"FramesRemaining : {FramesRemaining}";
}
