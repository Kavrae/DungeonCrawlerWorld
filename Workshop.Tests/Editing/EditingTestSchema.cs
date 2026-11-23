using Engine.Content;
using Engine.ECS.Components;
using Engine.Modules;
using Engine.Tags;
using Workshop.Editing;

namespace Workshop.Tests.Editing;

/// <summary>A small content schema of its own, with no game type in it: a Box with every kind of field the inspector draws -- text, numbers, a flag, an enum, an object, a polymorphic value and a list of them.</summary>
internal static class EditingTestSchema
{
    public const string TableName = "Boxes";

    public enum Shade
    {
        Plain,
        Dark,
        Bright,
    }

    public sealed record Lid(long Height);

    public interface IPart;

    public sealed record Peg(long Length) : IPart;

    public sealed record Hook(long Length, bool IsOpen) : IPart;

    public sealed record Box(Guid Id, string Name, long Count, float Weight, bool IsSealed, Shade Shade, Lid? Lid, IPart? Part, IReadOnlyList<IPart> Parts, string Note);

    public static ContentTypeRegistry Types { get; } = Declare();

    public static Guid BoxId { get; } = new("6b0f6f5e-6b8c-4b1e-9a51-0000000000b1");

    /// <summary>A box with its required fields and a peg among its parts.</summary>
    public static string BoxJson => $"{{\"id\":\"{ContentGuid.Format(BoxId)}\",\"n\":\"Crate\",\"c\":3,\"pr\":[{{\"$\":\"Peg\",\"ln\":2}}]}}";

    private static ContentTypeRegistry Declare()
    {
        var module = new BoxModule();
        return ContentTypeRegistry.Declare([module], GameplayTagRegistry.Declare([module]));
    }

    private sealed class BoxModule : IModule
    {
        public string Name => "Boxes";

        public void DeclareContentTypes(ContentTypeDeclarations contentTypes)
        {
            contentTypes.RegisterValue(new BoxCodec());
            contentTypes.RegisterValue(new LidCodec());
            contentTypes.RegisterPolymorphic<IPart, Peg>("Peg", new PegCodec());
            contentTypes.RegisterPolymorphic<IPart, Hook>("Hook", new HookCodec());
        }

        public void RegisterComponents(ComponentRegistration registration)
        {
        }
    }

    private sealed class BoxCodec : ContentCodec<Box>
    {
        public override ContentSchema Schema { get; } = new(nameof(Box),
        [
            ContentField.Guid(ContentTable.IdKey, "Id", isRequired: true),
            ContentField.Text("n", "Name", isRequired: true),
            ContentField.Integer("c", "Count (how many in the box)", 0, 10, defaultValue: 1),
            ContentField.Number("w", "Weight", 0, 100),
            ContentField.Boolean("s", "Sealed"),
            ContentField.Enum("sh", "Shade", Shade.Plain),
            ContentField.Object<Lid>("lid", "Lid"),
            ContentField.Polymorphic<IPart>("pt", "Part"),
            ContentField.ListOf<IPart>("pr", "Parts", ContentFieldKind.Polymorphic),
            ContentField.Text("nt", "Note") with { IsMultiline = true },
        ]);

        public override void WriteProperties(ContentWriter writer, Box value)
        {
            writer.Guid(ContentTable.IdKey, value.Id);
            writer.String("n", value.Name);
            writer.Integer("c", value.Count, 1);
            writer.Number("w", value.Weight);
            writer.Boolean("s", value.IsSealed);
            writer.Enum("sh", value.Shade, Shade.Plain);
            writer.Object("lid", value.Lid);
            writer.Polymorphic("pt", value.Part);
            writer.List("pr", value.Parts);
            writer.String("nt", value.Note);
        }

        public override Box Read(ContentObject element) => new(
            element.Guid(ContentTable.IdKey),
            element.String("n"),
            element.Integer("c", 1),
            element.Number("w"),
            element.Boolean("s"),
            element.Enum("sh", Shade.Plain),
            element.Object<Lid>("lid"),
            element.Polymorphic<IPart>("pt"),
            element.List<IPart>("pr"),
            element.String("nt"));
    }

    private sealed class LidCodec : ContentCodec<Lid>
    {
        public override ContentSchema Schema { get; } = new(nameof(Lid), [ContentField.Integer("h", "Height")]);

        public override void WriteProperties(ContentWriter writer, Lid value) => writer.Integer("h", value.Height);

        public override Lid Read(ContentObject element) => new(element.Integer("h"));
    }

    private sealed class PegCodec : ContentCodec<Peg>
    {
        public override ContentSchema Schema { get; } = new(nameof(Peg), [ContentField.Integer("ln", "Length")]);

        public override void WriteProperties(ContentWriter writer, Peg value) => writer.Integer("ln", value.Length);

        public override Peg Read(ContentObject element) => new(element.Integer("ln"));
    }

    private sealed class HookCodec : ContentCodec<Hook>
    {
        public override ContentSchema Schema { get; } = new(nameof(Hook),
        [
            ContentField.Integer("ln", "Length"),
            ContentField.Boolean("op", "Open"),
        ]);

        public override void WriteProperties(ContentWriter writer, Hook value)
        {
            writer.Integer("ln", value.Length);
            writer.Boolean("op", value.IsOpen);
        }

        public override Hook Read(ContentObject element) => new(element.Integer("ln"), element.Boolean("op"));
    }

    /// <summary>Every reference names something: the box schema holds none, and the tests that need one say so.</summary>
    public sealed class AllReferencesExist : IContentReferenceLookup
    {
        public static AllReferencesExist Instance { get; } = new();

        public bool Exists(string tableName, Guid id) => true;
    }
}
