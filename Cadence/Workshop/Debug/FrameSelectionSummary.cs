namespace Workshop.Debug;

/// <summary>The frames selected on the timeline that are still in its window: the first and last frame numbers, how many, and their update milliseconds -- average, largest, and the frame that took it.</summary>
public readonly record struct FrameSelectionSummary(long FirstFrame, long LastFrame, int FrameCount, double AverageMilliseconds, double MaximumMilliseconds, long SlowestFrame);
