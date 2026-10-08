using Engine.Tags;
using Engine.Utilities;
using Game.Effects;
using Game.Modules.Actions;

namespace Presentation.UI;

/// <summary>What using an action or item takes, in text, the same wherever it is described: its activation effects, and a toggle's upkeep while on.</summary>
/// <remarks>Used by the inventory tooltip, the hotbar summary and Item Details, so the three can't word a cost differently.</remarks>
public static class CostText
{
    /// <summary>"To use: ..." when using it takes anything -- "To turn on: ..." for a toggle, since turning one off takes nothing -- or null.</summary>
    public static string? ActivationLine(ActivatableDefinition definition, GameplayTagRegistry? gameplayTags) =>
        definition.ActivationEffects.Count > 0 && Join(definition.ActivationEffects, gameplayTags) is { Length: > 0 } text
            ? $"{(definition.Toggle is null ? "To use" : "To turn on")}: {text}"
            : null;

    /// <summary>"While on, every 1s: ..." when a toggle does anything while on, or null.</summary>
    public static string? UpkeepLine(ActivatableDefinition definition, GameplayTagRegistry? gameplayTags) =>
        definition.Toggle?.Periodic is { } periodic && Join(periodic.Effects, gameplayTags) is { Length: > 0 } text
            ? $"While on, every {FormatInterval(periodic.IntervalFrames)}: {text}"
            : null;

    /// <summary>The lines ActivationLine and UpkeepLine give, those that apply, for a summary that lists them after its own text.</summary>
    public static IEnumerable<string> Lines(ActivatableDefinition definition, GameplayTagRegistry? gameplayTags)
    {
        if (ActivationLine(definition, gameplayTags) is { } activation)
        {
            yield return activation;
        }

        if (UpkeepLine(definition, gameplayTags) is { } upkeep)
        {
            yield return upkeep;
        }
    }

    private static string Join(IReadOnlyList<Effect> effects, GameplayTagRegistry? gameplayTags) =>
        string.Join("; ", effects.SelectMany(static effect => effect.Entries).Select(entry => EffectFormatting.FormatEntry(entry, gameplayTags)));

    private static string FormatInterval(ushort intervalFrames)
    {
        var seconds = intervalFrames / (float)GameTiming.FramesPerSecond;
        return seconds == MathF.Round(seconds) ? $"{seconds:0}s" : $"{seconds:0.##}s";
    }
}
