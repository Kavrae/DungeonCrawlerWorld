using Engine.ECS.Components;
using Game.World;

namespace Game.Modules.StatusEffects;

/// <summary>The timer of a status effect whose stacks are its strength and whose duration is counted apart from them, in its own ticks (Poison's shape).</summary>
/// <remarks>One per entity, in a Packed pool, driven by a PackedTimerWheel. TimedStatusEffectStacks holds the rules every such effect shares.</remarks>
public interface ITimedStatusEffectTimer<TSelf> : IScheduledTimer, IStatusEffectStackCount where TSelf : struct, ITimedStatusEffectTimer<TSelf>
{
    new byte StackCount { get; set; }

    /// <summary>Ticks left, counting the one at NextTickFrame. Not a frame count.</summary>
    ushort RemainingDurationTicks { get; set; }

    /// <summary>What first applied the effect, kept for its whole run.</summary>
    ActionSource Source { get; }

    static abstract TSelf Create(uint nextTickFrame, byte stackCount, ushort remainingDurationTicks, ActionSource source);
}
