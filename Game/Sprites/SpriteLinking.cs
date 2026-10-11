using System.Text.Json.Serialization;

namespace Game.Sprites;

/// <summary>Whether a manifest part's cells are one piece per combination of joined neighbours rather than interchangeable options.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SpriteLinking>))]
public enum SpriteLinking : byte
{
    None,

    /// <summary>Sixteen cells per variant set, in neighbour-mask order: north 1, east 2, south 4, west 8. A wall's top.</summary>
    Cardinal,

    /// <summary>
    /// Four cells per variant set, in neighbour-mask order east 1, west 2: standalone (no wall either side), left end
    /// (a wall only to the east), right end (a wall only to the west), middle (walls both sides). A wall's face.
    /// Only the standalone piece is required; a missing one falls back to it.
    /// </summary>
    Horizontal,
}
