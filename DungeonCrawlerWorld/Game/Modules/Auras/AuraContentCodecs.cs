using Engine.Content;
using Engine.Tiles;
using Game.Content;
using Game.Effects;

namespace Game.Modules.Auras;

/// <summary>Codecs for AuraDefinition (its own table) and AuraSourceGrant, which names its aura by Guid; registered by AurasModule.</summary>
internal static class AuraContentCodecs
{
    public const string AurasTable = "Auras";

    public static void Register(ContentTypeDeclarations contentTypes)
    {
        contentTypes.RegisterValue(new AuraDefinitionCodec());
        contentTypes.RegisterPolymorphic<IEffectEntry, AuraSourceGrant>(nameof(AuraSourceGrant), new AuraSourceGrantCodec());
    }

    private sealed class AuraDefinitionCodec : ContentCodec<AuraDefinition>
    {
        public override ContentSchema Schema { get; } = new(nameof(AuraDefinition),
        [
            ContentField.Guid(ContentTable.IdKey, "Id", isRequired: true),
            ContentField.Text("n", "Name", isRequired: true),
            ContentField.Color("gc", "Glow colour"),
            ContentField.ListOf<Effect>("fx", "Effects each tick", ContentFieldKind.Object),
            ContentField.TagSet("t", "Tags"),
            ContentField.Enum<AuraMagnitude>("mg", "Magnitude"),
            ContentField.Enum<AuraFalloff>("fo", "Falloff"),
        ]);

        public override void WriteProperties(ContentWriter writer, AuraDefinition value)
        {
            writer.Guid(ContentTable.IdKey, value.Id);
            writer.String("n", value.Name);
            writer.Color("gc", value.GlowColor);
            writer.List("fx", value.Effects);
            writer.Tags("t", value.Tags);
            writer.Enum("mg", value.Magnitude, AuraMagnitude.Power);
            writer.Enum("fo", value.Falloff, AuraFalloff.Linear);
        }

        public override AuraDefinition Read(ContentObject element) => new(
            element.Guid(ContentTable.IdKey),
            element.String("n"),
            element.Color("gc"),
            element.List<Effect>("fx"),
            element.Tags("t"),
            element.Enum("mg", AuraMagnitude.Power),
            element.Enum("fo", AuraFalloff.Linear));
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
