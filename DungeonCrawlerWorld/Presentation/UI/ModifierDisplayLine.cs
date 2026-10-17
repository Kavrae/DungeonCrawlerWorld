using Game;
using Game.Modules.StatModifiers;
using Game.Views;
using Game.World;

namespace Presentation.UI;

/// <summary>
/// One formatted "what's contributing to this total, from where, for how long" line -- generic
/// over anything built on StatModifierComponent's Source/RemainingDurationFrames shape (Ability
/// Scores today; Skills and Spell/Action leveling are meant to reuse the same StatModifierComponent
/// layering per TODO.md's Skills entry, so this isn't AbilityScores-specific). Source/ModifierText/
/// Operation/RemainingDurationFrames are null for a non-modifier line (e.g.
/// AbilityScoreModifierFormatter's own "Base : N" line) -- nothing to hover a source/duration
/// popup for, and no group-separator boundary is anchored to it. ModifierText is the signed
/// value/percent alone (e.g. "+1", "-10%"), without the source prefix Text carries -- the hover
/// popup shows it under a title of its own (the source name), so it shouldn't repeat it.
/// Operation is what lets a caller (e.g. AbilityScoreWindow) detect the Additive/Multiplicative
/// group boundary to draw a separator at, without re-deriving it from Text.
/// </summary>
public readonly record struct ModifierDisplayLine(string Text, ActionSource? Source, int? RemainingDurationFrames, string? ModifierText = null, StatModifierOperation? Operation = null);

/// <summary>Shared formatting for ModifierDisplayLine's Source/RemainingDurationFrames -- one place so every consumer (AbilityScoreModifierFormatter today, Skills/Action-leveling formatters later) reads the same source name and duration text for the same underlying StatModifierComponent.</summary>
public static class ModifierDisplayFormatting
{
    /// <summary>The hover popup for one effect and where it came from: the source's name as the title, then the effect and how long it has left.</summary>
    /// <param name="effectText">The effect alone, with no source and no duration.</param>
    public static (string Title, string Body) SourcePopup(ActionSourceNaming actionSourceNaming, ActionSource source, string effectText, int? remainingDurationFrames) =>
        SourcePopupShowingSeconds(actionSourceNaming, source, effectText, RemainingSecondsShown(remainingDurationFrames));

    /// <summary>SourcePopup from the remaining duration as shown (RemainingSecondsShown) -- for a caller that keys a cache on it, so the popup is rebuilt once a second rather than every frame.</summary>
    public static (string Title, string Body) SourcePopupShowingSeconds(ActionSourceNaming actionSourceNaming, ActionSource source, string effectText, int? remainingSeconds) =>
        (actionSourceNaming.Describe(source), $"{effectText}\n{FormatRemainingSeconds(remainingSeconds)}");

    /// <summary>"Permanent" when null (a StatModifierComponent whose ExpiresAtFrame is FrameDeadline.Never), else "{n}s remaining" -- n = Ceiling(frames / GameFrameRate.FramesPerSecond), the same rounding convention PotionCooldownEffects.RemainingSeconds already uses.</summary>
    public static string FormatDuration(int? remainingDurationFrames) => FormatRemainingSeconds(RemainingSecondsShown(remainingDurationFrames));

    /// <summary>The whole seconds a remaining duration is shown as (see FormatDuration) -- null for a permanent one.</summary>
    public static int? RemainingSecondsShown(int? remainingDurationFrames) =>
        remainingDurationFrames is { } frames ? (int)System.Math.Ceiling(frames / (float)GameFrameRate.FramesPerSecond) : null;

    private static string FormatRemainingSeconds(int? remainingSeconds) =>
        remainingSeconds is not { } seconds ? "Permanent" : $"{seconds}s remaining";
}
