using Engine.ECS.Components;
using Game.Modules.StatusEffects;
using Game.World;

namespace Game.Modules.Burning.Components;

/// <summary>
/// Present on an entity only while it currently has at least one Burning stack -- added on the
/// 0-to-1 stack transition, removed once stacks reach 0 (see BurningSystem). Countdown to the
/// next damage tick; gaining an additional stack while already burning must not reset it.
/// </summary>
public struct BurningTimerComponent(ushort framesUntilNextTick, byte stackCount, StatusEffectSource source) : ITickCountdown, IStatusEffectStackCount
{
    public ushort FramesUntilNextTick { get; set; } = framesUntilNextTick;

    /// <summary>
    /// Cached running total of this entity's Burning stacks, kept in sync by
    /// BurningEffects.ApplyStack (increment) and BurningSystem.Tick (decrement)
    /// </summary>
    public byte StackCount { get; set; } = stackCount;

    /// <summary>Set once on the 0-to-1 transition (BurningEffects.ApplyStack), never overwritten by a later top-off -- first applier is attributed for the whole burn, mirroring PoisonTimerComponent's own Source field.</summary>
    public StatusEffectSource Source { get; set; } = source;

    public override readonly string ToString() => $"FramesUntilNextTick : {FramesUntilNextTick}\nStackCount : {StackCount}\nSource : {Source}";
}
