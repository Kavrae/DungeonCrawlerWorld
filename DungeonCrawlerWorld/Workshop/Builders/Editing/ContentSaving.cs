using Engine.Content;
using Game.Bootstrap;
using Workshop.Editing;

namespace Builders.Editing;

/// <summary>What one record brings to a save: whether anything stops it, what it names, the line the codec writes for it, and its JSON as edited.</summary>
/// <param name="PublishedLine">The codec's own line for what it decodes to; null when it doesn't decode.</param>
/// <param name="OpenedMasterLine">Master's line for it as the Workshop last read or wrote it -- what its edits were made against; null for a record master didn't hold.</param>
public sealed record ContentSaveEntry(string TableName, Guid Id, string Name, bool HasErrors, IReadOnlyList<ContentFieldReference> References, string? PublishedLine, string DraftJson, string? OpenedMasterLine);

public enum ContentSaveDestination : byte
{
    Master,
    Drafts,
}

/// <summary>Where a save sent one record, and why it went to Drafts beyond its own errors.</summary>
public sealed record ContentSaveResult(string TableName, Guid Id, ContentSaveDestination Destination, IReadOnlyList<string> Reasons, string? PublishedLine)
{
    /// <summary>A publish found master's line for it changed on disk since it was opened, and kept it in Drafts rather than write over that.</summary>
    public bool IsMasterMoved { get; init; }

    /// <summary>When IsMasterMoved: master's line for it as found on disk; null when master no longer holds it.</summary>
    public string? MasterLineNow { get; init; }
}

/// <summary>What a save did: where each record went, how many deletions it wrote, each deletion a publish kept in Drafts, and why a table couldn't be written.</summary>
/// <param name="RefusedRemovals">Each deletion a publish kept out of master, saved to Drafts instead, with why.</param>
/// <param name="WriteFailure">Why the files of a table couldn't be written, which leaves it unsaved; null when every one was.</param>
public sealed record ContentSaveReport(IReadOnlyList<ContentSaveResult> Results, int RemovedCount, IReadOnlyDictionary<ContentRecordKey, IReadOnlyList<string>> RefusedRemovals, string? WriteFailure)
{
    public static ContentSaveReport Failed(string writeFailure) => new([], 0, new Dictionary<ContentRecordKey, IReadOnlyList<string>>(), writeFailure);

    /// <summary>Whether the save had nothing to write.</summary>
    public bool IsEmpty => Results.Count == 0 && RemovedCount == 0 && RefusedRemovals.Count == 0 && WriteFailure is null;
}

/// <summary>A record by its table and Guid: what names one record across every table the workspace holds.</summary>
public readonly record struct ContentRecordKey(string TableName, Guid Id);

/// <summary>One record that names another: its name, kind and how it names it, and the table holding it -- null for what can't be edited here (code, a table no tab edits), which TabTitle may still show.</summary>
public sealed record ContentDependent(string Name, string Kind, string Relationship, IContentEditingTable? Table, Guid Id, string? TabTitle);

/// <summary>What disabling a record touches: the records naming it (Direct), those Cascade disables with it, those it can't (NotEditable, which refuse Cascade) and drafts, which neither block nor are disabled.</summary>
public sealed record ContentDisablePlan(IReadOnlyList<ContentDependent> Direct, IReadOnlyList<ContentDependent> Cascaded, IReadOnlyList<ContentDependent> NotEditable, IReadOnlyList<ContentDependent> Drafts)
{
    /// <summary>Why Cascade can't run: something it would have to disable can't be edited here; null when it can.</summary>
    public string? CascadeRefusal => NotEditable.Count == 0
        ? null
        : $"{string.Join(", ", NotEditable.Take(3).Select(dependent => $"{dependent.Kind} {dependent.Name}"))}{(NotEditable.Count > 3 ? $" and {NotEditable.Count - 3} more" : string.Empty)} can't be disabled here (code, or a table no tab edits).";
}

/// <summary>What a trial build of master alone found: each record it failed, and a failure of the whole build naming no record.</summary>
public sealed record ContentTrialOutcome(IReadOnlyList<ContentFailure> Failures, string? BuildFailure);

/// <summary>How the workspace saves and rebuilds: where each table's files are, master as it is on disk, a trial build of master as given, and a fresh session over what is saved.</summary>
/// <param name="ReadMasterFiles">Every master file's text, by its path inside master ("Items.jsonl", "Areas/start.json"): read once per publish, so its trial builds and its writes start from the same master.</param>
/// <param name="TrialBuild">Builds the built-ins over master as the files it is given (every master file, by path), never simulated, failures collected.</param>
/// <param name="BuildSession">The Workshop's session over master as saved, built on a worker.</param>
public sealed record ContentSaving(Func<string, ContentTableFiles> FilesOf, Func<IReadOnlyDictionary<string, string>> ReadMasterFiles, Func<IReadOnlyDictionary<string, string>, ContentTrialOutcome> TrialBuild, Func<GameSession> BuildSession);
