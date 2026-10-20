using Engine.Content;
using Game.Modules.Health;
using Game.Modules.Health.Components;

namespace Game.Effects;

/// <summary>Codecs for Effect and BodyPartTargeting, the values every effect holder shares; registered by Core.</summary>
internal static class EffectContentCodecs
{
    public static void Register(ContentTypeDeclarations contentTypes)
    {
        contentTypes.RegisterValue(new EffectCodec());
        contentTypes.RegisterValue(new BodyPartTargetingCodec());
    }

    /// <summary>An Effect as {"e":[entries]}, each entry with its discriminator first.</summary>
    private sealed class EffectCodec : ContentCodec<Effect>
    {
        public override ContentSchema Schema { get; } = new(nameof(Effect),
        [
            ContentField.ListOf<IEffectEntry>("e", "Entries", ContentFieldKind.Polymorphic),
        ]);

        public override void WriteProperties(ContentWriter writer, Effect value) => writer.List("e", value.Entries);

        public override Effect Read(ContentObject element) => new(element.List<IEffectEntry>("e"));
    }

    /// <summary>Which body part an entry lands on; Unspecified (the default) is omitted by every entry that holds one.</summary>
    private sealed class BodyPartTargetingCodec : ContentCodec<BodyPartTargeting>
    {
        public override ContentSchema Schema { get; } = new(nameof(BodyPartTargeting),
        [
            ContentField.Enum<BodyPartTargetingKind>("k", "Kind"),
            ContentField.Enum<BodyPartType>("pt", "Part type"),
            ContentField.Enum<BodyPartFallback>("fb", "Fallback"),
        ]);

        public override void WriteProperties(ContentWriter writer, BodyPartTargeting value)
        {
            writer.Enum("k", value.Kind);
            writer.Enum("pt", value.PartType);
            writer.Enum("fb", value.Fallback, BodyPartFallback.Random);
        }

        public override BodyPartTargeting Read(ContentObject element) =>
            new(element.Enum<BodyPartTargetingKind>("k"), element.Enum<BodyPartType>("pt"), element.Enum("fb", BodyPartFallback.Random));
    }
}

/// <summary>Writing and reading an entry's body part targeting, omitted when Unspecified.</summary>
internal static class BodyPartTargetingContent
{
    public const string Key = "bp";

    public static ContentField Field { get; } = ContentField.Object<BodyPartTargeting>(Key, "Body part");

    public static void Write(ContentWriter writer, BodyPartTargeting value)
    {
        if (value != BodyPartTargeting.Unspecified)
        {
            writer.ValueObject(Key, value);
        }
    }

    public static BodyPartTargeting Read(ContentObject element) => element.ValueObject(Key, BodyPartTargeting.Unspecified);
}
