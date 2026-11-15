using System.Text.Json.Nodes;
using Engine.Content;

namespace Workshop.Editing;

/// <summary>Where one table is saved: its master file, written only by the codec, one line at a time, and its drafts file, written as the editor holds each record.</summary>
/// <remarks>
/// Both are read and written through ContentTableLines, so a save changes only the lines of the records it saved. A draft
/// line is the record's JSON as edited, whether or not it decodes; a published record deleted and saved, not yet
/// published, is a removal line (its id and "rm": true), which drafts are never read as a game layer to mean anything else.
/// A drafts file left with no record is deleted rather than kept as a header alone.
/// </remarks>
public sealed class ContentTableFiles(string tableName, string masterPath, string draftsPath)
{
    public string TableName { get; } = tableName;

    public string MasterPath { get; } = masterPath;

    public string DraftsPath { get; } = draftsPath;

    /// <summary>The master file's name ("Items.jsonl"), which a trial build's layer holds it under.</summary>
    public string FileName => Path.GetFileName(MasterPath);

    public ContentTableLines ReadMaster() => ContentTableLines.Read(MasterPath, ContentTable.Header(TableName));

    /// <summary>The master file as masterFiles (every master file's text, by file name) holds it: one read of master, shared by whatever reads it.</summary>
    public ContentTableLines MasterFrom(IReadOnlyDictionary<string, string> masterFiles) =>
        masterFiles.TryGetValue(FileName, out var text) ? ContentTableLines.Parse(text) : ContentTableLines.Empty(ContentTable.Header(TableName));

    public ContentTableLines ReadDrafts() => ContentTableLines.Read(DraftsPath, ContentTable.Header(TableName));

    /// <summary>Adds the master file's write to writes.</summary>
    public void WriteMaster(ContentFileWrites writes, ContentTableLines lines) => writes.Write(MasterPath, lines.ToText());

    /// <summary>Adds the drafts file's write to writes, or its deletion when it holds no record.</summary>
    public void WriteDrafts(ContentFileWrites writes, ContentTableLines lines)
    {
        if (!lines.Lines.Any(line => ContentTable.TryReadId(line, out _)))
        {
            writes.Delete(DraftsPath);
            return;
        }

        writes.Write(DraftsPath, lines.ToText());
    }

    /// <summary>A drafts line read back: the record's JSON; or, for a removal line (RemovalLine), that a published record was deleted and saved so; false for a line holding no record.</summary>
    public static bool TryReadDraftLine(string line, out Guid id, out string json, out bool isRemoval)
    {
        json = string.Empty;
        isRemoval = false;
        if (!ContentTable.TryReadId(line, out id) || JsonNode.Parse(line) is not JsonObject record)
        {
            return false;
        }

        if (record[ContentTable.RemovalKey] is JsonValue removal && removal.TryGetValue<bool>(out var removed) && removed)
        {
            isRemoval = true;
            return true;
        }

        json = record.ToJsonString();
        return true;
    }

    /// <summary>Whether two record lines hold the same JSON, however each escapes its text: a draft of master's record as it stands is no draft.</summary>
    public static bool IsSameRecord(string first, string second) =>
        first == second || JsonNode.DeepEquals(JsonNode.Parse(first), JsonNode.Parse(second));

    /// <summary>The drafts line saying master's record id was deleted: saved, not yet published (the next publish takes it out of master).</summary>
    public static string RemovalLine(Guid id) => ContentTable.WriteRemoval(id);

    /// <summary>Whether a master line is a disabled record ("off": true).</summary>
    public static bool IsDisabledLine(string line) =>
        JsonNode.Parse(line) is JsonObject record && record[ContentTable.DisabledKey] is JsonValue disabled && disabled.TryGetValue<bool>(out var isDisabled) && isDisabled;
}
