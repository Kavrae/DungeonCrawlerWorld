using System.Text.Json.Serialization;

namespace SpriteManifestBuilder;

/// <summary>Mirrors Game/Sprites/SpriteManifestPart.cs: a separately authored picture of an entry -- a wall's top. ColumnSpan/RowSpan are this tool's authoring state, as on ManifestEntry.</summary>
public sealed record ManifestPart(
    List<ManifestCell?> Cells,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PixelsPerTile = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PivotX = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PivotY = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] SpriteLinking Linking = SpriteLinking.None,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LinkGroup = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int ColumnSpan = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int RowSpan = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] List<int>? VariantWeights = null);
