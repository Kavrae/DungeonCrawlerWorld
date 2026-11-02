using System.Globalization;

namespace Presentation.Rendering;

/// <summary>How a health or mana bar's popup shows a regeneration rate: signed, one decimal place, per second ("+1.2/s").</summary>
public static class ResourceRegenerationText
{
    /// <summary>perSecond rounded to the tenths the text shows -- the key a cache of the text is held on.</summary>
    public static int DisplayedTenths(float perSecond) => (int)MathF.Round(perSecond * 10f, MidpointRounding.AwayFromZero);

    public static string Format(int tenths) => (tenths < 0 ? "-" : "+") + (Math.Abs(tenths) / 10f).ToString("0.0", CultureInfo.InvariantCulture) + "/s";
}
