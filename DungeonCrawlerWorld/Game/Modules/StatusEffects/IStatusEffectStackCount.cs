namespace Game.Modules.StatusEffects;

/// <summary>Represents a component that tracks the stack count of a status effect.</summary>
/// <remarks>Maximum of 255 stacks per status effect, though each effect can define a lower limit.</remarks>
/// <cleanupVersion>1</cleanupVersion>
public interface IStatusEffectStackCount
{
    byte StackCount { get; }
}
