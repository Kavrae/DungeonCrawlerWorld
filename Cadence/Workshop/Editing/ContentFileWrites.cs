using System.Text;

namespace Workshop.Editing;

/// <summary>The files one table's save writes or deletes, written together: every new text staged in a temporary file beside its target first, then each moved over its target.</summary>
/// <remarks>
/// A save stages every table's writes before it moves any, so a disk that refuses a write changes nothing at all; a
/// move that fails leaves its target as it was and takes back every temporary file still waiting. A temporary file
/// is never left behind.
/// </remarks>
public sealed class ContentFileWrites
{
    private const string TemporarySuffix = ".tmp";

    private readonly List<(string Path, string? Text)> _writes = [];
    private readonly List<string> _staged = [];

    public bool IsEmpty => _writes.Count == 0;

    /// <summary>Writes text to path, UTF-8 with no byte order mark.</summary>
    public void Write(string path, string text) => _writes.Add((path, text));

    /// <summary>Deletes path, when it exists.</summary>
    public void Delete(string path) => _writes.Add((path, null));

    /// <summary>Writes every text to its temporary file; on a failure, takes back what it staged and rethrows.</summary>
    public void Stage()
    {
        try
        {
            foreach (var (path, text) in _writes)
            {
                if (text is null)
                {
                    continue;
                }

                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                var temporaryPath = path + TemporarySuffix;
                _staged.Add(temporaryPath);
                File.WriteAllText(temporaryPath, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }
        catch
        {
            Discard();
            throw;
        }
    }

    /// <summary>Moves each staged file over its target and deletes each deleted one, in order; on a failure, takes back the temporary files not yet moved and rethrows.</summary>
    public void Commit()
    {
        try
        {
            foreach (var (path, text) in _writes)
            {
                if (text is null)
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }

                    continue;
                }

                var temporaryPath = path + TemporarySuffix;
                File.Move(temporaryPath, path, overwrite: true);
                _staged.Remove(temporaryPath);
            }
        }
        catch
        {
            Discard();
            throw;
        }
    }

    /// <summary>Deletes every temporary file staged and not moved.</summary>
    public void Discard()
    {
        foreach (var temporaryPath in _staged)
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        _staged.Clear();
    }
}
