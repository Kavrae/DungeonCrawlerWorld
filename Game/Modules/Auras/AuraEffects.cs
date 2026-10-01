using Engine.Utilities;

namespace Game.Modules.Auras;

/// <summary>The rules every aura shares.</summary>
public static class AuraEffects
{
    /// <summary>How often an aura applies its effect to an entity that stays in range.</summary>
    public const int TickIntervalFrames = GameTiming.FramesPerSecond;
}
