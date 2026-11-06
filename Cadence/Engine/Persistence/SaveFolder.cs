using System.Globalization;
using Engine.Content;

namespace Engine.Persistence;

/// <summary>One game's save: a folder holding save.json, the content tables the game created or overrode (Definitions, a Save content layer), and its drafts (Drafts, the same tables, never a layer of the game's).</summary>
/// <remarks>
/// The master and every mod are layered under Definitions and never copied into it. Everything written to the folder goes
/// through Writer, one queue in the order asked for; disposing the folder flushes it and stops it.
/// </remarks>
public sealed class SaveFolder : IDisposable
{
    public const string HeaderFileName = "save.json";
    public const string DefinitionsDirectoryName = "Definitions";
    public const string DraftsDirectoryName = "Drafts";

    private SaveFolder(string name, string directory)
    {
        Name = name;
        Directory = directory;
        DefinitionsLayer = ContentLayer.FromDirectory($"Save {name}", ContentLayerKind.Save, DefinitionsDirectory);
        System.IO.Directory.CreateDirectory(DefinitionsDirectory);
        System.IO.Directory.CreateDirectory(DraftsDirectory);
        ReadHeader();
    }

    public string Name { get; }

    public string Directory { get; }

    public string HeaderPath => Path.Combine(Directory, HeaderFileName);

    public string DefinitionsDirectory => Path.Combine(Directory, DefinitionsDirectoryName);

    public string DraftsDirectory => Path.Combine(Directory, DraftsDirectoryName);

    /// <summary>The Definitions folder as the Save layer of a content stack, named "Save " and the save's name.</summary>
    public ContentLayer DefinitionsLayer { get; }

    /// <summary>What save.json holds; null while it doesn't exist (a new save) or can't be read (HeaderProblem).</summary>
    public SaveHeader? Header { get; private set; }

    /// <summary>Why save.json couldn't be read; null when it was, or doesn't exist.</summary>
    public string? HeaderProblem { get; private set; }

    /// <summary>Writes every table line and draft the game makes, in order.</summary>
    public ContentTableAppender Writer { get; } = new();

    /// <summary>The folder name a new game's save gets: when it started, then its seed ("20261103-221904-seed42").</summary>
    public static string NewGameName(DateTime startedAt, int seed) =>
        $"{startedAt.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-seed{seed.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Whether name can be a save's folder name: not empty, nothing a file name can't hold, not ending in a dot or a space (which Windows drops), and not a device name ("CON", "NUL", "COM1", with or without an extension).</summary>
    public static bool IsValidName(string name) =>
        name.Trim().Length > 0
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && !name.EndsWith('.') && !name.EndsWith(' ')
        && !ReservedDeviceNames.Contains(name.Split('.')[0].TrimEnd());

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>The save named name under savesDirectory, its folders created if they don't exist yet.</summary>
    /// <exception cref="ArgumentException">name isn't a valid save name (IsValidName).</exception>
    public static SaveFolder OpenOrCreate(string savesDirectory, string name)
    {
        if (!IsValidName(name))
        {
            throw new ArgumentException($"'{name}' can't be a save's name.", nameof(name));
        }

        return new SaveFolder(name, Path.Combine(savesDirectory, name));
    }

    /// <summary>Writes header as save.json, at once; for a save with none yet.</summary>
    public void WriteHeader(SaveHeader header)
    {
        File.WriteAllText(HeaderPath, header.Write(), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Header = header;
        HeaderProblem = null;
    }

    /// <summary>Compacts every table of the save's definitions (ContentTableCompaction) once what is queued is written: part of StartupCleanup, before anything reads the Definitions layer.</summary>
    /// <param name="problems">Gets one line per table that couldn't be rewritten, which is left as it is.</param>
    /// <returns>How many lines were dropped, every table together.</returns>
    public int CompactDefinitions(List<string> problems)
    {
        Writer.Flush();
        var dropped = 0;
        foreach (var path in System.IO.Directory.GetFiles(DefinitionsDirectory, "*.jsonl"))
        {
            try
            {
                dropped += ContentTableCompaction.Compact(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                problems.Add($"{path} couldn't be compacted, so it is left as it is: {exception.Message}");
            }
        }

        return dropped;
    }

    /// <summary>Where table fileName ("Actions.jsonl") is kept among the save's definitions.</summary>
    public string DefinitionsPathOf(string fileName) => Path.Combine(DefinitionsDirectory, fileName);

    /// <summary>Where table fileName is kept among the save's drafts.</summary>
    public string DraftsPathOf(string fileName) => Path.Combine(DraftsDirectory, fileName);

    public void Dispose() => Writer.Dispose();

    private void ReadHeader()
    {
        if (!File.Exists(HeaderPath))
        {
            return;
        }

        if (SaveHeader.TryRead(File.ReadAllText(HeaderPath), out var header, out var problem))
        {
            Header = header;
        }
        else
        {
            HeaderProblem = problem;
        }
    }
}
