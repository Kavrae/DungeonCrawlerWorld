namespace Game.Blueprints.Parts;

/// <summary>TEMPORARY: gives an entity a description long enough to actually word-wrap and hyphenate, for visually exercising SelectionWindowContent against real content.</summary>
/// <remarks>The wrap algorithm itself is unit tested; nothing else on the map has a description this long. Remove once real content does. Only the description is replaced; the name stays whatever the rest of the entity's blueprint composes.</remarks>
public static class LongDescriptionPart
{
    public static readonly Guid Id = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000108");

    public const string Name = "Long Description";

    private const string Description =
        "ThisIsAReallyLongDescriptionToTestTheWordWrapCapabilitiesAroundHyphenatingLongWordsMultipleTimes";

    public static readonly BlueprintDefinition Definition = new(Id, Name)
    {
        Appearance = new() { Description = Description }
    };
}
