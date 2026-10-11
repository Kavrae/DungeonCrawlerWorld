using System.Text.Json.Serialization;

namespace SpriteManifestBuilder;

/// <summary>Mirrors Game/Sprites/SpriteManifestEntry.cs's shape -- see ManifestCell's own doc comment for why this is a separate, independently-maintained type.</summary>
/// <param name="Cells">A linked set saved unfinished holds null for each empty slot.</param>
/// <param name="ColumnSpan">The span this tool fills options and slots with (0 means 1). Authoring state only: the game doesn't declare it and skips it, since each cell carries its own span.</param>
/// <param name="RowSpan">See ColumnSpan.</param>
/// <param name="Top">A wall's top, authored apart from its face (this entry's own cells).</param>
/// <param name="VariantWeights">For a linked part, each variant set's chance in whole percent, adding up to 100; null (an even split) for equal chances.</param>
public sealed record ManifestEntry(
    string Name,
    List<ManifestCell?> Cells,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PixelsPerTile = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PivotX = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PivotY = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] SpriteLinking Linking = SpriteLinking.None,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LinkGroup = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ManifestPart? Top = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int ColumnSpan = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int RowSpan = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] List<int>? VariantWeights = null)
{
    public const int CellsPerLinkedSet = 16;

    [JsonIgnore]
    public string EffectiveLinkGroup => LinkGroup ?? Name;

    [JsonIgnore]
    public string EffectiveTopLinkGroup => Top?.LinkGroup ?? Name;

    /// <summary>Every link group this entry's linked pictures join by: its own if it is a linked set, its top's if it is a wall.</summary>
    [JsonIgnore]
    public IEnumerable<string> LinkGroups
    {
        get
        {
            if (Linking == SpriteLinking.Cardinal)
            {
                yield return EffectiveLinkGroup;
            }

            if (Top is { Linking: not SpriteLinking.None })
            {
                yield return EffectiveTopLinkGroup;
            }
        }
    }
}

/// <summary>Mirrors Game/Sprites/SpriteLinking.cs.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SpriteLinking>))]
public enum SpriteLinking : byte
{
    None,
    Cardinal,
    Horizontal,
}
