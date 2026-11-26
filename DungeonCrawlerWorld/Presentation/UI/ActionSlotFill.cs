using Game.Modules.Actions;
using Game.Modules.Core.Components;
using Game.Views;

namespace Presentation.UI;

/// <summary>How much of an action's icon the cooldown wedge covers: a hotbar slot holding it and its Actions window cell draw the same.</summary>
public static class ActionSlotFill
{
    /// <summary>
    /// The entity's own cooldown on action while it counts down; otherwise the shared action lock's, unless the action is
    /// FreeCast -- 0 when neither holds it.
    /// </summary>
    /// <remarks>
    /// The shared lock is set by whichever Immediate or Delayed action used it, so taking the larger of the two would
    /// spike this icon to an unrelated windup's fraction while its own, longer cooldown runs; its own cooldown is the more
    /// specific signal whenever it exists, and the lock is shown only for an action with none.
    /// </remarks>
    /// <param name="actions">The entity's actions, for its cooldowns; null shows no cooldown, only the lock.</param>
    public static float Of(ActionStateView actionStateView, EntityActions? actions, int entityId, ActionDefinition action, long now)
    {
        if (actions is not null && actionStateView.CooldownFramesFor(entityId, action.Activator.Timing) is { } cooldownFrames && cooldownFrames > 0)
        {
            var cooldownFraction = System.Math.Min(1f, (float)actions.CooldownFramesRemaining(entityId, action.Id, now) / cooldownFrames);
            if (cooldownFraction > 0f)
            {
                return cooldownFraction;
            }
        }

        if (action.Activator.Timing.Category != ActionTimingCategory.FreeCast &&
            actionStateView.TryGetActionLock(entityId, out var actionLock) &&
            actionLock.CurrentLockTotalFrames > 0)
        {
            return (float)ActionLockGate.FramesRemaining(actionLock, now) / actionLock.CurrentLockTotalFrames;
        }

        return 0f;
    }
}
