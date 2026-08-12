namespace Game.Modules.Actions;

/// <summary>
/// Shared between every IActionActivator kind (SpellActivator, DirectAction, PotionActivator).
/// CooldownFrames is orthogonal to Category: any category may carry its own individual cooldown
/// on top of (Immediate/Delayed) or instead of (FreeCast) the shared ActionLock. A PotionActivator
/// always constructs Immediate with CooldownFrames: null today -- reusing this shared shape rather
/// than inventing its own flat ActionLockFrames field, the same way TargetingSpec is already
/// reused verbatim across both domains.
/// </summary>
public sealed record ActionTiming(ActionTimingCategory Category, short ActionLockFrames, short? CooldownFrames);
