using Game.World;

namespace Presentation.UI.FloatingText;

/// <summary>Which of an entity's two waterfalls a floating text falls in: numbers on the left, statuses on the right.</summary>
public enum FloatingTextLane : byte
{
    Numbers,
    Statuses,
}

/// <summary>How each kind of floating text is laid out and moves.</summary>
public static class FloatingTextLayout
{
    public static FloatingTextLane GetLane(FloatingTextKind kind) => kind switch
    {
        FloatingTextKind.StatusEffectStacksAdded or FloatingTextKind.Immune => FloatingTextLane.Statuses,
        _ => FloatingTextLane.Numbers,
    };

    /// <summary>-1 rises, 1 falls: a hit that didn't land falls, everything else rises.</summary>
    public static sbyte GetVerticalDirection(FloatingTextKind kind) =>
        kind is FloatingTextKind.Dodged or FloatingTextKind.Immune ? (sbyte)1 : (sbyte)-1;
}
