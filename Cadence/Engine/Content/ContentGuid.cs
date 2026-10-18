namespace Engine.Content;

/// <summary>Guids in content files: 22-character base64url (the 16 bytes, '-' and '_' for '+' and '/', no padding).</summary>
public static class ContentGuid
{
    private const int EncodedLength = 22;

    public static string Format(Guid value) =>
        Convert.ToBase64String(value.ToByteArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static bool TryParse(string text, out Guid value)
    {
        value = Guid.Empty;
        if (text.Length != EncodedLength)
        {
            return false;
        }

        var base64 = text.Replace('-', '+').Replace('_', '/') + "==";
        var bytes = new byte[16];
        if (!Convert.TryFromBase64String(base64, bytes, out var written) || written != 16)
        {
            return false;
        }

        value = new Guid(bytes);
        return true;
    }
}

/// <summary>Resolves a Guid a record names to the value it means -- the loader supplies what has been loaded so far (an action's aura from the auras table).</summary>
public interface IContentReferences
{
    bool TryResolve<T>(Guid id, out T value)
        where T : class;
}

/// <summary>References that resolve nothing -- for content that names no other record.</summary>
public sealed class NoContentReferences : IContentReferences
{
    public static NoContentReferences Instance { get; } = new();

    public bool TryResolve<T>(Guid id, out T value)
        where T : class
    {
        value = null!;
        return false;
    }
}
