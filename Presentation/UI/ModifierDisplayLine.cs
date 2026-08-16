using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Utilities;
using Game.Modules.Core.Components;
using Game.Modules.StatModifiers;
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
public readonly record struct ModifierDisplayLine(string Text, StatusEffectSource? Source, ushort? RemainingDurationFrames, string? ModifierText = null, StatModifierOperation? Operation = null);

/// <summary>Shared formatting for ModifierDisplayLine's Source/RemainingDurationFrames -- one place so every consumer (AbilityScoreModifierFormatter today, Skills/Action-leveling formatters later) reads the same source name and duration text for the same underlying StatModifierComponent.</summary>
public static class ModifierDisplayFormatting
{
    /// <summary>Admin/AI have no entity to name (StatusEffectSource.ToString() already covers them); an Entity source resolves DisplayTextComponent.Name if present, else falls back to a numeric label.</summary>
    public static string DescribeSource(ComponentManager componentManager, StatusEffectSource source)
    {
        if (source.Kind != StatusEffectSourceKind.Entity)
        {
            return source.ToString();
        }

        return componentManager.IsRegistered<DisplayTextComponent>()
            && componentManager.GetDirectPool<DisplayTextComponent>().TryGetReadonly(source.EntityId, out var displayText)
            ? displayText.Name
            : $"Entity#{source.EntityId}";
    }

    /// <summary>"Permanent" when null (StatModifierComponent.RemainingDurationFrames' own null-means-permanent convention), else "{n}s remaining" -- n = Ceiling(frames / GameTiming.FramesPerSecond), the same rounding convention PotionCooldownEffects.RemainingSeconds already uses.</summary>
    public static string FormatDuration(ushort? remainingDurationFrames) =>
        remainingDurationFrames is not { } frames
            ? "Permanent"
            : $"{(int)System.Math.Ceiling(frames / (float)GameTiming.FramesPerSecond)}s remaining";
}
