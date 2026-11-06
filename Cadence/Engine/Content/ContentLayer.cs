namespace Engine.Content;

/// <summary>What a content layer is, which decides what its records may do and what a failing one costs.</summary>
/// <remarks>Layers stack in this order: master, then mods, then a save, then an IDE's drafts on top.</remarks>
public enum ContentLayerKind
{
    /// <summary>The shipped content: may add and disable records, and must load whole.</summary>
    Master,

    /// <summary>A mod's content: may add, replace and disable records; a failing record drops the mod.</summary>
    Mod,

    /// <summary>One game's content: may add records and override lower ones, and remove its overrides ("rm"), but never disable; a failing record is left out.</summary>
    Save,

    /// <summary>An IDE's unsaved work, over everything else, for its own builds only -- the game never has one.</summary>
    Drafts,
}

/// <summary>One named source of content tables and documents -- a directory, or for a test text held in memory -- and its kind.</summary>
/// <remarks>Paths inside a layer are relative and '/'-separated ("Items.jsonl", "Areas/starting-area.json"), the same for both sources.</remarks>
public sealed class ContentLayer
{
    private readonly string? _directory;
    private readonly IReadOnlyDictionary<string, byte[]>? _bytesByPath;

    private ContentLayer(string name, ContentLayerKind kind, string? directory, IReadOnlyDictionary<string, string>? textByPath)
    {
        Name = name;
        Kind = kind;
        _directory = directory;
        _bytesByPath = textByPath?.ToDictionary(pair => pair.Key, pair => System.Text.Encoding.UTF8.GetBytes(pair.Value), StringComparer.Ordinal);
    }

    /// <summary>What failures and startup lines call the layer ("Master", a mod's name, a save's).</summary>
    public string Name { get; }

    public ContentLayerKind Kind { get; }

    /// <summary>The folder the layer reads, or null for one held in memory.</summary>
    public string? Directory => _directory;

    /// <summary>The layer under directory; a file it lacks is an empty table.</summary>
    public static ContentLayer FromDirectory(string name, ContentLayerKind kind, string directory) => new(name, kind, directory, textByPath: null);

    /// <summary>A layer of the files textByPath holds, each by its relative path, kept as its UTF-8 bytes -- so a record read by id is sliced from them, never re-encoded with the whole file.</summary>
    public static ContentLayer FromText(string name, ContentLayerKind kind, IReadOnlyDictionary<string, string> textByPath) => new(name, kind, directory: null, textByPath);

    /// <summary>The whole of the file at path as UTF-8 bytes; false when the layer has no such file.</summary>
    /// <remarks>For a layer held in memory these are the layer's own bytes, not a copy: read them, never write to them.</remarks>
    public bool TryReadBytes(string path, out byte[] bytes)
    {
        if (_bytesByPath is not null)
        {
            var found = _bytesByPath.TryGetValue(path, out var held);
            bytes = found ? held! : [];
            return found;
        }

        var fullPath = Path.Combine(_directory!, path);
        if (!File.Exists(fullPath))
        {
            bytes = [];
            return false;
        }

        bytes = File.ReadAllBytes(fullPath);
        return true;
    }

    /// <summary>length bytes of the file at path from offset: one line a table index found there.</summary>
    /// <remarks>A file only ever grows at its end while a session reads it (a save's tables are appended to), so an offset the index took still names the same line.</remarks>
    public byte[] ReadBytes(string path, long offset, int length)
    {
        var bytes = new byte[length];
        if (_bytesByPath is not null)
        {
            _bytesByPath[path].AsSpan((int)offset, length).CopyTo(bytes);
            return bytes;
        }

        using var stream = new FileStream(Path.Combine(_directory!, path), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        stream.Seek(offset, SeekOrigin.Begin);
        stream.ReadExactly(bytes);
        return bytes;
    }

    /// <summary>The whole text of the file at path; false when the layer has no such file.</summary>
    public bool TryReadText(string path, out string text)
    {
        if (_bytesByPath is not null)
        {
            var found = _bytesByPath.TryGetValue(path, out var held);
            text = found ? System.Text.Encoding.UTF8.GetString(held!) : string.Empty;
            return found;
        }

        var fullPath = Path.Combine(_directory!, path);
        if (!File.Exists(fullPath))
        {
            text = string.Empty;
            return false;
        }

        text = File.ReadAllText(fullPath, System.Text.Encoding.UTF8);
        return true;
    }

    /// <summary>The relative path of every file directly in folder whose name ends in extension (".json"), in ordinal order.</summary>
    public IReadOnlyList<string> FilesIn(string folder, string extension)
    {
        List<string> paths;
        if (_bytesByPath is not null)
        {
            var prefix = $"{folder}/";
            paths = [.. _bytesByPath.Keys.Where(path => path.StartsWith(prefix, StringComparison.Ordinal) && !path[prefix.Length..].Contains('/') && path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))];
        }
        else
        {
            var fullFolder = Path.Combine(_directory!, folder);
            paths = System.IO.Directory.Exists(fullFolder)
                ? [.. System.IO.Directory.GetFiles(fullFolder, $"*{extension}").Select(path => $"{folder}/{Path.GetFileName(path)}")]
                : [];
        }

        paths.Sort(StringComparer.Ordinal);
        return paths;
    }

    /// <summary>A SHA-256 of every file the layer holds, folders included, by relative path in ordinal order, so the same files give the same hash wherever they are.</summary>
    /// <returns>64 lowercase hex digits.</returns>
    public string ComputeContentHash()
    {
        IEnumerable<(string Path, byte[] Bytes)> files;
        if (_bytesByPath is not null)
        {
            files = _bytesByPath.Select(pair => (pair.Key, pair.Value));
        }
        else
        {
            files = System.IO.Directory.Exists(_directory)
                ? System.IO.Directory.GetFiles(_directory!, "*", SearchOption.AllDirectories)
                    .Select(fullPath => (Path.GetRelativePath(_directory!, fullPath).Replace('\\', '/'), File.ReadAllBytes(fullPath)))
                : [];
        }

        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        foreach (var (path, bytes) in files.OrderBy(file => file.Path, StringComparer.Ordinal))
        {
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes(path));
            hash.AppendData([0]);
            hash.AppendData(BitConverter.GetBytes(bytes.LongLength));
            hash.AppendData(bytes);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    public override string ToString() => $"{Kind} {Name}";
}
