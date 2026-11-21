using Game;
using Game.Modules.Actions;

namespace Presentation.UI;

/// <summary>How an activation's timing reads in a details window: one word, and a tooltip saying what it means and how long it takes.</summary>
public static class TimingText
{
    /// <summary>The line for an activation that cancels its user's windup and frees their action lock (ActionTiming.ReleasesActionLock).</summary>
    public const string ReleasesActionLock = "Cancels your current windup";

    public static string Word(ActionTimingCategory category) => category switch
    {
        ActionTimingCategory.Immediate => "Immediate",
        ActionTimingCategory.Delayed => "Delayed",
        ActionTimingCategory.FreeCast => "Freecast",
        _ => category.ToString(),
    };

    /// <summary>What category means for whoever uses it.</summary>
    /// <param name="actionLockFrames">The lock it sets, or the windup a Delayed one waits -- the user's as used, or the activation's own; null when neither is known (no user), which reads as "your action lock".</param>
    public static string Tooltip(ActionTimingCategory category, ushort? actionLockFrames)
    {
        var lockLength = actionLockFrames is { } frames ? Seconds(frames) : "your action lock";
        return category switch
        {
            ActionTimingCategory.Immediate => $"Instant, then locks you for {lockLength}",
            ActionTimingCategory.Delayed => actionLockFrames is null ? "Winds up for your action lock, can be cancelled" : $"{lockLength} windup, can be cancelled",
            ActionTimingCategory.FreeCast => "Instant, no action lock",
            _ => category.ToString(),
        };
    }

    /// <summary>frames in seconds to two decimal places at most ("0.5s", "0.25s", "5s").</summary>
    public static string Seconds(int frames) => $"{frames / (float)GameFrameRate.FramesPerSecond:0.##}s";
}
