namespace Game.Modules.Actions;

/// <summary>
/// Shared between every IActionActivator kind (SpellActivator, DirectAction, PotionActivator).
/// CooldownFrames is orthogonal to Category: any category may carry its own individual cooldown
/// on top of (Immediate/Delayed) or instead of (FreeCast) the shared ActionLock. A PotionActivator
/// always constructs Immediate with CooldownFrames: null today -- reusing this shared shape rather
/// than inventing its own flat ActionLockFrames field, the same way TargetingSpec is already
/// reused verbatim across both domains.
///
/// ActionLockFrames defaults to null, meaning "use the acting entity's own ActionLockComponent.
/// StandardLockFrames" -- omit it entirely unless this action/item genuinely needs a different
/// lock duration regardless of who casts it (see HotkeyExpansionPotion for the one current
/// override).
/// </summary>
public sealed record ActionTiming(ActionTimingCategory Category, ushort? ActionLockFrames = null, ushort? CooldownFrames = null);
