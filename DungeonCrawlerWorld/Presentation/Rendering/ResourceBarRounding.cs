namespace Presentation.Rendering;

/// <summary>How a health or mana bar rounds its numbers before ResourceBarValueText draws them.</summary>
/// <remarks>
/// Each number rounds the way that agrees with the game: current health rounds up, so 0 is shown
/// only at 0; current mana rounds down, since casting needs CurrentMana &gt;= cost; an effective
/// maximum rounds to nearest, midpoints away from zero.
/// </remarks>
public static class ResourceBarRounding
{
    public static int DisplayedHealth(float currentHealth) => (int)MathF.Ceiling(currentHealth);

    public static int DisplayedMana(float currentMana) => (int)MathF.Floor(currentMana);

    public static int DisplayedMaximum(float effectiveMaximum) => (int)MathF.Round(effectiveMaximum, MidpointRounding.AwayFromZero);
}
