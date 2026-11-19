using System.Globalization;

namespace Engine.ECS.Properties;

/// <summary>How an integer property's value, carried as a long, reads as text and back: a ulong is carried as its bits, so it is shown and typed unsigned.</summary>
public static class ComponentPropertyIntegers
{
    /// <summary>The value as its property's type shows it: unsigned for a ulong, else as the long it is.</summary>
    public static string Format(Type valueType, long value) =>
        valueType == typeof(ulong)
            ? unchecked((ulong)value).ToString(CultureInfo.InvariantCulture)
            : value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Parses typed text as its property's type takes it -- a ulong up to ulong.MaxValue, carried as its bits -- false for text that isn't one.</summary>
    public static bool TryParse(Type valueType, string? text, out long value)
    {
        const NumberStyles styles = NumberStyles.AllowLeadingSign | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite;
        if (valueType == typeof(ulong))
        {
            var parsed = ulong.TryParse(text, styles, CultureInfo.InvariantCulture, out var unsigned);
            value = unchecked((long)unsigned);
            return parsed;
        }

        return long.TryParse(text, styles, CultureInfo.InvariantCulture, out value);
    }
}
