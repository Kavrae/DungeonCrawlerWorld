using Engine.Content;
using Engine.Tiles;
using Game.Content;
using Game.Effects;
using Game.Tags;

namespace Game.Modules.Auras;

/// <summary>Codecs for AuraDefinition (its own table) and AuraSourceGrant, which names its aura by Guid.</summary>
/// <remarks>
/// AuraDefinition's is registered by CoreModule: every build reads the Auras table, since terrain and blueprints name
/// their auras in it and the catalog they resolve through is the build's own, whatever modules it holds.
/// AuraSourceGrant's is AurasModule's, the module that applies it.
/// </remarks>
internal static class AuraContentCodecs
{
    public const string AurasTable = "Auras";

    public static void RegisterDefinitions(ContentTypeDeclarations contentTypes) => contentTypes.RegisterValue(new AuraDefinitionCodec());

    public static void RegisterEntries(ContentTypeDeclarations contentTypes) =>
        contentTypes.RegisterPolymorphic<IEffectEntry, AuraSourceGrant>(nameof(AuraSourceGrant), new AuraSourceGrantCodec());

    private sealed class AuraDefinitionCodec : ContentCodec<AuraDefinition>
    {
        public override ContentSchema Schema { get; } = new(nameof(AuraDefinition),
        [
            ContentField.Guid(ContentTable.IdKey, "Id", isRequired: true),
            ContentField.Text("n", "Name", isRequired: true),
            ContentField.Text("d", "Description"),
            ContentField.Color("gc", "Glow colour"),
            ContentField.ListOf<Effect>("fx", "Effects each tick", ContentFieldKind.Object),
            ContentField.TagSet("t", "Tags", GameTagDomains.Activation),
            ContentField.Enum<AuraMagnitude>("mg", "Magnitude"),
            ContentField.Enum<AuraFalloff>("fo", "Falloff"),
            ContentField.Number("go", "Glow opacity multiplier", 0, 1),
        ]);

        public override void WriteProperties(ContentWriter writer, AuraDefinition value)
        {
            writer.Guid(ContentTable.IdKey, value.Id);
            writer.String("n", value.Name);
            writer.String("d", value.Description);
            writer.Color("gc", value.GlowColor);
            writer.List("fx", value.Effects);
            writer.Tags("t", value.Tags);
            writer.Enum("mg", value.Magnitude, AuraMagnitude.Power);
            writer.Enum("fo", value.Falloff, AuraFalloff.Linear);
            writer.Number("go", value.GlowOpacityMultiplier, 0.5f);
        }

        public override AuraDefinition Read(ContentObject element) => new(
            element.Guid(ContentTable.IdKey),
            element.String("n"),
            element.Color("gc"),
            element.List<Effect>("fx"),
            element.Tags("t"),
            element.Enum("mg", AuraMagnitude.Power),
            element.Enum("fo", AuraFalloff.Linear),
            element.Number("go", 0.5f),
            element.String("d"));
    }

    /// <summary>The aura is its Guid in the Auras table, resolved when the record is read -- the table is loaded before anything that grants one.</summary>
    private sealed class AuraSourceGrantCodec : ContentCodec<AuraSourceGrant>
    {
        public override ContentSchema Schema { get; } = new(nameof(AuraSourceGrant),
        [
            ContentField.Reference("au", "Aura", AurasTable, isRequired: true),
            ContentField.Integer("p", "Power", 0, ushort.MaxValue),
            ContentField.Integer("s", "Size", 0, byte.MaxValue),
            ContentField.Integer("d", "Duration in frames (none: until revoked)", 0, ushort.MaxValue),
        ]);

        public override void WriteProperties(ContentWriter writer, AuraSourceGrant value)
        {
            writer.Guid("au", value.Aura.Id);
            writer.Integer("p", value.Power);
            writer.Integer("s", value.Size);
            writer.OptionalInteger("d", value.DurationFrames);
        }

        public override AuraSourceGrant Read(ContentObject element) => new(
            element.Reference<AuraDefinition>("au"),
            ContentNumbers.UInt16(element.Integer("p"), "p"),
            ContentNumbers.Byte(element.Integer("s"), "s"),
            ContentNumbers.UInt16(element.OptionalInteger("d"), "d"));
    }
}
