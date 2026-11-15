using System.Text;

namespace Engine.Content;

/// <summary>A table file held as its lines, each record line by the Guid it holds: one record's line replaced, inserted or removed, and every other line kept byte for byte.</summary>
/// <remarks>
/// For a tool that edits a table one record at a time -- the Workshop -- so a change shows in a diff as the lines it
/// changed and nothing else. The header is kept as written. A new record is inserted before the first record line whose
/// Guid comes after it in ContentGuid.FileOrder, so a table kept in that order stays in it (and one that isn't still
/// gains the line somewhere stable). A line that holds no readable Guid (blank, or broken) is kept where it is and never
/// matched. Every line is written with the line break the header was read with ("\r\n" for a file a checkout gave
/// Windows line endings, else "\n"), so a line put in matches the lines around it. The file is written whole, through a
/// temporary file beside it, so a crash mid-write never leaves half a table.
/// </remarks>
public sealed class ContentTableLines
{
    private const string LineFeed = "\n";
    private const string CarriageReturnLineFeed = "\r\n";

    private readonly List<string> _lines;
    private readonly bool _endsWithLineBreak;
    private readonly string _lineBreak;

    private ContentTableLines(string header, List<string> lines, bool endsWithLineBreak, string lineBreak)
    {
        Header = header;
        _lines = lines;
        _endsWithLineBreak = endsWithLineBreak;
        _lineBreak = lineBreak;
    }

    /// <summary>The header line, as written.</summary>
    public string Header { get; }

    /// <summary>Every line after the header, in order, without line breaks.</summary>
    public IReadOnlyList<string> Lines => _lines;

    /// <summary>An empty table: header and no records.</summary>
    public static ContentTableLines Empty(string header) => new(header, [], endsWithLineBreak: true, LineFeed);

    /// <summary>The lines of text, a whole table file: its first line is the header.</summary>
    public static ContentTableLines Parse(string text)
    {
        var endsWithLineBreak = text.EndsWith('\n');
        var lines = (endsWithLineBreak ? text[..^1] : text).Split('\n').ToList();
        var lineBreak = lines.Count > 1 && lines[0].EndsWith('\r') ? CarriageReturnLineFeed : LineFeed;
        for (var index = 0; index < lines.Count; index++)
        {
            lines[index] = lines[index].TrimEnd('\r');
        }

        var header = lines[0];
        lines.RemoveAt(0);
        return new ContentTableLines(header, lines, endsWithLineBreak, lineBreak);
    }

    /// <summary>The table file at path; one holding only header, when the file doesn't exist.</summary>
    public static ContentTableLines Read(string path, string headerIfMissing) =>
        File.Exists(path) ? Parse(File.ReadAllText(path, Encoding.UTF8)) : Empty(headerIfMissing);

    /// <summary>Whether a line holds id.</summary>
    public bool Contains(Guid id) => IndexOf(id) >= 0;

    /// <summary>The last line holding id -- the one a reader of this file takes for it; false when none does.</summary>
    public bool TryGetLine(Guid id, out string line)
    {
        var index = IndexOf(id);
        line = index >= 0 ? _lines[index] : string.Empty;
        return index >= 0;
    }

    /// <summary>Makes line the record line of id: in place of the first line holding it, every later one removed, or inserted at its place in Guid order when none does.</summary>
    /// <exception cref="ArgumentException">line doesn't hold id.</exception>
    public void Put(Guid id, string line)
    {
        if (!ContentTable.TryReadId(line, out var lineId) || lineId != id)
        {
            throw new ArgumentException($"The line doesn't hold {ContentGuid.Format(id)}.", nameof(line));
        }

        var first = -1;
        for (var index = _lines.Count - 1; index >= 0; index--)
        {
            if (!HoldsId(_lines[index], id))
            {
                continue;
            }

            if (first >= 0)
            {
                _lines.RemoveAt(first);
            }

            first = index;
        }

        if (first >= 0)
        {
            _lines[first] = line;
            return;
        }

        _lines.Insert(InsertionIndexOf(id), line);
    }

    /// <summary>Removes every line holding id; false when none did.</summary>
    public bool Remove(Guid id) => _lines.RemoveAll(line => HoldsId(line, id)) > 0;

    /// <summary>The whole file: the header, then every line, each ending in the file's line break unless the file read had none at its end.</summary>
    public string ToText()
    {
        var text = new StringBuilder(Header);
        foreach (var line in _lines)
        {
            text.Append(_lineBreak).Append(line);
        }

        if (_endsWithLineBreak)
        {
            text.Append(_lineBreak);
        }

        return text.ToString();
    }

    /// <summary>Writes the whole file to path, UTF-8 with no byte order mark, through a temporary file it then moves over path; a failure leaves path as it was and no temporary file behind.</summary>
    public void Write(string path)
    {
        var temporaryPath = path + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, ToText(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            throw;
        }
    }

    private int IndexOf(Guid id)
    {
        for (var index = _lines.Count - 1; index >= 0; index--)
        {
            if (HoldsId(_lines[index], id))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>Before the first record line whose Guid comes after id in file order; at the end when none does.</summary>
    private int InsertionIndexOf(Guid id)
    {
        for (var index = 0; index < _lines.Count; index++)
        {
            if (ContentTable.TryReadId(_lines[index], out var lineId) && ContentGuid.FileOrder.Compare(lineId, id) > 0)
            {
                return index;
            }
        }

        return _lines.Count;
    }

    private static bool HoldsId(string line, Guid id) => ContentTable.TryReadId(line, out var lineId) && lineId == id;
}
