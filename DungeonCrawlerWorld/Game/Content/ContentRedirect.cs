using Engine.Content;

namespace Game.Content;

/// <summary>A record removed outright, and the one that now stands for it: what something saved under the old Guid resolves to.</summary>
/// <remarks>
/// For a record taken out of its table altogether; one that is only disabled names its replacement itself ("rep", a catalog
/// redirect). Read across layers like any table (GameContent.Redirects) and kept; nothing applies one until entity saves
/// exist, which are what hold old Guids.
/// </remarks>
/// <param name="Id">The removed record's Guid.</param>
/// <param name="To">The Guid it resolves to now.</param>
/// <param name="Table">The table both records belong to ("Items").</param>
public sealed record ContentRedirect(Guid Id, Guid To, string Table);

/// <summary>The codec for ContentRedirect, registered by Core.</summary>
internal sealed class ContentRedirectCodec : ContentCodec<ContentRedirect>
{
    public override ContentSchema Schema { get; } = new(nameof(ContentRedirect),
    [
        ContentField.Guid(ContentTable.IdKey, "Removed record", isRequired: true),
        ContentField.Guid("to", "Resolves to", isRequired: true),
        ContentField.Text("tb", "Table", isRequired: true),
    ]);

    public override void WriteProperties(ContentWriter writer, ContentRedirect value)
    {
        writer.Guid(ContentTable.IdKey, value.Id);
        writer.Guid("to", value.To);
        writer.String("tb", value.Table);
    }

    public override ContentRedirect Read(ContentObject element) => new(element.Guid(ContentTable.IdKey), element.Guid("to"), element.String("tb"));
}
