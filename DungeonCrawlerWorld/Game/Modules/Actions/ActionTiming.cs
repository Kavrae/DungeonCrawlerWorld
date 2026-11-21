using Engine.ECS.Components.Stores;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;
using Game.Modules.StatModifiers.Components;

namespace Game.Modules.Actions;

/// <summary>
/// Shared between every IActionActivator kind (SpellActivator, DirectAction, PotionActivator).
/// CooldownFrames is orthogonal to Category: any category may carry its own individual cooldown
/// on top of (Immediate/Delayed) or instead of (FreeCast) the shared ActionLock. A PotionActivator
/// always constructs Immediate with CooldownFrames: null today -- reusing this shared shape rather
/// than inventing its own flat ActionLockFrames field, the same way TargetingSpec is already
/// reused verbatim across both domains.
///
/// ActionLockFrames defaults to null, meaning "use the acting entity's own standard lock"
/// (StandardActionLockFrames: its Dexterity and ActionLockFrames modifiers) -- omit it entirely
/// unless this action/item genuinely needs a different
/// lock duration regardless of who casts it (see HotkeyExpansionPotion for the one current
/// override).
///
/// CooldownMatchesActionLock makes the cooldown as long as the lock this timing would set for the
/// user (ActionLockFrames, else its standard lock), resolved when each use starts it, so it follows
/// Dexterity and ActionLockFrames modifiers as the lock does. It replaces CooldownFrames; the reader
/// refuses a record stating both. Dodge uses it.
///
/// ReleasesActionLock makes a successful activation cancel the acting entity's windup, if any, and
/// release its shared ActionLock, whatever set it -- see ActionActivationSystem. Dodge uses it so its
/// step is taken at once rather than after the current lock.
/// </summary>
public sealed record ActionTiming(ActionTimingCategory Category, ushort? ActionLockFrames = null, ushort? CooldownFrames = null, bool ReleasesActionLock = false, bool CooldownMatchesActionLock = false)
{
    /// <summary>The cooldown a use by entityId starts now: its action lock's length when CooldownMatchesActionLock, else CooldownFrames; null when it has none.</summary>
    public ushort? ResolveCooldownFrames(PackedComponentPool<AbilityScoresComponent> abilityScores, MultiComponentPool<StatModifierComponent> statModifiers, int entityId) =>
        CooldownMatchesActionLock
            ? StandardActionLockFrames.ResolveForEntity(abilityScores, statModifiers, entityId, ActionLockFrames)
            : CooldownFrames;
}
