namespace Game.Modules.Movement.Components;

/// <summary>The movement-target selection strategy used by MovementSystem.</summary>
public enum MovementMode : byte
{
    Random,
    SeekTarget,
}
