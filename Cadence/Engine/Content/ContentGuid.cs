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

    /// <summary>A Guid that is a pure function of namespaceId and bytes: their SHA-256, marked as a name-based Guid (version 8, RFC 9562 variant).</summary>
    /// <remarks>The same namespace and bytes give the same Guid in every session; a different namespace keeps one kind of derived id from ever meeting another.</remarks>
    public static Guid Derive(Guid namespaceId, ReadOnlySpan<byte> bytes)
    {
        var buffer = new byte[16 + bytes.Length];
        namespaceId.TryWriteBytes(buffer, bigEndian: true, out _);
        bytes.CopyTo(buffer.AsSpan(16));

        Span<byte> hash = stackalloc byte[32];
        System.Security.Cryptography.SHA256.HashData(buffer, hash);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x80);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash[..16], bigEndian: true);
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
