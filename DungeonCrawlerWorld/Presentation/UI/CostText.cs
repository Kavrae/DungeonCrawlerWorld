using Game;
using Game.Effects;
using Game.Modules.Actions;

namespace Presentation.UI;

/// <summary>What using an action or item takes, in text, the same wherever it is described: its activation effects, and a toggle's upkeep while on.</summary>
/// <remarks>Used by the inventory tooltip, the hotbar summary and Item Details, so the three can't word a cost differently. Always in simple mode: a cost's body part or stacking is never worth a summary's space.</remarks>
public static class CostText
{
    /// <summary>"To use: ..." when using it takes anything -- "To turn on: ..." for a toggle, since turning one off takes nothing -- or null.</summary>
    public static string? ActivationLine(ActivatableDefinition definition, EffectTextNames names) =>
        definition.ActivationEffects.Count > 0 && Join(definition.ActivationEffects, names) is { Length: > 0 } text
            ? $"{(definition.Toggle is null ? "To use" : "To turn on")}: {text}"
            : null;

    /// <summary>"While on, every 1s: ..." when a toggle does anything while on, or null.</summary>
    public static string? UpkeepLine(ActivatableDefinition definition, EffectTextNames names) =>
        definition.Toggle?.Periodic is { } periodic && Join(periodic.Effects, names) is { Length: > 0 } text
            ? $"While on, every {FormatInterval(periodic.IntervalFrames)}: {text}"
            : null;

    /// <summary>The lines ActivationLine and UpkeepLine give, those that apply, for a summary that lists them after its own text.</summary>
    public static IEnumerable<string> Lines(ActivatableDefinition definition, EffectTextNames names)
    {
        if (ActivationLine(definition, names) is { } activation)
        {
            yield return activation;
        }

        if (UpkeepLine(definition, names) is { } upkeep)
        {
            yield return upkeep;
        }
    }

    private static string Join(IReadOnlyList<Effect> effects, EffectTextNames names) =>
        string.Join("; ", effects.SelectMany(static effect => effect.Entries).Select(entry => EffectFormatting.FormatEntry(entry, names, DetailsMode.Simple)));

    private static string FormatInterval(ushort intervalFrames)
    {
        var seconds = intervalFrames / (float)GameFrameRate.FramesPerSecond;
        return seconds == MathF.Round(seconds) ? $"{seconds:0}s" : $"{seconds:0.##}s";
    }
}
