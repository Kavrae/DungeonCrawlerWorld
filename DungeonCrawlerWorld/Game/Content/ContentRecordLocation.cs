using Engine.Content;

namespace Game.Content;

/// <summary>Where a record was read from, for a failure found after loading (a build's content checks) to point at.</summary>
/// <param name="Line">The record's line: its line in a JSON Lines table, 1 for a document of its own.</param>
/// <param name="Document">A document's position of every value, by path; null for a table's record.</param>
public sealed record ContentRecordLocation(string File, int Line, ContentSourceMap? Document = null)
{
    /// <summary>The 1-based line and column path's value starts at; the nearest enclosing line and no column when the file doesn't hold it, and the record's line when it isn't a document.</summary>
    public (int Line, int Column) PositionOf(string path)
    {
        if (Document is { } document)
        {
            return document.TryGetPosition(path, out var line, out var column)
                ? (line, column)
                : (document.LineOf(path), 0);
        }

        return (Line, 0);
    }
}

/// <summary>A record loaded from the manifest and where it was read from.</summary>
public sealed record LocatedRecord<T>(T Value, ContentRecordLocation Location);
