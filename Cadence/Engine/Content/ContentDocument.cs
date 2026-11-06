using System.Text;
using System.Text.Json;
using Engine.Json;

namespace Engine.Content;

/// <summary>A record read from a content document, with where each of its values sits in the file.</summary>
public sealed record ContentDocumentRecord<T>(Guid Id, T? Value, bool IsDisabled, Guid? Replacement, JsonSourceMap Source)
    where T : class;

/// <summary>One record of a table as a file of its own: one JSON object holding the format and table first, then the record's fields, laid out one value per line (JsonExpandedLayout).</summary>
/// <remarks>
/// For records too large for one line of a JSON Lines table -- a map area's rows -- so that a person can read and write
/// one, and a diff shows the line that changed. The record carries the same "id", "off" and "rep" as a table's line,
/// and its source map (JsonSourceMap) names the line and column of each value for a failure to point at.
/// </remarks>
public static class ContentDocument
{
    private static readonly IReadOnlySet<string> EnvelopeKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        ContentTable.FormatKey, ContentTable.TableKey, ContentTable.VersionsKey, ContentTable.DisabledKey, ContentTable.ReplacementKey,
    };

    /// <summary>Reads the record of path, which must be a document of table tableName; null, with a failure added, when it can't be read.</summary>
    public static ContentDocumentRecord<T>? Read<T>(string path, string tableName, ContentTypeRegistry types, IContentReferences references, List<ContentFailure> failures)
        where T : class =>
        Read<T>(File.ReadAllText(path, Encoding.UTF8), Path.GetFileName(path), tableName, types, references, failures);

    /// <summary>Reads the record of text, a whole document; fileName names it in failures and in the record's source map.</summary>
    public static ContentDocumentRecord<T>? Read<T>(string text, string fileName, string tableName, ContentTypeRegistry types, IContentReferences references, List<ContentFailure> failures)
        where T : class
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes);
        }
        catch (JsonException exception)
        {
            failures.Add(new ContentFailure(fileName, (int)(exception.LineNumber ?? 0) + 1, null, $"Not valid JSON: {exception.Message}", (int)(exception.BytePositionInLine ?? 0) + 1));
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (!ContentTable.TryReadHeader(root, tableName, out _, out var headerProblem))
            {
                failures.Add(new ContentFailure(fileName, 1, null, headerProblem));
                return null;
            }

            var record = ContentTable.TryReadRecord<T>(root, fileName, 1, types, references, failures, EnvelopeKeys);
            return record is null
                ? null
                : new ContentDocumentRecord<T>(record.Id, record.Value, record.IsDisabled, record.Replacement, JsonSourceMap.Of(bytes, fileName));
        }
    }

    /// <summary>The whole document for record of table tableName, ending in '\n'.</summary>
    public static string Write<T>(string tableName, T record, ContentTypeRegistry types)
        where T : notnull
    {
        var compact = ContentWriter.WriteRecord(types, record, header: json =>
        {
            json.WriteNumber(ContentTable.FormatKey, ContentTable.FormatVersion);
            json.WriteString(ContentTable.TableKey, tableName);
        });

        using var document = JsonDocument.Parse(compact);
        return JsonExpandedLayout.Write(document.RootElement) + "\n";
    }
}
