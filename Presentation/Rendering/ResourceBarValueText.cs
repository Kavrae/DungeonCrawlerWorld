namespace Presentation.Rendering;

/// <summary>The "current / maximum" text drawn on a HUD resource bar, rebuilt only when a displayed number changes.</summary>
/// <remarks>
/// Each number rounds the way that agrees with the game: current health rounds up, so 0 is shown
/// only at 0; current mana rounds down, since casting needs CurrentMana &gt;= cost; an effective
/// maximum rounds to nearest, midpoints away from zero. The displayed current is clamped to
/// [0, displayed maximum].
/// </remarks>
public sealed class ResourceBarValueText
{
    private int _displayedCurrent = -1;
    private int _displayedMaximum = -1;

    public string Text { get; private set; } = string.Empty;

    public static int DisplayedHealth(float currentHealth) => (int)MathF.Ceiling(currentHealth);

    public static int DisplayedMana(float currentMana) => (int)MathF.Floor(currentMana);

    public static int DisplayedMaximum(float effectiveMaximum) => (int)MathF.Round(effectiveMaximum, MidpointRounding.AwayFromZero);

    public string Update(int displayedCurrent, int displayedMaximum)
    {
        displayedMaximum = Math.Max(0, displayedMaximum);
        displayedCurrent = Math.Clamp(displayedCurrent, 0, displayedMaximum);

        if (displayedCurrent != _displayedCurrent || displayedMaximum != _displayedMaximum)
        {
            _displayedCurrent = displayedCurrent;
            _displayedMaximum = displayedMaximum;
            Text = $"{displayedCurrent} / {displayedMaximum}";
        }

        return Text;
    }
}
