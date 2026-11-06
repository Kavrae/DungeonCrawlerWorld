using System.Text.Json;
using Engine.Content;
using Engine.Json;

namespace Engine.Persistence;

/// <summary>Rewrites a save's content table keeping only the lines that decide something: each Guid's last line, and none for a Guid whose last line is a removal ("rm").</summary>
/// <remarks>
/// The table is read by the rules a Save layer is read by (ContentTable.ReadIndex, ContentLayers), so it reads exactly as
/// it did before. A line those rules leave out -- one that isn't JSON or holds a malformed envelope, or a disabled line,
/// which a save can't hold -- decides nothing and is kept as written, so it is still reported; a last line cut short is
/// dropped. A Guid's winning line takes the place where the Guid first appeared, so records keep their order. A file whose
/// header can't be read is left alone, and one with nothing to drop isn't written. Run only while nothing reads the file:
/// a session's index holds where each of its lines is.
/// </remarks>
public static class ContentTableCompaction
{
    /// <summary>Compacts the table file at path; a missing file is nothing to compact.</summary>
    /// <returns>How many lines were dropped.</returns>
    public static int Compact(string path)
    {
        if (!File.Exists(path))
        {
            return 0;
        }

        var bytes = File.ReadAllBytes(path);
        var lines = JsonLines.Split(bytes).ToList();
        if (lines.Count == 0 || !TryReadTableName(bytes, lines[0], out var tableName))
        {
            return 0;
        }

        var failures = new List<ContentFailure>();
        var index = ContentTable.ReadIndex(bytes, Path.GetFileName(path), tableName, static _ => 0, failures);
        if (failures.Any(failure => failure.Line == lines[0].Number))
        {
            return 0;
        }

        var decidingByLineNumber = index.Lines.Where(line => !line.IsDisabled).ToDictionary(line => line.Line);
        var firstLineNumberById = new Dictionary<Guid, int>();
        var lastLineById = new Dictionary<Guid, ContentTableIndexLine<int>>();
        foreach (var line in decidingByLineNumber.Values.OrderBy(line => line.Line))
        {
            firstLineNumberById.TryAdd(line.Id, line.Line);
            lastLineById[line.Id] = line;
        }

        var winnerByLineNumber = lastLineById.Values.Where(line => !line.IsRemoval).ToDictionary(line => firstLineNumberById[line.Id]);
        var indexedLineNumbers = index.Lines.Select(line => line.Line).ToHashSet();

        using var compacted = new MemoryStream(bytes.Length);
        AppendLine(compacted, bytes, lines[0].Offset, lines[0].Length);
        var kept = 1;
        foreach (var line in lines.Skip(1))
        {
            if (winnerByLineNumber.TryGetValue(line.Number, out var winner))
            {
                AppendLine(compacted, bytes, (int)winner.Offset, winner.Length);
                kept++;
            }
            else if (!decidingByLineNumber.ContainsKey(line.Number) && (line.IsComplete || indexedLineNumbers.Contains(line.Number)))
            {
                AppendLine(compacted, bytes, line.Offset, line.Length);
                kept++;
            }
        }

        var dropped = lines.Count - kept;
        if (dropped == 0)
        {
            return 0;
        }

        var replacement = path + ".tmp";
        File.WriteAllBytes(replacement, compacted.ToArray());
        File.Move(replacement, path, overwrite: true);
        return dropped;
    }

    private static void AppendLine(MemoryStream target, byte[] bytes, int offset, int length)
    {
        target.Write(bytes, offset, length);
        target.WriteByte((byte)'\n');
    }

    private static bool TryReadTableName(byte[] bytes, JsonLine header, out string tableName)
    {
        tableName = string.Empty;
        if (!JsonLines.TryParse(bytes, header, out var document, out _))
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(ContentTable.TableKey, out var table) || table.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            tableName = table.GetString()!;
            return true;
        }
    }
}
