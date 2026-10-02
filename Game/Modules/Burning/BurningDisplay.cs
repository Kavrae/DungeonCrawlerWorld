using Engine.ECS.Components.Stores;
using Game.Modules.Burning.Components;
using Game.Modules.StatusEffects;

namespace Game.Modules.Burning;

/// <summary>Burning as shown for the entity as a whole: its entity-scoped burn and every burning body part, reported as the highest of them.</summary>
/// <remarks>
/// A burn is held either on the entity or on single body parts (see BurningApplier), and an entity
/// standing in lava holds only the second. Reading the entity-scoped timer alone would show such an
/// entity as not burning at all, so the stack count is the highest any one of them holds and the
/// remaining duration is the longest, never a sum: the parts burn side by side, not one after another.
/// Each part's own burn is still shown on that part's row (HealthWindow).
/// </remarks>
public sealed class BurningDisplay(
    PackedComponentPool<BurningTimerComponent> entityTimers,
    MultiComponentPool<BodyPartBurningTimerComponent> bodyPartTimers) : IStatusEffectDisplay
{
    public StatusEffectType EffectType => StatusEffectType.Burning;

    public string Glyph => BurningEffects.Glyph;

    public int? GetRemainingDurationFrames(int entityId, long now)
    {
        int? longestRemainingFrames = entityTimers.TryGetReadonly(entityId, out var entityTimer)
            ? BurningModule.RemainingFrames(entityTimer.NextTickFrame, entityTimer.StackCount, now)
            : null;

        for (var denseIndex = bodyPartTimers.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = bodyPartTimers.GetNextDenseIndex(denseIndex))
        {
            ref readonly var partTimer = ref bodyPartTimers.GetReadonlyByDenseIndex(denseIndex);
            var remainingFrames = BurningModule.RemainingFrames(partTimer.NextTickFrame, partTimer.StackCount, now);
            if (longestRemainingFrames is not { } longest || remainingFrames > longest)
            {
                longestRemainingFrames = remainingFrames;
            }
        }

        return longestRemainingFrames;
    }

    public int GetStackCount(int entityId)
    {
        var highestStackCount = entityTimers.TryGetReadonly(entityId, out var entityTimer) ? entityTimer.StackCount : 0;

        for (var denseIndex = bodyPartTimers.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = bodyPartTimers.GetNextDenseIndex(denseIndex))
        {
            highestStackCount = Math.Max(highestStackCount, bodyPartTimers.GetReadonlyByDenseIndex(denseIndex).StackCount);
        }

        return highestStackCount;
    }
}
