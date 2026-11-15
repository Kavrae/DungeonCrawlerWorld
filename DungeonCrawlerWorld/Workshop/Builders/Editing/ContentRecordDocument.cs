using System.Text.Json.Nodes;
using Engine.Content;
using Workshop.Editing;

namespace Builders.Editing;

/// <summary>Master's line for a record as found on disk by a publish, where it no longer matched what the record was opened with; Line is null when master no longer holds the record.</summary>
public sealed record MovedMasterLine(string? Line);

/// <summary>One record the Workshop holds open: what is being edited, the last definition it decoded to, where it is saved, and what is wrong with it now.</summary>
/// <remarks>Kept for the session by Guid, so selection, edits and undo outlive any rebuild of what shows it.</remarks>
public sealed class ContentRecordDocument<T>(EditableContentRecord record, T original, string? modName)
    where T : class
{
    public Guid Id => Record.Id;

    public EditableContentRecord Record { get; } = record;

    /// <summary>The definition it was opened with: the session's, or a stand-in for a record that didn't decode.</summary>
    public T Original { get; } = original;

    /// <summary>The mod whose layer supplied it, or null for master: a mod's record is shown, never edited.</summary>
    public string? ModName { get; } = modName;

    public bool IsReadOnly => ModName is not null;

    /// <summary>The mod whose record the game loads in place of this one -- a master record or draft a mod replaces, which is edited here all the same; null when the game loads this one.</summary>
    public string? ReplacedInGameBy { get; init; }

    /// <summary>The last definition the record decoded to -- what the preview shows, even while it doesn't decode.</summary>
    public T LastGood { get; set; } = original;

    /// <summary>Why the record as it is now doesn't decode; null when it does.</summary>
    public string? DecodeFailure { get; set; }

    /// <summary>What the per-record checks refuse in what it decoded to.</summary>
    public IReadOnlyList<string> CheckProblems { get; set; } = [];

    /// <summary>Why the last save sent it to Drafts beyond its own errors -- a trial build's failure, a draft it names -- until it is edited or saved again.</summary>
    public IReadOnlyList<string> SaveProblems { get; set; } = [];

    /// <summary>What is wrong with each of its values as the schemas read them.</summary>
    public IReadOnlyList<ContentFieldProblem> SchemaProblems
    {
        get => _schemaProblems;
        set
        {
            _schemaProblems = value;
            _fieldProblems = null;
        }
    }

    /// <summary>Warnings that another record looks the same (glyph and colour, or sprite).</summary>
    public IReadOnlyList<ContentFieldProblem> AppearanceProblems
    {
        get => _appearanceProblems;
        set
        {
            _appearanceProblems = value;
            _fieldProblems = null;
        }
    }

    /// <summary>What is wrong with what it names, which only the other records can say: a disabled record with no replacement, a replacement that isn't published or comes back round.</summary>
    public IReadOnlyList<ContentFieldProblem> ReferenceProblems
    {
        get => _referenceProblems;
        set
        {
            _referenceProblems = value;
            _fieldProblems = null;
        }
    }

    /// <summary>Every problem shown at a field: the schemas' (less what isn't filled in yet), what it names, then the look's.</summary>
    public IReadOnlyList<ContentFieldProblem> FieldProblems => _fieldProblems ??= [.. _schemaProblems.Where(problem => !IsNotFilledInYet(problem)), .. _referenceProblems, .. _appearanceProblems];

    /// <summary>Disabled ("off"): kept whole in master, but never loaded.</summary>
    public bool IsDisabled => Record.Root[ContentTable.DisabledKey] is JsonValue disabled && disabled.TryGetValue<bool>(out var isDisabled) && isDisabled;

    /// <summary>What replaces it while disabled ("rep"); null for nothing.</summary>
    public Guid? Replacement => Record.Root[ContentTable.ReplacementKey] is JsonValue replacement && replacement.TryGetValue<string>(out var text) && ContentGuid.TryParse(text, out var id) ? id : null;

    private IReadOnlyList<ContentFieldProblem> _referenceProblems = [];

    /// <summary>
    /// The paths of required values an edit left missing -- a list element just added, a type just picked -- which aren't
    /// shown as problems until the record is saved or the value is filled in; one that was missing already isn't one.
    /// </summary>
    public HashSet<string> NotFilledInYet { get; } = new(StringComparer.Ordinal);

    /// <summary>Whether NotFilledInYet holds any value still missing.</summary>
    public bool HasValuesNotFilledInYet => _schemaProblems.Any(IsNotFilledInYet);

    private bool IsNotFilledInYet(ContentFieldProblem problem) => problem.IsMissing && NotFilledInYet.Contains(problem.Path.Text);

    /// <summary>Forgets what wasn't filled in yet, so it is shown as the problem it is -- once the record is saved.</summary>
    public void ShowEverythingMissing()
    {
        NotFilledInYet.Clear();
        _fieldProblems = null;
    }

    /// <summary>After an edit or an undo: each required value newly missing (not in missingBefore) isn't filled in yet, and one no longer missing is forgotten.</summary>
    public void TrackNotFilledInYet(IReadOnlySet<string> missingBefore)
    {
        var missingNow = MissingPaths();
        NotFilledInYet.IntersectWith(missingNow);
        NotFilledInYet.UnionWith(missingNow.Where(path => !missingBefore.Contains(path)));
        _fieldProblems = null;
    }

    /// <summary>The paths of every required value missing now.</summary>
    public HashSet<string> MissingPaths() => [.. _schemaProblems.Where(problem => problem.IsMissing).Select(problem => problem.Path.Text)];

    private IReadOnlyList<ContentFieldProblem> _schemaProblems = [];
    private IReadOnlyList<ContentFieldProblem> _appearanceProblems = [];
    private IReadOnlyList<ContentFieldProblem>? _fieldProblems;

    /// <summary>Whether anything stops the record: an error at a field (shown or not filled in yet), a failed decode, or a failed check -- one is as fatal as another.</summary>
    public bool HasErrors => DecodeFailure is not null || CheckProblems.Count > 0 || _schemaProblems.Any(problem => !problem.IsWarning) || _referenceProblems.Any(problem => !problem.IsWarning);

    /// <summary>Whether a field shows an error, or values aren't filled in yet: either already says why the record doesn't decode.</summary>
    public bool ExplainsDecodeFailure => FieldProblems.Any(problem => !problem.IsWarning) || HasValuesNotFilledInYet;

    /// <summary>Its line in master as saved; null when master doesn't hold it.</summary>
    public string? MasterLine { get; set; }

    /// <summary>Master's line as the last publish found it on disk, when that wasn't MasterLine -- changed under the Workshop -- until Reload Master takes it; null otherwise.</summary>
    public MovedMasterLine? MasterMovedTo { get; set; }

    /// <summary>Its JSON in Drafts as saved; null when it has no draft.</summary>
    public string? DraftJson { get; set; }

    /// <summary>Deleted, and saved so: Drafts holds a removal line for it until a publish takes it out of master, or it is brought back and saved.</summary>
    public bool IsRemovalSaved { get; set; }

    public bool IsInMaster => MasterLine is not null;

    /// <summary>Saved as a draft: a record of its own there, or a master record's unpublished version.</summary>
    public bool IsDraft => DraftJson is not null;

    /// <summary>A draft a publish would put in master: nothing is wrong with it, and the last save didn't keep it back. One with errors stays a draft whatever is published.</summary>
    public bool IsWaitingToPublish => IsDraft && !HasErrors && SaveProblems.Count == 0;

    /// <summary>Whether a save would write it: its JSON differs from what was saved.</summary>
    public bool IsChanged => Record.IsChanged;

    private IReadOnlyList<ContentFieldDifference> _differencesFromMaster = [];
    private int _differencesVersion = -1;
    private string? _differencesMasterLine;

    /// <summary>Every difference between master's line and the record as it is now -- worked out once per change of either; null when master doesn't hold it.</summary>
    public IReadOnlyList<ContentFieldDifference>? DifferencesFromMaster(ContentTypeRegistry types)
    {
        if (MasterLine is not { } masterLine)
        {
            return null;
        }

        if (_differencesVersion != Record.Version || !ReferenceEquals(_differencesMasterLine, masterLine))
        {
            _differencesVersion = Record.Version;
            _differencesMasterLine = masterLine;
            _differencesFromMaster = ContentRecordDiff.Compare(masterLine, Record.Json, Record.ValueType, types);
        }

        return _differencesFromMaster;
    }

    /// <summary>Whether the value at path differs from master's line -- it, or what holds it, was changed, added or moved, or something was removed from it.</summary>
    public bool IsChangedFromMaster(ContentPath path, ContentTypeRegistry types) =>
        DifferencesFromMaster(types)?.Any(difference => difference.CurrentPlace is { IsRoot: false } place ? path.IsWithin(place) : path.Equals(difference.RemovedFrom)) == true;

    private IReadOnlyList<ContentFieldReference> _references = [];
    private int _referencesVersion = -1;

    /// <summary>Every record it names as it is now, with the field naming each -- walked once per change of its JSON.</summary>
    public IReadOnlyList<ContentFieldReference> ReferencesNamed(ContentTypeRegistry types)
    {
        if (_referencesVersion != Record.Version)
        {
            _referencesVersion = Record.Version;
            _references = ContentReferenceWalk.ReferencesOf(Record, types);
        }

        return _references;
    }

    public override string ToString() => $"{Record.TableName} {ContentGuid.Format(Id)}";
}
