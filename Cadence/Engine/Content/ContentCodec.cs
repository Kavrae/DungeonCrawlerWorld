namespace Engine.Content;

/// <summary>Reads and writes one type of content value as a JSON object, and describes its fields.</summary>
/// <remarks>
/// A codec writes only its own properties: ContentWriter opens and closes the object, and writes a polymorphic value's
/// discriminator before them. Every key a codec writes must be one its Schema declares (ContentWriter refuses any other),
/// and a key read that the schema doesn't declare fails the record (ContentReader), so the schema is always the whole of
/// what the codec reads and writes. A field at its default is omitted on write and means the default on read.
/// </remarks>
public interface IContentCodec
{
    Type ValueType { get; }

    ContentSchema Schema { get; }

    void WriteProperties(ContentWriter writer, object value);

    object Read(ContentObject element);
}

/// <summary>A codec for values of T.</summary>
public abstract class ContentCodec<T> : IContentCodec
    where T : notnull
{
    public Type ValueType => typeof(T);

    public abstract ContentSchema Schema { get; }

    public abstract void WriteProperties(ContentWriter writer, T value);

    public abstract T Read(ContentObject element);

    void IContentCodec.WriteProperties(ContentWriter writer, object value) => WriteProperties(writer, (T)value);

    object IContentCodec.Read(ContentObject element) => Read(element);
}

/// <summary>A record that can't be read: a missing or malformed field, an unknown key or discriminator, a reference to nothing, a tag outside its field's domain.</summary>
public sealed class ContentFormatException(string message) : Exception(message)
{
    /// <summary>A failure at one value: the key that holds it, the value's JSON text and the JSON text of the object the key is in, which the record's reader finds in its source to name the column.</summary>
    public ContentFormatException(string message, string key, string valueText, string objectText)
        : this(message)
    {
        Key = key;
        ValueText = valueText;
        ObjectText = objectText;
    }

    /// <summary>The JSON text of the object whose key holds the value -- the record, or an object nested in it; null when Key is.</summary>
    public string? ObjectText { get; }

    /// <summary>The key holding the value the failure is about; null when the failure is the record's as a whole.</summary>
    public string? Key { get; }

    /// <summary>The JSON text of the value the failure is about, such as "\"Area.Testing\""; null when Key is.</summary>
    public string? ValueText { get; }
}
