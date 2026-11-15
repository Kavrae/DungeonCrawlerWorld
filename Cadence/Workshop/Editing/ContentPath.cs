using System.Text;

namespace Workshop.Editing;

/// <summary>One step of a ContentPath: a field's key, or an element's index in a list.</summary>
public readonly record struct ContentPathSegment(string? Key, int Index)
{
    public bool IsIndex => Key is null;
}

/// <summary>Where a value sits in a record: the keys and list indices from the record's own object down to it ("fx[0].e[1].c").</summary>
/// <remarks>Compared by its text, which is also how it travels (an Error List line names the field it is about).</remarks>
public sealed class ContentPath : IEquatable<ContentPath>
{
    private readonly ContentPathSegment[] _segments;

    private ContentPath(ContentPathSegment[] segments)
    {
        _segments = segments;
        Text = Format(segments);
    }

    /// <summary>The record's own object.</summary>
    public static ContentPath Root { get; } = new([]);

    public IReadOnlyList<ContentPathSegment> Segments => _segments;

    /// <summary>The path as text: keys joined by '.', indices in brackets; empty for the root.</summary>
    public string Text { get; }

    public bool IsRoot => _segments.Length == 0;

    /// <summary>The path one step up; the root for a path one step long.</summary>
    public ContentPath Parent => _segments.Length == 0 ? this : new ContentPath(_segments[..^1]);

    /// <summary>The last step, which names this value in what holds it.</summary>
    public ContentPathSegment Last => _segments[^1];

    public ContentPath Key(string key) => new([.. _segments, new ContentPathSegment(key, -1)]);

    public ContentPath Element(int index) => new([.. _segments, new ContentPathSegment(null, index)]);

    /// <summary>Whether this path is ancestor itself or below it.</summary>
    public bool IsWithin(ContentPath ancestor) =>
        ancestor._segments.Length <= _segments.Length && _segments.AsSpan(0, ancestor._segments.Length).SequenceEqual(ancestor._segments);

    /// <summary>The path text names, as Text writes it; false for text that isn't one.</summary>
    public static bool TryParse(string text, out ContentPath path)
    {
        path = Root;
        var segments = new List<ContentPathSegment>();
        var index = 0;
        while (index < text.Length)
        {
            if (text[index] == '[')
            {
                var close = text.IndexOf(']', index);
                if (close < 0 || !int.TryParse(text.AsSpan(index + 1, close - index - 1), out var element))
                {
                    return false;
                }

                segments.Add(new ContentPathSegment(null, element));
                index = close + 1;
                continue;
            }

            if (text[index] == '.')
            {
                index++;
            }

            var end = index;
            while (end < text.Length && text[end] != '.' && text[end] != '[')
            {
                end++;
            }

            if (end == index)
            {
                return false;
            }

            segments.Add(new ContentPathSegment(text[index..end], -1));
            index = end;
        }

        path = new ContentPath([.. segments]);
        return true;
    }

    private static string Format(ContentPathSegment[] segments)
    {
        var text = new StringBuilder();
        foreach (var segment in segments)
        {
            if (segment.IsIndex)
            {
                text.Append('[').Append(segment.Index).Append(']');
                continue;
            }

            if (text.Length > 0)
            {
                text.Append('.');
            }

            text.Append(segment.Key);
        }

        return text.ToString();
    }

    public bool Equals(ContentPath? other) => other is not null && other.Text == Text;

    public override bool Equals(object? obj) => obj is ContentPath other && Equals(other);

    public override int GetHashCode() => Text.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => Text;
}
