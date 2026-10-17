namespace Game.Modules.Actions.Components;

/// <summary>
/// Tracks how many times entityId has activated a scroll whose ScrollActivator.SpellId is
/// SpellId -- MultiComponentPool-backed since an entity can be mastering several different
/// scrolls at once (independent counters, one entry per SpellId). Written by
/// ScrollMasteryEffects.RecordUsage; never removed once created (a mastered scroll's counter
/// simply stops mattering once ScrollMasteryEffects.MasteryThreshold is reached and the spell has
/// been granted -- re-activating the same scroll afterward just keeps incrementing harmlessly).
/// </summary>
public struct ScrollMasteryComponent(Guid spellId, ushort usageCount)
{
    public Guid SpellId { get; } = spellId;
    public ushort UsageCount { get; set; } = usageCount;
    public override readonly string ToString() => $"SpellId : {SpellId}\nUsageCount : {UsageCount}";
}
