namespace Engine.Diagnostics;

/// <summary>A fixed ring of frame records between one writer (the simulation thread) and one reader (the link's sender), which never makes the writer wait.</summary>
/// <remarks>
/// Each slot holds a frame number, a paused flag and ValueCount doubles, all allocated at construction. The writer
/// always writes: when the reader falls more than Capacity records behind, the oldest are overwritten, and the reader
/// counts them as dropped and reports the count with the next record it reads. No lock: each slot carries a sequence
/// number, set to "being written" before its values change and to the record's number after, and the reader keeps a
/// copy only if the sequence was that record's number both before and after it copied.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class DiagnosticsFrameRing
{
    private const long SlotBeingWritten = -1;

    private readonly long[] _slotSequences;
    private readonly long[] _frames;
    private readonly bool[] _pausedFlags;
    private readonly double[] _values;
    private long _writtenCount;
    private long _nextReadIndex;
    private long _writingIndex;
    private int _droppedSinceLastRead;

    public DiagnosticsFrameRing(int capacity, int valueCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(valueCount);

        Capacity = capacity;
        ValueCount = valueCount;
        _slotSequences = new long[capacity];
        _frames = new long[capacity];
        _pausedFlags = new bool[capacity];
        _values = new double[capacity * valueCount];
        Array.Fill(_slotSequences, SlotBeingWritten);
    }

    public int Capacity { get; }

    /// <summary>The doubles each record holds.</summary>
    public int ValueCount { get; }

    /// <summary>Every record the reader has counted as dropped.</summary>
    public long TotalDroppedCount { get; private set; }

    /// <summary>Starts writing the next record and returns its values to fill; CommitWrite publishes it. Writer only.</summary>
    public Span<double> BeginWrite(long frame, bool isPaused)
    {
        _writingIndex = _writtenCount;
        var slot = (int)(_writingIndex % Capacity);
        Interlocked.Exchange(ref _slotSequences[slot], SlotBeingWritten);
        _frames[slot] = frame;
        _pausedFlags[slot] = isPaused;
        return _values.AsSpan(slot * ValueCount, ValueCount);
    }

    /// <summary>Publishes the record BeginWrite started. Writer only.</summary>
    public void CommitWrite()
    {
        var slot = (int)(_writingIndex % Capacity);
        Volatile.Write(ref _slotSequences[slot], _writingIndex);
        Volatile.Write(ref _writtenCount, _writingIndex + 1);
    }

    /// <summary>Copies the oldest unread record into values, which holds at least ValueCount doubles; false when there is none. Reader only.</summary>
    /// <param name="droppedBefore">Records dropped since the previous record read.</param>
    public bool TryRead(Span<double> values, out long frame, out bool isPaused, out int droppedBefore)
    {
        while (true)
        {
            var writtenCount = Volatile.Read(ref _writtenCount);
            if (_nextReadIndex >= writtenCount)
            {
                frame = 0;
                isPaused = false;
                droppedBefore = 0;
                return false;
            }

            if (writtenCount - _nextReadIndex > Capacity)
            {
                CountDropped(writtenCount - Capacity - _nextReadIndex);
                _nextReadIndex = writtenCount - Capacity;
            }

            var slot = (int)(_nextReadIndex % Capacity);
            var sequenceBefore = Volatile.Read(ref _slotSequences[slot]);
            frame = _frames[slot];
            isPaused = _pausedFlags[slot];
            _values.AsSpan(slot * ValueCount, ValueCount).CopyTo(values);
            Interlocked.MemoryBarrier();
            var sequenceAfter = Volatile.Read(ref _slotSequences[slot]);

            if (sequenceBefore != _nextReadIndex || sequenceAfter != _nextReadIndex)
            {
                CountDropped(1);
                _nextReadIndex++;
                continue;
            }

            _nextReadIndex++;
            droppedBefore = _droppedSinceLastRead;
            _droppedSinceLastRead = 0;
            return true;
        }
    }

    private void CountDropped(long count)
    {
        TotalDroppedCount += count;
        _droppedSinceLastRead = (int)System.Math.Min(int.MaxValue, _droppedSinceLastRead + count);
    }
}
