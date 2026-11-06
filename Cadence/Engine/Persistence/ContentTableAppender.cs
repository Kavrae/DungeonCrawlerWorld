using System.Collections.Concurrent;
using System.Text;
using Engine.Content;

namespace Engine.Persistence;

/// <summary>Appends lines to content table files on a thread of its own, in the order they were asked for, so the caller never waits on the disk.</summary>
/// <remarks>
/// A line is queued the moment it is appended; the writer opens the file, writes, and closes it, so whatever is on disk
/// is whole up to the last line written. A file that doesn't exist yet gets its header first, and one whose last line was
/// cut short (a crash mid-write) gets a line break before the next line, so the broken line stays a line of its own that
/// the reader ignores. Flush waits for everything queued so far; Dispose flushes and stops the writer. A write that fails
/// is reported on the error output once per failure and the rest still go ahead.
/// </remarks>
public sealed class ContentTableAppender : IDisposable
{
    private static readonly UTF8Encoding Utf8WithoutMark = new(encoderShouldEmitUTF8Identifier: false);

    private readonly BlockingCollection<Action> _writes = [];
    private readonly Thread _writer;
    private bool _isDisposed;

    public ContentTableAppender()
    {
        _writer = new Thread(WriteQueued) { IsBackground = true, Name = nameof(ContentTableAppender) };
        _writer.Start();
    }

    /// <summary>Queues line for the end of path, after header when the file doesn't exist yet.</summary>
    public void Append(string path, string header, string line) => Enqueue(() => AppendNow(path, header, line));

    /// <summary>Queues taking every record line holding id out of path, keeping the header and every other line as written.</summary>
    /// <remarks>The file is rewritten, so the lines after them move: never for a file a content layer is read from while a session runs, whose index holds where each line is.</remarks>
    public void RemoveRecords(string path, Guid id) => Enqueue(() => RemoveLinesNow(path, id));

    /// <summary>Waits until everything queued before this call is on disk.</summary>
    public void Flush()
    {
        if (_isDisposed)
        {
            return;
        }

        using var written = new ManualResetEventSlim();
        Enqueue(written.Set);
        written.Wait();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _writes.CompleteAdding();
        _writer.Join();
        _writes.Dispose();
    }

    private void Enqueue(Action write)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _writes.Add(write);
    }

    private void WriteQueued()
    {
        foreach (var write in _writes.GetConsumingEnumerable())
        {
            try
            {
                write();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"[Save] A content write failed: {exception.Message}");
            }
        }
    }

    private static void AppendNow(string path, string header, string line)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        var text = new StringBuilder();
        if (stream.Length == 0)
        {
            text.Append(header).Append('\n');
        }
        else
        {
            stream.Seek(-1, SeekOrigin.End);
            if (stream.ReadByte() != '\n')
            {
                text.Append('\n');
            }
        }

        text.Append(line).Append('\n');
        stream.Seek(0, SeekOrigin.End);
        stream.Write(Utf8WithoutMark.GetBytes(text.ToString()));
    }

    private static void RemoveLinesNow(string path, Guid id)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var lines = File.ReadAllText(path, Encoding.UTF8).Split('\n');
        var kept = new StringBuilder();
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].TrimEnd('\r');
            if (index == lines.Length - 1 && line.Length == 0)
            {
                break;
            }

            if (index > 0 && ContentTable.TryReadId(line, out var lineId) && lineId == id)
            {
                continue;
            }

            kept.Append(line).Append('\n');
        }

        var replacement = path + ".tmp";
        File.WriteAllText(replacement, kept.ToString(), Utf8WithoutMark);
        File.Move(replacement, path, overwrite: true);
    }
}
