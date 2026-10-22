using System.Text;
using System.Text.Json;

namespace Engine.Content;

/// <summary>A record that couldn't be loaded, and why -- reported, never thrown past the loader, like a ModuleFailure.</summary>
/// <param name="Line">1-based line in the file; 0 for a failure of the whole file.</param>
/// <param name="RecordId">The record's Guid, when it could be read.</param>
/// <param name="Column">1-based column in Line; 0 when the failure is the whole line's.</param>
public sealed record ContentFailure(string File, int Line, Guid? RecordId, string Reason, int Column = 0)
{
    /// <summary>File:line, with :column when there is one, or just the file for a failure of the whole file.</summary>
    public string Location => Line <= 0 ? File : Column > 0 ? $"{File}:{Line}:{Column}" : $"{File}:{Line}";

    public override string ToString() =>
        RecordId is { } id
            ? $"{Location} ({ContentGuid.Format(id)}): {Reason}"
            : $"{Location}: {Reason}";
}

/// <summary>A record decoded from a table: its value, or that it is disabled (and what replaces it, if anything).</summary>
public sealed record ContentTableRecord<T>(int Line, Guid Id, T? Value, bool IsDisabled, Guid? Replacement)
    where T : class;

/// <summary>One table of a manifest as a JSON Lines file: a header line naming the format and the table, then one compact record per line.</summary>
/// <remarks>
/// Each record line is the record's own object with its "id" first, plus two table-level fields a codec never sees:
/// "off": true when the record is disabled (not loaded at all), and "rep", the Guid that replaces a disabled one. A
/// disabled record keeps its whole definition, so it can be enabled again, but is not decoded. A last line that won't
/// parse is a write cut short (a crash mid-append) and is ignored; any other bad line is a ContentFailure for that
/// record only, and the rest of the file still loads.
/// </remarks>
public static class ContentTable
{
    public const int FormatVersion = 1;
    public const string IdKey = "id";
    public const string DisabledKey = "off";
    public const string ReplacementKey = "rep";

    public const string FormatKey = "fmt";
    public const string TableKey = "table";

    private static readonly IReadOnlySet<string> EnvelopeKeys = new HashSet<string>(StringComparer.Ordinal) { DisabledKey, ReplacementKey };

    /// <summary>Reads every record of path, which must be a file of table tableName; a missing file is an empty table.</summary>
    public static IReadOnlyList<ContentTableRecord<T>> Read<T>(string path, string tableName, ContentTypeRegistry types, IContentReferences references, List<ContentFailure> failures)
        where T : class
    {
        if (!File.Exists(path))
        {
            return [];
        }

        return Read<T>(File.ReadAllText(path, Encoding.UTF8), Path.GetFileName(path), tableName, types, references, failures);
    }

    /// <summary>Reads every record of text, a whole table file; fileName names it in failures.</summary>
    public static IReadOnlyList<ContentTableRecord<T>> Read<T>(string text, string fileName, string tableName, ContentTypeRegistry types, IContentReferences references, List<ContentFailure> failures)
        where T : class
    {
        var lines = text.Split('\n');
        var lastLineIsComplete = text.EndsWith('\n');
        var records = new List<ContentTableRecord<T>>();

        var headerIndex = Array.FindIndex(lines, line => line.Trim().Length > 0);
        if (headerIndex < 0)
        {
            return records;
        }

        if (!TryReadHeader(lines[headerIndex], tableName, out var headerProblem))
        {
            failures.Add(new ContentFailure(fileName, headerIndex + 1, null, headerProblem));
            return records;
        }

        for (var index = headerIndex + 1; index < lines.Length; index++)
        {
            var line = lines[index].TrimEnd('\r');
            if (line.Trim().Length == 0)
            {
                continue;
            }

            var isLastLine = index == lines.Length - 1;
            var lineNumber = index + 1;
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException exception)
            {
                if (isLastLine && !lastLineIsComplete)
                {
                    continue;
                }

                failures.Add(new ContentFailure(fileName, lineNumber, null, $"Not valid JSON: {exception.Message}"));
                continue;
            }

            using (document)
            {
                if (TryReadRecord<T>(document.RootElement, fileName, lineNumber, types, references, failures) is { } record)
                {
                    records.Add(record);
                }
            }
        }

        return records;
    }

    /// <summary>The header line for table tableName.</summary>
    public static string Header(string tableName) => $"{{\"{FormatKey}\":{FormatVersion},\"{TableKey}\":\"{tableName}\"}}";

    /// <summary>A whole table file: the header, then each record on its own line, every line ending in '\n'.</summary>
    public static string Write<T>(string tableName, IEnumerable<T> records, ContentTypeRegistry types)
        where T : notnull
    {
        var text = new StringBuilder();
        text.Append(Header(tableName)).Append('\n');
        foreach (var record in records)
        {
            text.Append(Encoding.UTF8.GetString(ContentWriter.WriteRecord(types, record))).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>A disabled record's line: its definition as written, plus "off" and, when something replaces it, "rep".</summary>
    public static string WriteDisabled<T>(T record, Guid? replacement, ContentTypeRegistry types)
        where T : notnull =>
        Encoding.UTF8.GetString(ContentWriter.WriteRecord(types, record, envelope: json =>
        {
            json.WriteBoolean(DisabledKey, true);
            if (replacement is { } replacementId)
            {
                json.WriteString(ReplacementKey, ContentGuid.Format(replacementId));
            }
        }));

    private static bool TryReadHeader(string line, string tableName, out string problem)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            return TryReadHeader(document.RootElement, tableName, out problem);
        }
        catch (JsonException exception)
        {
            problem = $"The header isn't valid JSON: {exception.Message}";
            return false;
        }
    }

    /// <summary>Whether header holds this format's version and names table tableName.</summary>
    internal static bool TryReadHeader(JsonElement header, string tableName, out string problem)
    {
        if (header.ValueKind != JsonValueKind.Object
            || !header.TryGetProperty(FormatKey, out var format) || format.ValueKind != JsonValueKind.Number || !format.TryGetInt32(out var version) || version != FormatVersion)
        {
            problem = $"The header must say \"{FormatKey}\":{FormatVersion}.";
            return false;
        }

        if (!header.TryGetProperty(TableKey, out var table) || table.ValueKind != JsonValueKind.String || table.GetString() != tableName)
        {
            problem = $"The header must name table \"{tableName}\".";
            return false;
        }

        problem = string.Empty;
        return true;
    }

    private static ContentTableRecord<T>? TryReadRecord<T>(JsonElement element, string fileName, int lineNumber, ContentTypeRegistry types, IContentReferences references, List<ContentFailure> failures)
        where T : class =>
        TryReadRecord<T>(element, fileName, lineNumber, types, references, failures, EnvelopeKeys);

    /// <summary>One record: its id, then disabled (with its replacement) or its value through T's codec; a failure is added to failures and null returned.</summary>
    /// <param name="envelopeKeys">Keys allowed beside the codec's own -- the disabled flag and replacement, and a document's header.</param>
    internal static ContentTableRecord<T>? TryReadRecord<T>(JsonElement element, string fileName, int lineNumber, ContentTypeRegistry types, IContentReferences references, List<ContentFailure> failures, IReadOnlySet<string> envelopeKeys)
        where T : class
    {
        Guid? id = null;
        try
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(IdKey, out var idElement) || idElement.ValueKind != JsonValueKind.String
                || !ContentGuid.TryParse(idElement.GetString()!, out var parsedId))
            {
                failures.Add(new ContentFailure(fileName, lineNumber, null, $"A record needs an \"{IdKey}\"."));
                return null;
            }

            id = parsedId;
            var isDisabled = element.TryGetProperty(DisabledKey, out var disabled) && disabled.ValueKind == JsonValueKind.True;
            Guid? replacement = null;
            if (element.TryGetProperty(ReplacementKey, out var replacementElement))
            {
                replacement = replacementElement.ValueKind == JsonValueKind.String && ContentGuid.TryParse(replacementElement.GetString()!, out var replacementId)
                    ? replacementId
                    : throw new ContentFormatException($"\"{ReplacementKey}\" must be a Guid.");
            }

            if (isDisabled)
            {
                return new ContentTableRecord<T>(lineNumber, parsedId, null, IsDisabled: true, replacement);
            }

            if (replacement is not null)
            {
                throw new ContentFormatException($"Only a disabled record has a \"{ReplacementKey}\".");
            }

            var value = ContentObject.Read<T>(element, types, references, envelopeKeys);
            return new ContentTableRecord<T>(lineNumber, parsedId, value, IsDisabled: false, Replacement: null);
        }
        catch (ContentFormatException exception)
        {
            failures.Add(new ContentFailure(fileName, lineNumber, id, exception.Message));
            return null;
        }
    }
}
