using Engine.Content;
using Engine.ECS.Components;
using Engine.Modules;
using Engine.Tags;

namespace Tests.Content;

/// <summary>A field's declared default against the one its codec passes: the writer refuses a different one always, the reader in Debug.</summary>
[TestClass]
public sealed class ContentFieldDefaultTests
{
    private static readonly Guid RecordId = new("5b0f6f5e-6b8c-4b1e-9a51-0000000000aa");

    private sealed record CountedRecord(Guid Id, long Count, string? Note);

    /// <summary>A codec that passes writerDefault to the writer and readerDefault to the reader for "c", against a schema declaring 1; "o" is declared optional.</summary>
    private sealed class CountedRecordCodec(long writerDefault, long readerDefault, bool writesNoteWithDefault = false) : ContentCodec<CountedRecord>
    {
        public override ContentSchema Schema { get; } = new(nameof(CountedRecord),
        [
            ContentField.Guid(ContentTable.IdKey, "Id", isRequired: true),
            ContentField.Integer("c", "Count", defaultValue: 1),
            ContentField.OptionalText("o", "Note"),
        ]);

        public override void WriteProperties(ContentWriter writer, CountedRecord value)
        {
            writer.Guid(ContentTable.IdKey, value.Id);
            writer.Integer("c", value.Count, writerDefault);
            if (writesNoteWithDefault)
            {
                writer.String("o", value.Note ?? string.Empty);
            }
            else
            {
                writer.OptionalString("o", value.Note);
            }
        }

        public override CountedRecord Read(ContentObject element) =>
            new(element.Guid(ContentTable.IdKey), element.Integer("c", readerDefault), element.OptionalString("o"));
    }

    private sealed class CountedModule(CountedRecordCodec codec) : IModule
    {
        public string Name => "Counted";

        public void DeclareContentTypes(ContentTypeDeclarations contentTypes) => contentTypes.RegisterValue(codec);

        public void RegisterComponents(ComponentRegistration registration)
        {
        }
    }

    private static ContentTypeRegistry Types(CountedRecordCodec codec)
    {
        var module = new CountedModule(codec);
        return ContentTypeRegistry.Declare([module], GameplayTagRegistry.Declare([module]));
    }

    private static CountedRecord Read(ContentTypeRegistry types, string line)
    {
        using var document = System.Text.Json.JsonDocument.Parse(line);
        return ContentObject.Read<CountedRecord>(document.RootElement, types, NoContentReferences.Instance);
    }

    [TestMethod]
    public void ACodecPassingItsSchemasDefault_WritesAndReadsIt()
    {
        var types = Types(new CountedRecordCodec(writerDefault: 1, readerDefault: 1));

        var line = ContentTable.WriteLine(new CountedRecord(RecordId, 1, null), types);

        Assert.AreEqual($"{{\"id\":\"{ContentGuid.Format(RecordId)}\"}}", line);
        Assert.AreEqual(1, Read(types, line).Count);
        Assert.AreEqual(1L, new CountedRecordCodec(1, 1).Schema.FieldOf("c").Default);
        Assert.IsNull(new CountedRecordCodec(1, 1).Schema.FieldOf("o").Default);
    }

    [TestMethod]
    public void AWriterDefaultThatDisagreesWithTheSchema_Throws_NamingTheKey()
    {
        var types = Types(new CountedRecordCodec(writerDefault: 0, readerDefault: 1));

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => ContentTable.WriteLine(new CountedRecord(RecordId, 5, null), types));

        StringAssert.Contains(exception.Message, "'c'");
        StringAssert.Contains(exception.Message, nameof(CountedRecord));
    }

    [TestMethod]
    public void AnOptionalFieldWrittenWithADefault_Throws_NamingTheKey()
    {
        var types = Types(new CountedRecordCodec(writerDefault: 1, readerDefault: 1, writesNoteWithDefault: true));

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => ContentTable.WriteLine(new CountedRecord(RecordId, 5, "Hello"), types));

        StringAssert.Contains(exception.Message, "'o'");
        StringAssert.Contains(exception.Message, "none");
    }

#if DEBUG
    [TestMethod]
    public void AReaderDefaultThatDisagreesWithTheSchema_ThrowsInDebug_NamingTheKey()
    {
        var types = Types(new CountedRecordCodec(writerDefault: 1, readerDefault: 7));

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => Read(types, $"{{\"id\":\"{ContentGuid.Format(RecordId)}\",\"c\":3}}"));

        StringAssert.Contains(exception.Message, "'c'");
    }
#endif
}
