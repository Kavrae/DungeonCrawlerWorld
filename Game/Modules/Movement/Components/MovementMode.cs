namespace Game.Modules.Movement.Components;

/// <summary>The movement-target selection strategy used by MovementSystem.</summary>
/// <cleanupVersion>1</cleanupVersion>
public enum MovementMode : byte
{
    Random,
    SeekTarget,
    PlayerControlled
}