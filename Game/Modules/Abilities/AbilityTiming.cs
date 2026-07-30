namespace Game.Modules.Abilities;

/// <summary>
/// CooldownFrames is independent of Category -- any timing category may carry its own
/// per-ability cooldown on top of (Immediate/Delayed) or instead of (FreeCast) the shared
/// ActionLock; FreeCast is simply where a cooldown is most commonly needed, since a FreeCast
/// ability with none would otherwise be usable every single frame.
/// </summary>
public sealed record AbilityTiming(ActionTimingCategory Category, short ActionLockFrames, short? CooldownFrames);
