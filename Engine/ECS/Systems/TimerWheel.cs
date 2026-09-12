namespace Engine.ECS.Systems;

/// <summary>One scheduled fire: which timer (entity, plus key for multi-instance pools), and the deadline it was scheduled for.</summary>
public readonly record struct TimerEntry(int EntityId, int Key, uint Deadline);

/// <summary>A hashed timer wheel over simulation frames: schedule an entry for a frame, and each drain hands back exactly the entries due.</summary>
/// <remarks>
/// A ring of slots; slot <c>frame &amp; (SlotCount - 1)</c> holds the entries due on that frame
/// (Varghese &amp; Lauck, 1987). Draining a frame touches only that slot, so the per-frame cost is
/// O(entries due), not O(timers alive) -- the reason ticking countdowns moved here from
/// scan-and-decrement (PLAN-timer-wheel.md, "Step 3 result").
///
/// Component-agnostic: it never validates an entry. PackedTimerWheel/MultiTimerWheel do that
/// (lazy cancellation) -- an entry whose timer was removed, re-armed or recycled since it was
/// scheduled is simply dropped when its slot comes round. Nothing is ever unscheduled.
///
/// - A deadline at or before the next frame to drain fires on that next drain (late, never lost).
/// - A deadline at least SlotCount frames out waits in an overflow list and moves into its slot
///   at the start of the revolution that contains it, always before its frame is drained.
/// - FrameDeadline.Never is never scheduled.
/// - Entries due on the same frame come back in the order they entered that frame's slot:
///   scheduling order, except that an entry waiting in overflow joins its slot at the start of
///   its revolution, behind anything already scheduled straight into the slot. Deterministic
///   either way, so seeded runs stay reproducible; not a promise of first-scheduled-first-fired.
/// - Draining past several frames at once drains each of them in order.
///
/// Frame 0 is always the first frame drained, because a wheel is always built before the
/// simulation runs: every one of them is constructed during GameBootstrapper.Build, and a
/// SimulationClock starts each session at 0. There is deliberately no way to start one elsewhere
/// -- a wheel handed a later start frame would just be one more thing to keep in sync with the
/// clock, and nothing needs it.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class TimerWheel
{
    /// <summary>4096 frames, ~68s at 60fps -- longer than every periodic tick, so overflow holds only long durations.</summary>
    public const int DefaultSlotCount = 4096;

    private readonly List<TimerEntry>?[] _slots;
    private readonly int _mask;
    private readonly List<TimerEntry> _overflow = [];
    private readonly List<TimerEntry> _rebucketScratch = [];

    /// <summary>The next frame DrainDue will process. Anything scheduled at or before it fires on that drain.</summary>
    private long _nextFrame;

    /// <param name="slotCount">Ring size; must be a power of two.</param>
    public TimerWheel(int slotCount = DefaultSlotCount)
    {
        if (slotCount <= 0 || (slotCount & (slotCount - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(slotCount), slotCount, "Must be a positive power of two.");
        }

        _slots = new List<TimerEntry>?[slotCount];
        _mask = slotCount - 1;
    }

    /// <summary>Entries waiting to be drained, stale ones included (they are only discovered stale when drained). For diagnostics and tests.</summary>
    public int PendingCount { get; private set; }

    /// <summary>Schedules an entry for its deadline.</summary>
    public void Schedule(int entityId, int key, uint deadline)
    {
        if (deadline == FrameDeadline.Never)
        {
            return;
        }

        Place(new TimerEntry(entityId, key, deadline));
        PendingCount++;
    }

    /// <summary>Appends to <paramref name="due"/> every entry due on each frame from the last drain up to and including <paramref name="now"/>, in frame then scheduling order, and forgets them.</summary>
    /// <remarks>Anything scheduled while the caller processes the results lands on a later frame -- this frame is already consumed.</remarks>
    public void DrainDue(long now, List<TimerEntry> due)
    {
        ArgumentNullException.ThrowIfNull(due);

        for (; _nextFrame <= now; _nextFrame++)
        {
            if ((_nextFrame & _mask) == 0 && _overflow.Count > 0)
            {
                Rebucket();
            }

            var slot = _slots[_nextFrame & _mask];
            if (slot is null || slot.Count == 0)
            {
                continue;
            }

            due.AddRange(slot);
            PendingCount -= slot.Count;
            slot.Clear();
        }
    }

    private void Place(TimerEntry entry)
    {
        var fireFrame = System.Math.Max(entry.Deadline, _nextFrame);
        if (fireFrame - _nextFrame >= _slots.Length)
        {
            _overflow.Add(entry);
            return;
        }

        var index = (int)(fireFrame & _mask);
        (_slots[index] ??= []).Add(entry);
    }

    /// <summary>At the start of a revolution, moves every overflow entry that now fits within one revolution into its slot, keeping overflow order.</summary>
    private void Rebucket()
    {
        _rebucketScratch.Clear();
        foreach (var entry in _overflow)
        {
            if (entry.Deadline - _nextFrame < _slots.Length)
            {
                var index = (int)(entry.Deadline & _mask);
                (_slots[index] ??= []).Add(entry);
            }
            else
            {
                _rebucketScratch.Add(entry);
            }
        }

        _overflow.Clear();
        _overflow.AddRange(_rebucketScratch);
    }
}
