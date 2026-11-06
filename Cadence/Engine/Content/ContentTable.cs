using System.Text;
using System.Text.Json;
using Engine.Json;

namespace Engine.Content;

/// <summary>A record that couldn't be loaded, and why -- reported, never thrown past the loader, like a ModuleFailure.</summary>
/// <param name="Line">1-based line in the file; 0 for a failure of the whole file.</param>
/// <param name="RecordId">The record's Guid, when it could be read.</param>
/// <param name="Column">1-based column in Line; 0 when the failure is the whole line's.</param>
/// <param name="Layer">The name of the content layer File was read from (ContentLayers); null when it wasn't read through one.</param>
public sealed record ContentFailure(string File, int Line, Guid? RecordId, string Reason, int Column = 0, string? Layer = null)
{
    /// <summary>File:line, with :column when there is one, or just the file for a failure of the whole file.</summary>
    public string Location => Line <= 0 ? File : Column > 0 ? $"{File}:{Line}:{Column}" : $"{File}:{Line}";

    public override string ToString()
    {
        var layer = Layer is null ? string.Empty : $"[{Layer}] ";
        return RecordId is { } id
            ? $"{layer}{Location} ({ContentGuid.Format(id)}): {Reason}"
            : $"{layer}{Location}: {Reason}";
    }
}

/// <summary>A record decoded from a table: its value, or that it is disabled (and what replaces it, if anything), or that it removes a save's override of its Guid.</summary>
public sealed record ContentTableRecord<T>(int Line, Guid Id, T? Value, bool IsDisabled, Guid? Replacement, bool IsRemoval = false)
    where T : class;

/// <summary>Every record a table file holds, and the content version its header gives each module (ContentTable.VersionsKey).</summary>
public sealed record ContentTableFile<T>(IReadOnlyList<ContentTableRecord<T>> Records, IReadOnlyDictionary<string, int> ModuleVersions)
    where T : class
{
    public static ContentTableFile<T> Empty { get; } = new([], new Dictionary<string, int>());

    /// <summary>The content version the header gives moduleName: the default when it names none.</summary>
    public int VersionOf(string moduleName) => ModuleVersions.GetValueOrDefault(moduleName, ContentTypeRegistry.DefaultContentVersion);
}

/// <summary>One line of a table file as ContentTable.ReadIndex found it: its id and envelope, where its bytes are, and the summary of an enabled record (default for any other).</summary>
/// <param name="Offset">Where the line's bytes start in the file.</param>
/// <param name="Length">How many bytes the line is, without its line break.</param>
public sealed record ContentTableIndexLine<TSummary>(int Line, Guid Id, bool IsDisabled, Guid? Replacement, bool IsRemoval, long Offset, int Length, TSummary? Summary);

/// <summary>Every line of a table file as ContentTable.ReadIndex found it, and the content version its header gives each module.</summary>
public sealed record ContentTableIndexFile<TSummary>(IReadOnlyList<ContentTableIndexLine<TSummary>> Lines, IReadOnlyDictionary<string, int> ModuleVersions);

/// <summary>One table of a manifest as a JSON Lines file: a header line naming the format and the table, then one compact record per line.</summary>
/// <remarks>
/// Each record line is the record's own object with its "id" first, plus table-level fields a codec never sees: "off":
/// true when the record is disabled (not loaded at all), "rep", the Guid that replaces a disabled one, and "rm": true on
/// a line holding nothing but an id, which takes a save's override of that Guid away (only a save layer may hold one --
/// ContentLayers refuses it anywhere else). A disabled record keeps its whole definition, so it can be enabled again,
/// but is not decoded. The header may carry "ver", the content version of each module whose codecs wrote the records;
/// a module at the default version is left out, so a missing one reads as the default. A last line that won't parse
/// is a write cut short (a crash mid-append) and is ignored; any other bad line is a ContentFailure for that record
/// only, and the rest of the file still loads.
/// </remarks>
public static class ContentTable
{
    public const int FormatVersion = 1;
    public const string IdKey = "id";
    public const string DisabledKey = "off";
    public const string ReplacementKey = "rep";
    public const string RemovalKey = "rm";

    public const string FormatKey = "fmt";
    public const string TableKey = "table";
    public const string VersionsKey = "ver";

    private static readonly IReadOnlySet<string> EnvelopeKeys = new HashSet<string>(StringComparer.Ordinal) { DisabledKey, ReplacementKey };

    /// <summary>Reads every record of path, which must be a file of table tableName; a missing file is an empty table.</summary>
    public static ContentTableFile<T> Read<T>(string path, string tableName, ContentTypeRegistry types, IContentReferences references, List<ContentFailure> failures)
        where T : class
    {
        if (!File.Exists(path))
        {
            return ContentTableFile<T>.Empty;
        }

        return Read<T>(File.ReadAllText(path, Encoding.UTF8), Path.GetFileName(path), tableName, types, references, failures);
    }

    /// <summary>Reads every record of text, a whole table file; fileName names it in failures.</summary>
    public static ContentTableFile<T> Read<T>(string text, string fileName, string tableName, ContentTypeRegistry types, IContentReferences references, List<ContentFailure> failures)
        where T : class
    {
        var records = new List<ContentTableRecord<T>>();
        var moduleVersions = ForEachRecordLine(Encoding.UTF8.GetBytes(text), fileName, tableName, failures, (line, element) =>
        {
            if (TryReadRecord<T>(element, fileName, line.Number, types, references, failures) is { } record)
            {
                records.Add(record);
            }
        });

        return new ContentTableFile<T>(records, moduleVersions);
    }

    /// <summary>Every line of bytes, a whole table file, as an index entry -- its id, envelope, where it sits in bytes and summarize's summary of an enabled record -- without decoding any record; fileName names it in failures.</summary>
    /// <remarks>
    /// The same line rules Read applies short of decoding: a line that isn't JSON, has no id, or holds a malformed envelope is a
    /// failure, and a truncated last line is ignored. A record that would fail its codec is only found when ReadLine decodes it.
    /// </remarks>
    /// <param name="summarize">What a whole-catalog question needs of one enabled record, read from its raw JSON object.</param>
    public static ContentTableIndexFile<TSummary> ReadIndex<TSummary>(byte[] bytes, string fileName, string tableName, Func<JsonElement, TSummary> summarize, List<ContentFailure> failures)
    {
        var lines = new List<ContentTableIndexLine<TSummary>>();
        var moduleVersions = ForEachRecordLine(bytes, fileName, tableName, failures, (line, element) =>
        {
            if (TryIndexLine(element, fileName, line.Number, line.Offset, line.Length, summarize, failures) is { } indexLine)
            {
                lines.Add(indexLine);
            }
        });

        return new ContentTableIndexFile<TSummary>(lines, moduleVersions);
    }

    /// <summary>Reads bytes as a table file (JsonLines): checks its header names table tableName, then hands visit each record line; returns the header's module versions.</summary>
    /// <remarks>A header that isn't JSON or isn't this table's is a failure, and the file then holds no records.</remarks>
    private static IReadOnlyDictionary<string, int> ForEachRecordLine(byte[] bytes, string fileName, string tableName, List<ContentFailure> failures, Action<JsonLine, JsonElement> visit)
    {
        IReadOnlyDictionary<string, int> moduleVersions = new Dictionary<string, int>();
        var headerRead = false;
        foreach (var result in JsonLines.Read(bytes))
        {
            if (result.Document is not { } document)
            {
                failures.Add(new ContentFailure(fileName, result.Line.Number, null, headerRead ? $"Not valid JSON: {result.Problem}" : $"The header isn't valid JSON: {result.Problem}"));
                if (!headerRead)
                {
                    return moduleVersions;
                }

                continue;
            }

            using (document)
            {
                if (headerRead)
                {
                    visit(result.Line, document.RootElement);
                    continue;
                }

                if (!TryReadHeader(document.RootElement, tableName, out moduleVersions, out var headerProblem))
                {
                    failures.Add(new ContentFailure(fileName, result.Line.Number, null, headerProblem));
                    return new Dictionary<string, int>();
                }

                headerRead = true;
            }
        }

        return moduleVersions;
    }

    /// <summary>The record line (one line of a table file, as ReadIndex found it) decodes to through T's codec; null, with a failure added, when it doesn't or isn't an enabled record.</summary>
    public static T? ReadLine<T>(ReadOnlyMemory<byte> line, string fileName, int lineNumber, ContentTypeRegistry types, IContentReferences references, List<ContentFailure> failures)
        where T : class
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException exception)
        {
            failures.Add(new ContentFailure(fileName, lineNumber, null, $"Not valid JSON: {exception.Message}"));
            return null;
        }

        using (document)
        {
            var record = TryReadRecord<T>(document.RootElement, fileName, lineNumber, types, references, failures);
            if (record is { Value: not null })
            {
                return record.Value;
            }

            if (record is not null)
            {
                failures.Add(new ContentFailure(fileName, lineNumber, record.Id, "The line is no longer an enabled record."));
            }

            return null;
        }
    }

    private static ContentTableIndexLine<TSummary>? TryIndexLine<TSummary>(JsonElement element, string fileName, int lineNumber, long offset, int length, Func<JsonElement, TSummary> summarize, List<ContentFailure> failures)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(IdKey, out var idElement) || idElement.ValueKind != JsonValueKind.String
            || !ContentGuid.TryParse(idElement.GetString()!, out var id))
        {
            failures.Add(new ContentFailure(fileName, lineNumber, null, $"A record needs an \"{IdKey}\"."));
            return null;
        }

        if (element.TryGetProperty(RemovalKey, out var removal))
        {
            if (removal.ValueKind != JsonValueKind.True || element.EnumerateObject().Any(property => property.Name != IdKey && property.Name != RemovalKey))
            {
                failures.Add(new ContentFailure(fileName, lineNumber, id, $"A removal line holds only \"{IdKey}\" and \"{RemovalKey}\": true."));
                return null;
            }

            return new ContentTableIndexLine<TSummary>(lineNumber, id, IsDisabled: false, Replacement: null, IsRemoval: true, offset, length, default);
        }

        var isDisabled = element.TryGetProperty(DisabledKey, out var disabled) && disabled.ValueKind == JsonValueKind.True;
        Guid? replacement = null;
        if (element.TryGetProperty(ReplacementKey, out var replacementElement))
        {
            if (replacementElement.ValueKind != JsonValueKind.String || !ContentGuid.TryParse(replacementElement.GetString()!, out var replacementId))
            {
                failures.Add(new ContentFailure(fileName, lineNumber, id, $"\"{ReplacementKey}\" must be a Guid."));
                return null;
            }

            if (!isDisabled)
            {
                failures.Add(new ContentFailure(fileName, lineNumber, id, $"Only a disabled record has a \"{ReplacementKey}\"."));
                return null;
            }

            replacement = replacementId;
        }

        return new ContentTableIndexLine<TSummary>(lineNumber, id, isDisabled, replacement, IsRemoval: false, offset, length, isDisabled ? default : summarize(element));
    }

    /// <summary>The header line for table tableName, naming each module of moduleVersions above the default version, in name order.</summary>
    public static string Header(string tableName, IReadOnlyDictionary<string, int>? moduleVersions = null)
    {
        var header = new StringBuilder($"{{\"{FormatKey}\":{FormatVersion},\"{TableKey}\":\"{tableName}\"");
        var versioned = (moduleVersions ?? new Dictionary<string, int>())
            .Where(version => version.Value != ContentTypeRegistry.DefaultContentVersion)
            .OrderBy(version => version.Key, StringComparer.Ordinal)
            .ToList();
        if (versioned.Count > 0)
        {
            header.Append($",\"{VersionsKey}\":{{");
            header.Append(string.Join(",", versioned.Select(version => $"{JsonSerializer.Serialize(version.Key)}:{version.Value}")));
            header.Append('}');
        }

        return header.Append('}').ToString();
    }

    /// <summary>A whole table file: the header, with the content version of every module whose codecs wrote a record, then each record on its own line, every line ending in '\n'.</summary>
    public static string Write<T>(string tableName, IEnumerable<T> records, ContentTypeRegistry types)
        where T : notnull
    {
        var modulesUsed = new HashSet<string>(StringComparer.Ordinal);
        var lines = new StringBuilder();
        foreach (var record in records)
        {
            lines.Append(Encoding.UTF8.GetString(ContentWriter.WriteRecord(types, record, modulesUsed: modulesUsed))).Append('\n');
        }

        var moduleVersions = modulesUsed.ToDictionary(moduleName => moduleName, types.ContentVersionOf, StringComparer.Ordinal);
        return new StringBuilder().Append(Header(tableName, moduleVersions)).Append('\n').Append(lines).ToString();
    }

    /// <summary>One record's line, as Write writes it, without the '\n'.</summary>
    public static string WriteLine<T>(T record, ContentTypeRegistry types)
        where T : notnull =>
        Encoding.UTF8.GetString(ContentWriter.WriteRecord(types, record));

    /// <summary>The Guid a record line holds under "id", whether or not the rest of it decodes; false for a line that isn't a JSON object with one.</summary>
    public static bool TryReadId(string line, out Guid id)
    {
        id = Guid.Empty;
        try
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty(IdKey, out var idElement) &&
                idElement.ValueKind == JsonValueKind.String &&
                ContentGuid.TryParse(idElement.GetString()!, out id);
        }
        catch (JsonException)
        {
            return false;
        }
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

    /// <summary>A save's removal line for id: the override of id before it is taken away, and the lower layer's record is back.</summary>
    public static string WriteRemoval(Guid id) => $"{{\"{IdKey}\":\"{ContentGuid.Format(id)}\",\"{RemovalKey}\":true}}";

    /// <summary>Whether header holds this format's version and names table tableName, and the module versions it gives.</summary>
    internal static bool TryReadHeader(JsonElement header, string tableName, out IReadOnlyDictionary<string, int> moduleVersions, out string problem)
    {
        var versions = new Dictionary<string, int>(StringComparer.Ordinal);
        moduleVersions = versions;
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

        if (header.TryGetProperty(VersionsKey, out var versionsElement))
        {
            if (versionsElement.ValueKind != JsonValueKind.Object)
            {
                problem = $"The header's \"{VersionsKey}\" must map module names to versions.";
                return false;
            }

            foreach (var module in versionsElement.EnumerateObject())
            {
                if (module.Value.ValueKind != JsonValueKind.Number || !module.Value.TryGetInt32(out var moduleVersion) || moduleVersion < ContentTypeRegistry.DefaultContentVersion)
                {
                    problem = $"The header's version of {module.Name} must be a whole number of at least {ContentTypeRegistry.DefaultContentVersion}.";
                    return false;
                }

                versions[module.Name] = moduleVersion;
            }
        }

        problem = string.Empty;
        return true;
    }

    private static ContentTableRecord<T>? TryReadRecord<T>(JsonElement element, string fileName, int lineNumber, ContentTypeRegistry types, IContentReferences references, List<ContentFailure> failures)
        where T : class
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(RemovalKey, out var removal))
        {
            return TryReadRemoval<T>(element, removal, fileName, lineNumber, failures);
        }

        return TryReadRecord<T>(element, fileName, lineNumber, types, references, failures, EnvelopeKeys);
    }

    /// <summary>A removal line: an id and "rm": true, and nothing else.</summary>
    private static ContentTableRecord<T>? TryReadRemoval<T>(JsonElement element, JsonElement removal, string fileName, int lineNumber, List<ContentFailure> failures)
        where T : class
    {
        if (!element.TryGetProperty(IdKey, out var idElement) || idElement.ValueKind != JsonValueKind.String || !ContentGuid.TryParse(idElement.GetString()!, out var id))
        {
            failures.Add(new ContentFailure(fileName, lineNumber, null, $"A record needs an \"{IdKey}\"."));
            return null;
        }

        if (removal.ValueKind != JsonValueKind.True || element.EnumerateObject().Any(property => property.Name != IdKey && property.Name != RemovalKey))
        {
            failures.Add(new ContentFailure(fileName, lineNumber, id, $"A removal line holds only \"{IdKey}\" and \"{RemovalKey}\": true."));
            return null;
        }

        return new ContentTableRecord<T>(lineNumber, id, null, IsDisabled: false, Replacement: null, IsRemoval: true);
    }

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
