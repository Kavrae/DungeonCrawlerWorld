using System.Text.Json;
using System.Text.Json.Nodes;
using Engine.Content;
using Microsoft.Xna.Framework;
using UIEngine.UI;
using UIEngine.UI.Chrome;
using UIEngine.UI.ColorPalettes;
using UIEngine.UI.Fields;

namespace Workshop.Editing;

/// <summary>What a game adds to its inspectors: the choices a text field offers, a note under a field, a preview drawn among an object's rows, and the records a reference may name.</summary>
/// <remarks>Every answer is optional; an engine-only table needs none of them.</remarks>
public interface IContentInspectorSource
{
    /// <summary>The records of tableName a reference field may name, as a picker lists them.</summary>
    IReadOnlyList<ContentReferenceChoice> ReferenceChoices(string tableName);

    /// <summary>What a picker offers for a text field of schema (sprite names); null for plain text.</summary>
    IReadOnlyList<string>? TextChoices(ContentSchema schema, ContentField field);

    /// <summary>A read-only line shown under field's row (the tags an activator implies); null for none.</summary>
    string? NoteFor(ContentSchema schema, ContentField field, EditableContentRecord record);

    /// <summary>A preview drawn among the rows of an object of schema (a targeting shape, an icon); null for none.</summary>
    ContentInspectorDecoration? DecorationFor(ContentSchema schema);

    /// <summary>The order schema's fields are shown in -- the schema's own unless a field reads better beside another (an activator before the tags it implies). Every field, each once.</summary>
    IReadOnlyList<ContentField> FieldsInDisplayOrder(ContentSchema schema) => schema.Fields;

    /// <summary>The heading of element index of field's list, with the family member it is (typeName) when the list holds a polymorphic family -- "[0]" or "[0] DirectHeal" unless a list's elements have a name of their own ("Group 0").</summary>
    string ElementTitle(ContentField field, int index, string? typeName) => typeName is null ? $"[{index}]" : $"[{index}] {typeName}";

    /// <summary>What to ask before field of schema changes which family member it holds (an item's activator); null to change it at once.</summary>
    string? TypeChangeConfirmation(ContentSchema schema, ContentField field) => null;

    /// <summary>The records of tableName a disabled record recordId may be replaced by -- every record of it but itself, unless the source narrows it (published and enabled only).</summary>
    IReadOnlyList<ContentReferenceChoice> ReplacementChoices(string tableName, Guid recordId) =>
        [.. ReferenceChoices(tableName).Where(choice => choice.Id != recordId)];
}

/// <summary>A preview among an object's rows: a heading, then a host of Height filled from the object's JSON, placed before BeforeKey's row (first, when null) and filled again after every edit.</summary>
public sealed record ContentInspectorDecoration(string Title, string? BeforeKey, float Height, Action<Window, JsonElement, float> Fill);

/// <summary>An edit the inspector asks for: what it is called, what it does to the record, and whether it changes which rows there are.</summary>
/// <param name="MergeKey">The field it edits, so a run of edits to one field undoes in one step; null for a structural edit.</param>
public sealed record ContentInspectorEdit(string Description, Action<EditableContentRecord> Apply, string? MergeKey, bool ChangesShape)
{
    /// <summary>A question the owner asks before applying the edit (changing an item's activator); null to apply it at once. Declined, nothing changes and the rows are shown again as they were.</summary>
    public string? Confirmation { get; init; }

    /// <summary>The field the edit changed, whose control undo and redo focus again; MergeKey's field when not given.</summary>
    public string? FieldPath
    {
        get => _fieldPath ?? MergeKey;
        init => _fieldPath = value;
    }

    private readonly string? _fieldPath;
}

/// <summary>
/// The rows of one record, generated from its schemas: a row per field in schema order -- a control by the field's kind,
/// a heading for each object, list and list element, a type picker for each polymorphic value -- indented by depth.
/// </summary>
/// <remarks>
/// <para>
/// It never changes the record itself: a committed value, a reset, an add, remove or move, a type picked is handed to its
/// owner as a ContentInspectorEdit (EditRequested), which records it in the undo log and applies it. A value edit keeps
/// every row where it is; one that changes which rows there are rebuilds them on the next Update, never inside the click
/// that asked.
/// </para>
/// <para>
/// A field set in the record shows its value; one left out shows its default, dimmed. A value of the wrong kind, and a
/// key no schema declares, is shown as written, read-only. A value that differs from the comparison's baseline has a
/// revert button. Problems colour the row of their path, or the nearest row above it that is shown. The
/// record's own Id is read-only; a type change the source asks to confirm (TypeChangeConfirmation) carries the question
/// on its edit.
/// </para>
/// <para>
/// Which headings are closed and how far the rows are scrolled belong to the record shown: a rebuild (an add, a remove,
/// a heading opened or closed, undo) keeps both, and so does showing the same record again in a new host after its tab
/// reopens (RememberScroll as it closes). Another record starts open and at the top.
/// </para>
/// </remarks>
public sealed class ContentInspector(ElementPoolService elementPool, ContentTypeRegistry types, IContentInspectorSource source, Action<Element> focus) : IUncommittedFieldText
{
    private const string NoneChoice = "(none)";
    private const string AddLabel = "Add";
    private const string RemoveLabel = "Remove";
    private const string MoveUpLabel = "^";
    private const string MoveDownLabel = "v";
    private const string DeleteLabel = "X";
    private const float NoteHeight = 20f;
    private const float DecorationGap = 4f;
    private const string IdHint = "What every other record, save and mod names this one by; never changed";
    private const string DisabledHint = "Kept whole, but not loaded: the game and its saves never see it. Enable puts it back";
    private const string ReplacementHint = "What everything naming this record gets instead; none, and whatever names it is an error";
    private static readonly HashSet<string> EnvelopeKeys = new(StringComparer.Ordinal) { ContentTable.DisabledKey, ContentTable.ReplacementKey };

    private readonly Dictionary<string, Element> _rowsByPath = new(StringComparer.Ordinal);
    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);
    private readonly List<(Window Host, ContentInspectorDecoration Decoration, ContentPath ObjectPath)> _decorations = [];
    private readonly Dictionary<Element, ContentFieldDifference> _ownDifferences = [];
    private readonly HashSet<Element> _removedRows = [];
    private bool _showsChangedOnly;

    private Window? _host;
    private EditableContentRecord? _record;
    private IReadOnlyList<ContentFieldProblem> _problems = [];
    private ContentComparison? _comparison;
    private string _removedRowsBuiltFor = string.Empty;
    private bool _rebuildRequested;
    private ContentPath? _pendingFocus;

    /// <summary>The record whose closed headings (_collapsed) and scroll position (_rememberedScroll) are kept.</summary>
    private EditableContentRecord? _stateRecord;
    private Vector2 _rememberedScroll;

    /// <summary>Raised with every edit the player makes; the owner applies it.</summary>
    public event Action<ContentInspectorEdit>? EditRequested;
    public EditableContentRecord? Record => _record;

    /// <summary>What a record shown is compared with, asked each time its rows are built or refreshed; null (or no answer) marks nothing.</summary>
    public Func<EditableContentRecord, ContentComparison?>? ComparisonOf { get; set; }

    /// <summary>
    /// Shows only what differs from the comparison's baseline -- each changed or removed value, what it holds and every
    /// heading above it -- from the next rebuild on. Which rows show is decided when they are built, so a value typed back
    /// to the baseline stays where it is until the rows are built again.
    /// </summary>
    public bool ShowsChangedOnly
    {
        get => _showsChangedOnly;
        set
        {
            if (_showsChangedOnly == value)
            {
                return;
            }

            _showsChangedOnly = value;
            _rebuildRequested = _record is not null;
        }
    }

    /// <summary>Puts the rows in host from now on -- a scrolling window whose children tile down. Nothing is shown in it until Show.</summary>
    public void Attach(Window host)
    {
        _host = host;
        _record = null;
        _rebuildRequested = false;
        _rowsByPath.Clear();
        _decorations.Clear();
    }

    /// <summary>Shows record, rebuilding every row, with problems on them -- where it was scrolled to, when it is the record shown last.</summary>
    public void Show(EditableContentRecord record, IReadOnlyList<ContentFieldProblem> problems)
    {
        if (!ReferenceEquals(record, _stateRecord))
        {
            _collapsed.Clear();
            _stateRecord = record;
            _rememberedScroll = Vector2.Zero;
        }
        else if (ReferenceEquals(record, _record))
        {
            RememberScroll();
        }

        _record = record;
        _problems = problems;
        Rebuild();
    }

    /// <summary>Keeps how far the rows are scrolled, so showing the same record again -- in a new host, once its tab reopens -- puts it back.</summary>
    public void RememberScroll()
    {
        if (_host is { } host && _record is not null)
        {
            _rememberedScroll = host.ScrollOffset;
        }
    }

    /// <summary>After a value edit: colours the rows for problems, marks what differs and fills each preview again, keeping every row -- unless what was removed changed, which rebuilds them on the next Update.</summary>
    public void Refresh(IReadOnlyList<ContentFieldProblem> problems)
    {
        _problems = problems;
        _comparison = _record is { } record ? ComparisonOf?.Invoke(record) : null;
        if (RemovedRowsKey(_comparison) != _removedRowsBuiltFor)
        {
            _rebuildRequested = true;
            return;
        }

        ApplyProblems();
        ApplyDifferences();
        RefillDecorations();
    }

    /// <summary>Rebuilds every row on the next Update, with problems on them -- after an edit that changed which rows there are, or undo.</summary>
    public void RequestRebuild(IReadOnlyList<ContentFieldProblem> problems)
    {
        _problems = problems;
        _rebuildRequested = true;
    }

    /// <summary>Shows no record: every row goes, and message is shown instead when there is one.</summary>
    public void Clear(string? message)
    {
        _record = null;
        _rebuildRequested = false;
        _rowsByPath.Clear();
        _decorations.Clear();
        _ownDifferences.Clear();
        _removedRows.Clear();
        _comparison = null;
        if (_host is not { } host)
        {
            return;
        }

        elementPool.CloseAllChildren(host);
        if (message is not null)
        {
            AddNote(message, depth: 0);
        }
    }

    /// <summary>Takes the player to the field at path: opens every heading above it and focuses its control.</summary>
    public void FocusField(ContentPath path)
    {
        for (var ancestor = path; !ancestor.IsRoot; ancestor = ancestor.Parent)
        {
            _collapsed.Remove(ancestor.Text);
        }

        _pendingFocus = path;
        _rebuildRequested = true;
    }

    /// <summary>Puts back a field's typed, uncommitted text; false when no row had any.</summary>
    public bool RevertUncommitted()
    {
        foreach (var row in _rowsByPath.Values)
        {
            if (row is FieldRow fieldRow && fieldRow.RevertUncommitted())
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Commits a field's typed, uncommitted text, as Enter would -- before a save; false when no row had any.</summary>
    public bool CommitUncommitted()
    {
        foreach (var row in _rowsByPath.Values)
        {
            if (row is FieldRow fieldRow && fieldRow.CommitUncommitted())
            {
                return true;
            }
        }

        return false;
    }

    public void Update()
    {
        if (!_rebuildRequested)
        {
            return;
        }

        _rebuildRequested = false;
        RememberScroll();
        Rebuild();
        if (_pendingFocus is { } path)
        {
            _pendingFocus = null;
            switch (RowAtOrAbove(path))
            {
                case FieldRow row:
                    focus(row.Editor);
                    row.BringIntoView();
                    break;
                case { } heading:
                    heading.BringIntoView();
                    break;
            }
        }
    }

    /// <summary>The row of path, or of the nearest path above it that has one.</summary>
    private Element? RowAtOrAbove(ContentPath path)
    {
        for (var current = path; ; current = current.Parent)
        {
            if (_rowsByPath.TryGetValue(current.Text, out var row))
            {
                return row;
            }

            if (current.IsRoot)
            {
                return null;
            }
        }
    }

    private float RowWidth => System.Math.Max(200f, (_host?.ContentSize.X ?? 400f) - WindowChrome.ScrollbarThickness - 4f);

    private void Rebuild()
    {
        if (_host is not { } host || _record is not { } record)
        {
            return;
        }

        elementPool.CloseAllChildren(host);
        _rowsByPath.Clear();
        _decorations.Clear();
        _removedRows.Clear();
        _comparison = ComparisonOf?.Invoke(record);
        _removedRowsBuiltFor = RemovedRowsKey(_comparison);

        using var document = JsonDocument.Parse(record.Json);
        var schema = types.GetValueCodec(record.ValueType).Schema;
        BuildDisabledSection(document.RootElement, record, schema);
        BuildObject(document.RootElement, schema, ContentPath.Root, depth: 0, isRoot: true);
        ApplyProblems();
        ApplyDifferences();
        if (_showsChangedOnly)
        {
            HideUnchangedRows(host);
        }

        RefillDecorations();
        host.ScrollTo(_rememberedScroll);
    }

    /// <summary>What the removed rows were built for: every Removed difference's place in the baseline, so a refresh that removes or brings back something rebuilds.</summary>
    private static string RemovedRowsKey(ContentComparison? comparison) =>
        comparison is null ? string.Empty : string.Join("|", comparison.Differences.Where(difference => difference.Kind == ContentDifferenceKind.Removed).Select(difference => difference.BaselinePath!.Text));

    /// <summary>The Removed differences that need a row of their own in what path holds: list elements, and keys no schema row shows.</summary>
    private IEnumerable<ContentFieldDifference> RemovedRowsIn(ContentPath path) =>
        _comparison?.Differences.Where(difference => difference.Kind == ContentDifferenceKind.Removed && path.Equals(difference.RemovedFrom)) ?? [];

    /// <summary>For a disabled record ("off"), a section above its fields: the heading, and what replaces it ("rep") -- picked from what the source allows, or none.</summary>
    private void BuildDisabledSection(JsonElement root, EditableContentRecord record, ContentSchema schema)
    {
        if (!root.TryGetProperty(ContentTable.DisabledKey, out var disabled) || disabled.ValueKind != JsonValueKind.True)
        {
            return;
        }

        AddHeader(ContentPath.Root.Key(ContentTable.DisabledKey), "Disabled", 0, isCollapsible: false, isExpanded: true, [], hint: DisabledHint);
        var choices = source.ReplacementChoices(record.TableName, record.Id);
        List<string> labels = [NoneChoice, .. choices.Select(choice => choice.Label)];
        var selected = 0;
        var hasReplacement = root.TryGetProperty(ContentTable.ReplacementKey, out var replacement);
        if (hasReplacement && replacement.ValueKind == JsonValueKind.String && ContentGuid.TryParse(replacement.GetString()!, out var replacementId))
        {
            var index = choices.ToList().FindIndex(choice => choice.Id == replacementId);
            if (index < 0)
            {
                labels.Add(ContentGuid.Format(replacementId));
                index = labels.Count - 2;
            }

            selected = index + 1;
        }

        var path = ContentPath.Root.Key(ContentTable.ReplacementKey);
        var row = AddFieldRow(path, new FieldEditorDescription("Replacement", FieldValueKind.SearchableChoice) { Depth = 1, Choices = labels, IsSet = hasReplacement, Hint = ReplacementHint }, FieldValue.OfChoice(selected));
        row.Committed += chosen =>
        {
            if (chosen.ChoiceIndex == selected || chosen.ChoiceIndex > choices.Count)
            {
                return;
            }

            if (chosen.ChoiceIndex <= 0)
            {
                Request(new ContentInspectorEdit("Remove Replacement", target => target.RemoveField(ContentPath.Root, ContentTable.ReplacementKey), path.Text, ChangesShape: false));
                return;
            }

            var chosenId = ContentGuid.Format(choices[chosen.ChoiceIndex - 1].Id);
            Request(new ContentInspectorEdit("Change Replacement", target => target.SetField(ContentPath.Root, schema, ContentTable.ReplacementKey, JsonValue.Create(chosenId)), path.Text, ChangesShape: false));
        };
    }

    private void BuildObject(JsonElement json, ContentSchema schema, ContentPath path, int depth, bool isRoot)
    {
        var decoration = source.DecorationFor(schema);
        if (decoration is { BeforeKey: null })
        {
            AddDecoration(decoration, path, depth);
        }

        foreach (var field in source.FieldsInDisplayOrder(schema))
        {
            if (decoration is { BeforeKey: { } beforeKey } && beforeKey == field.Key)
            {
                AddDecoration(decoration, path, depth);
            }

            var has = json.TryGetProperty(field.Key, out var value);
            BuildField(has ? value : null, field, schema, path, depth);
            if (_record is { } record && source.NoteFor(schema, field, record) is { } note)
            {
                AddNote(note, depth + 1);
            }
        }

        foreach (var property in json.EnumerateObject())
        {
            if (schema.HasKey(property.Name) || property.Name == ContentWriter.DiscriminatorKey || (isRoot && EnvelopeKeys.Contains(property.Name)))
            {
                continue;
            }

            AddRawRow($"{property.Name} (unknown field)", property.Value, path, property.Name, depth);
        }

        foreach (var removed in RemovedRowsIn(path))
        {
            var key = removed.BaselinePath!.Last.Key;
            if (removed.IsRemovedElement || key is null || schema.HasKey(key))
            {
                continue;
            }

            var label = isRoot && key == ContentTable.DisabledKey ? "Disabled" : isRoot && key == ContentTable.ReplacementKey ? "Replacement" : $"{key} (unknown field)";
            AddRemovedRow(removed, label, depth);
        }
    }

    /// <summary>A value only the baseline holds, where the record no longer has a row for it: a dimmed label reading Removed, and a button that puts it back.</summary>
    private void AddRemovedRow(ContentFieldDifference removed, string label, int depth)
    {
        var host = _host!;
        var description = new FieldEditorDescription($"{label} (removed)", FieldValueKind.ReadOnlyText) { Depth = depth, IsSet = false, IsLabelOnly = true };
        var row = elementPool.CreateElement<FieldRow>(host, RowOptions(FieldRow.HeightOf(description), WindowPalette.PanelBackgroundColor));
        row.Configure(description, FieldValue.OfText(string.Empty));
        host.AddChild(row);
        var baselineName = _comparison?.BaselineName ?? string.Empty;
        row.SetChangeMarker(true, $"Removed: {baselineName} has it", $"Put it back as {baselineName} has it");
        row.RevertRequested += () => RequestRevert(removed);
        _removedRows.Add(row);
    }

    private void BuildField(JsonElement? value, ContentField field, ContentSchema schema, ContentPath objectPath, int depth)
    {
        var path = objectPath.Key(field.Key);
        switch (field.Kind)
        {
            case ContentFieldKind.Object:
                BuildObjectField(value, field, schema, objectPath, path, depth);
                return;
            case ContentFieldKind.Polymorphic:
                BuildPolymorphicField(value, field, schema, objectPath, path, depth);
                return;
            case ContentFieldKind.List:
                BuildListField(value, field, schema, objectPath, path, depth);
                return;
            default:
                BuildScalarField(value, field, field.Kind, schema, objectPath, path, depth);
                return;
        }
    }

    private void BuildObjectField(JsonElement? value, ContentField field, ContentSchema schema, ContentPath objectPath, ContentPath path, int depth)
    {
        if (value is { } present && present.ValueKind != JsonValueKind.Object)
        {
            AddRawRow(ContentFieldLabels.LabelOf(field), present, objectPath, field.Key, depth);
            return;
        }

        var isSet = value is not null;
        var isExpanded = isSet && !_collapsed.Contains(path.Text);
        string[] buttons = isSet ? field.IsRequired ? [] : [RemoveLabel] : [AddLabel];
        var (label, hint) = ContentFieldLabels.Split(field.DisplayName);
        var header = AddHeader(path, label + (isSet ? string.Empty : " (none)"), depth, isCollapsible: isSet, isExpanded, buttons, hint: hint);
        header.ButtonClicked += _ => Request(isSet
            ? new ContentInspectorEdit($"Remove {ContentFieldLabels.LabelOf(field)}", record => record.RemoveField(objectPath, field.Key), null, ChangesShape: true) { FieldPath = path.Text }
            : new ContentInspectorEdit($"Add {ContentFieldLabels.LabelOf(field)}", record => record.SetField(objectPath, schema, field.Key, new JsonObject()), null, ChangesShape: true) { FieldPath = path.Text });

        if (isExpanded && value is { } objectJson && ContentSchemas.OfValue(types, field, objectJson) is { } childSchema)
        {
            BuildObject(objectJson, childSchema, path, depth + 1, isRoot: false);
        }
    }

    private void BuildPolymorphicField(JsonElement? value, ContentField field, ContentSchema schema, ContentPath objectPath, ContentPath path, int depth)
    {
        if (value is { } present && present.ValueKind != JsonValueKind.Object)
        {
            AddRawRow(ContentFieldLabels.LabelOf(field), present, objectPath, field.Key, depth);
            return;
        }

        var members = types.MembersOf(field.ValueType!);
        var choices = field.IsRequired ? members.Select(member => member.Name).ToList() : [NoneChoice, .. members.Select(member => member.Name)];
        var name = value is { } json ? ContentSchemas.DiscriminatorOf(json) : null;
        var selected = name is null ? (field.IsRequired ? -1 : 0) : choices.IndexOf(name);
        var oldSchema = value is { } oldJson ? ContentSchemas.MemberSchema(types, field.ValueType!, oldJson) : null;

        var (label, hint) = ContentFieldLabels.Split(field.DisplayName);
        var row = AddFieldRow(path, new FieldEditorDescription(label, FieldValueKind.Choice) { Depth = depth, Choices = choices, IsSet = value is not null, Hint = hint }, FieldValue.OfChoice(selected));
        var confirmation = value is null ? null : source.TypeChangeConfirmation(schema, field);
        row.Committed += chosen =>
        {
            var chosenName = choices[chosen.ChoiceIndex];
            if (chosenName == NoneChoice)
            {
                Request(new ContentInspectorEdit($"Remove {ContentFieldLabels.LabelOf(field)}", record => record.RemoveField(objectPath, field.Key), null, ChangesShape: true) { Confirmation = confirmation, FieldPath = path.Text });
                return;
            }

            if (!types.TryGetMember(field.ValueType!, chosenName, out var codec))
            {
                return;
            }

            Request(new ContentInspectorEdit($"Make {ContentFieldLabels.LabelOf(field)} {chosenName}", record => record.ChangeType(objectPath, schema, field.Key, chosenName, oldSchema, codec.Schema), null, ChangesShape: true) { Confirmation = confirmation, FieldPath = path.Text });
        };

        if (value is { } memberJson && oldSchema is not null && !_collapsed.Contains(path.Text))
        {
            BuildObject(memberJson, oldSchema, path, depth + 1, isRoot: false);
        }
    }

    private void BuildListField(JsonElement? value, ContentField field, ContentSchema schema, ContentPath objectPath, ContentPath path, int depth)
    {
        if (value is { } present && present.ValueKind != JsonValueKind.Array)
        {
            AddRawRow(ContentFieldLabels.LabelOf(field), present, objectPath, field.Key, depth);
            return;
        }

        var count = value?.GetArrayLength() ?? 0;
        var removedElements = new Queue<ContentFieldDifference>(RemovedRowsIn(path).Where(removed => removed.IsRemovedElement).OrderBy(removed => removed.BaselinePath!.Last.Index));
        var isExpanded = !_collapsed.Contains(path.Text);
        var (label, hint) = ContentFieldLabels.Split(field.DisplayName);
        var header = AddHeader(path, $"{label} ({count})", depth, isCollapsible: count > 0 || removedElements.Count > 0, isExpanded, [AddLabel], hint: hint);
        header.ButtonClicked += _ =>
        {
            if (NewElement(field) is { } element)
            {
                Request(new ContentInspectorEdit($"Add to {ContentFieldLabels.LabelOf(field)}", record => record.InsertElement(objectPath, schema, field.Key, -1, element), null, ChangesShape: true) { FieldPath = path.Text });
            }
        };

        if (!isExpanded)
        {
            return;
        }

        var index = 0;
        foreach (var element in value is { } array ? array.EnumerateArray().ToArray() : [])
        {
            while (removedElements.TryPeek(out var removed) && removed.BaselinePath!.Last.Index <= index)
            {
                AddRemovedElementRow(removedElements.Dequeue(), field, depth + 1);
            }

            BuildElement(element, field, schema, objectPath, path.Element(index), index, count, depth + 1);
            index++;
        }

        while (removedElements.TryDequeue(out var removed))
        {
            AddRemovedElementRow(removed, field, depth + 1);
        }
    }

    private void AddRemovedElementRow(ContentFieldDifference removed, ContentField field, int depth)
    {
        string? typeName = null;
        if (field.ElementKind == ContentFieldKind.Polymorphic && removed.BaselineJson is { } json)
        {
            using var document = JsonDocument.Parse(json);
            typeName = ContentSchemas.DiscriminatorOf(document.RootElement);
        }

        AddRemovedRow(removed, source.ElementTitle(field, removed.BaselinePath!.Last.Index, typeName), depth);
    }

    private void BuildElement(JsonElement element, ContentField field, ContentSchema schema, ContentPath objectPath, ContentPath path, int index, int count, int depth)
    {
        var elementKind = field.ElementKind ?? ContentFieldKind.Object;
        if (elementKind is not (ContentFieldKind.Object or ContentFieldKind.Polymorphic))
        {
            BuildScalarElement(element, field, elementKind, objectPath, path, index, depth);
            return;
        }

        var isExpanded = !_collapsed.Contains(path.Text);
        var name = elementKind == ContentFieldKind.Polymorphic ? ContentSchemas.DiscriminatorOf(element) : null;
        var header = AddHeader(path, source.ElementTitle(field, index, name), depth, isCollapsible: true, isExpanded, [MoveUpLabel, MoveDownLabel, DeleteLabel], [index > 0, index < count - 1, true], buttonsFollowTitle: true);
        header.ButtonClicked += button => Request(button switch
        {
            0 => new ContentInspectorEdit($"Move {ContentFieldLabels.LabelOf(field)}[{index}] up", record => record.MoveElement(objectPath, field.Key, index, index - 1), null, ChangesShape: true) { FieldPath = objectPath.Key(field.Key).Text },
            1 => new ContentInspectorEdit($"Move {ContentFieldLabels.LabelOf(field)}[{index}] down", record => record.MoveElement(objectPath, field.Key, index, index + 1), null, ChangesShape: true) { FieldPath = objectPath.Key(field.Key).Text },
            _ => new ContentInspectorEdit($"Remove {ContentFieldLabels.LabelOf(field)}[{index}]", record => record.RemoveElement(objectPath, field.Key, index), null, ChangesShape: true) { FieldPath = path.Text },
        });

        if (!isExpanded)
        {
            return;
        }

        if (elementKind == ContentFieldKind.Polymorphic)
        {
            BuildElementTypePicker(element, field, objectPath, path, index, depth + 1);
        }

        if (ContentSchemas.OfValue(types, field, element) is { } elementSchema)
        {
            BuildObject(element, elementSchema, path, depth + 1, isRoot: false);
        }
    }

    private void BuildElementTypePicker(JsonElement element, ContentField field, ContentPath objectPath, ContentPath path, int index, int depth)
    {
        var members = types.MembersOf(field.ValueType!);
        var choices = members.Select(member => member.Name).ToList();
        var oldSchema = ContentSchemas.MemberSchema(types, field.ValueType!, element);
        var name = ContentSchemas.DiscriminatorOf(element);
        var typePath = path.Key(ContentWriter.DiscriminatorKey);
        var row = AddFieldRow(typePath, new FieldEditorDescription("Type", FieldValueKind.Choice) { Depth = depth, Choices = choices }, FieldValue.OfChoice(name is null ? -1 : choices.IndexOf(name)));
        row.Committed += chosen =>
        {
            var chosenName = choices[chosen.ChoiceIndex];
            if (types.TryGetMember(field.ValueType!, chosenName, out var codec))
            {
                Request(new ContentInspectorEdit($"Make {ContentFieldLabels.LabelOf(field)}[{index}] {chosenName}", record => record.ChangeElementType(objectPath, field.Key, index, chosenName, oldSchema, codec.Schema), null, ChangesShape: true) { FieldPath = path.Text });
            }
        };
    }

    private void BuildScalarElement(JsonElement element, ContentField field, ContentFieldKind elementKind, ContentPath objectPath, ContentPath path, int index, int depth)
    {
        var values = ValuesFor(field, elementKind, null);
        if (!values.CanShow(element))
        {
            AddRawRow($"[{index}]", element, objectPath, field.Key, depth, index);
            return;
        }

        var row = AddFieldRow(path, values.Describe($"[{index}]", depth, isSet: true, values.ChoicesFor(element)), values.ValueOf(element));
        row.Committed += committed =>
        {
            if (values.JsonOf(committed) is { } node)
            {
                Request(new ContentInspectorEdit($"Change {ContentFieldLabels.LabelOf(field)}[{index}]", record => record.ReplaceElement(objectPath, field.Key, index, node.DeepClone()), path.Text, ChangesShape: false));
            }
        };
    }

    private void BuildScalarField(JsonElement? value, ContentField field, ContentFieldKind kind, ContentSchema schema, ContentPath objectPath, ContentPath path, int depth)
    {
        var values = ValuesFor(field, kind, schema);
        if (value is { } present && !values.CanShow(present))
        {
            AddRawRow(ContentFieldLabels.LabelOf(field), present, objectPath, field.Key, depth);
            return;
        }

        var isSet = value is not null;
        var isIdentity = objectPath.IsRoot && field.Key == ContentTable.IdKey && field.Kind == ContentFieldKind.Guid;
        var description = values.Describe(field.DisplayName, depth, isSet, values.ChoicesFor(value));
        if (isIdentity)
        {
            description = description with { Kind = FieldValueKind.ReadOnlyText, Hint = IdHint };
        }

        var row = AddFieldRow(path, description, values.ValueOf(value));
        row.Committed += committed =>
        {

            if (values.JsonOf(committed) is not { } node)
            {
                return;
            }

            row.MarkSet();
            Request(new ContentInspectorEdit($"Change {ContentFieldLabels.LabelOf(field)}", record => record.SetField(objectPath, schema, field.Key, node.DeepClone()), path.Text, ChangesShape: false));
        };
    }

    private ContentFieldValues ValuesFor(ContentField field, ContentFieldKind kind, ContentSchema? schema)
    {
        var textChoices = kind == ContentFieldKind.Text && schema is not null ? source.TextChoices(schema, field) : null;
        var references = kind == ContentFieldKind.Reference && field.ReferencedTable is { } table ? source.ReferenceChoices(table) : [];
        var tagNames = kind is ContentFieldKind.Tag or ContentFieldKind.TagSet ? ContentSchemas.TagNames(types, field.TagDomain) : [];
        return ContentFieldValues.For(field, kind, textChoices, references, tagNames);
    }

    /// <summary>A new element for a list of field: an empty object, the family's first member, or an empty scalar; null when the list's kind can't hold one.</summary>
    private JsonNode? NewElement(ContentField field) => field.ElementKind switch
    {
        ContentFieldKind.Object => new JsonObject(),
        ContentFieldKind.Polymorphic when types.MembersOf(field.ValueType!) is { Count: > 0 } members => new JsonObject { [ContentWriter.DiscriminatorKey] = members[0].Name },
        ContentFieldKind.Text => JsonValue.Create(string.Empty),
        ContentFieldKind.Reference when field.ReferencedTable is { } table && source.ReferenceChoices(table) is { Count: > 0 } choices => JsonValue.Create(ContentGuid.Format(choices[0].Id)),
        ContentFieldKind.Guid => JsonValue.Create(ContentGuid.Format(Guid.Empty)),
        _ => null,
    };

    /// <summary>A value shown as written -- the wrong kind for its field, or under a key no schema declares -- read-only.</summary>
    private void AddRawRow(string label, JsonElement value, ContentPath objectPath, string key, int depth, int elementIndex = -1)
    {
        var path = elementIndex >= 0 ? objectPath.Key(key).Element(elementIndex) : objectPath.Key(key);
        AddFieldRow(path, new FieldEditorDescription(label, FieldValueKind.ReadOnlyText) { Depth = depth }, FieldValue.OfText(value.GetRawText()));
    }

    private FieldRow AddFieldRow(ContentPath path, FieldEditorDescription description, FieldValue value)
    {
        var host = _host!;
        var row = elementPool.CreateElement<FieldRow>(host, RowOptions(FieldRow.HeightOf(description), WindowPalette.PanelBackgroundColor));
        row.Configure(description, value);
        host.AddChild(row);
        _rowsByPath[path.Text] = row;
        row.RevertRequested += () => RequestRevertOf(row);
        return row;
    }

    private FieldSectionHeader AddHeader(ContentPath path, string title, int depth, bool isCollapsible, bool isExpanded, IReadOnlyList<string> buttons, IReadOnlyList<bool>? buttonsEnabled = null, string? hint = null, bool buttonsFollowTitle = false)
    {
        var host = _host!;
        var header = elementPool.CreateElement<FieldSectionHeader>(host, RowOptions(FieldRow.RowHeight, WindowPalette.HeaderBackground));
        header.Configure(title, depth, isCollapsible, isExpanded, buttons, buttonsEnabled, hint, buttonsFollowTitle);
        header.ExpandToggled += expanded =>
        {
            if (expanded)
            {
                _collapsed.Remove(path.Text);
            }
            else
            {
                _collapsed.Add(path.Text);
            }

            _rebuildRequested = true;
        };
        host.AddChild(header);
        _rowsByPath[path.Text] = header;
        header.RevertRequested += () => RequestRevertOf(header);
        return header;
    }

    private void AddNote(string note, int depth)
    {
        var host = _host!;
        var line = elementPool.CreateElement<TextWindow>(host, new ElementOptions
        {
            Layout = new ElementLayoutOptions { Size = new Vector2(RowWidth, NoteHeight), MinimumSize = new Vector2(RowWidth, NoteHeight), MaximumSize = new Vector2(RowWidth, NoteHeight), DisplayMode = ElementDisplayMode.Fixed, IsTransparent = true },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Text = new TextOptions { Text = new string(' ', depth * 3) + note, TextColor = WindowPalette.GhostText },
        });
        host.AddChild(line);
    }

    private void AddDecoration(ContentInspectorDecoration decoration, ContentPath objectPath, int depth)
    {
        var host = _host!;
        AddHeader(objectPath.Key("#" + decoration.Title), decoration.Title, depth, isCollapsible: false, isExpanded: true, []);
        var size = new Vector2(RowWidth, decoration.Height + DecorationGap);
        var decorationHost = elementPool.CreateElement<Window>(host, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { Size = size, MinimumSize = size, MaximumSize = size, DisplayMode = ElementDisplayMode.Fixed, IsTransparent = true },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
        });
        decorationHost.ContentPadding = new Vector2(FieldSectionHeader.TextInset + (depth + 1) * FieldSectionHeader.IndentWidth, 0);
        host.AddChild(decorationHost);
        _decorations.Add((decorationHost, decoration, objectPath));
    }

    private ElementOptions RowOptions(float height, Color background)
    {
        var size = new Vector2(RowWidth, height);
        return new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { Size = size, MinimumSize = size, MaximumSize = size, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = background },
        };
    }

    private void Request(ContentInspectorEdit edit) => EditRequested?.Invoke(edit);

    private void ApplyProblems()
    {
        var problemsByRow = new Dictionary<Element, List<ContentFieldProblem>>();
        foreach (var problem in _problems)
        {
            if (RowAtOrAbove(problem.Path) is { } row)
            {
                if (!problemsByRow.TryGetValue(row, out var list))
                {
                    list = [];
                    problemsByRow[row] = list;
                }

                list.Add(problem);
            }
        }

        foreach (var row in _rowsByPath.Values.Distinct())
        {
            var problems = problemsByRow.GetValueOrDefault(row);
            var message = problems is null ? null : string.Join("\n", problems.Select(problem => problem.Message));
            var isWarning = problems is not null && problems.All(problem => problem.IsWarning);
            switch (row)
            {
                case FieldRow fieldRow:
                    fieldRow.SetProblem(message, isWarning);
                    break;
                case FieldSectionHeader header:
                    header.SetProblem(message, isWarning);
                    break;
            }
        }
    }

    /// <summary>
    /// Marks each row whose value differs from the comparison's baseline, with the baseline's value in its tooltip and a
    /// revert button, and every heading above one, counting while closed what it hides; clears every other row's mark.
    /// </summary>
    private void ApplyDifferences()
    {
        _ownDifferences.Clear();
        var changesInside = new Dictionary<Element, int>();
        foreach (var difference in _comparison?.Differences ?? [])
        {
            var place = difference.CurrentPlace;
            var above = place ?? difference.RemovedFrom;
            if (place is not null && _rowsByPath.TryGetValue(place.Text, out var row))
            {
                _ownDifferences.TryAdd(row, difference);
                above = place.IsRoot ? null : place.Parent;
            }

            for (var ancestor = above; ancestor is not null; ancestor = ancestor.IsRoot ? null : ancestor.Parent)
            {
                if (_rowsByPath.TryGetValue(ancestor.Text, out var holder))
                {
                    changesInside[holder] = changesInside.GetValueOrDefault(holder) + 1;
                }
            }
        }

        var revertTooltip = _comparison is { } comparison ? $"Revert to {comparison.BaselineName}" : null;
        foreach (var row in _rowsByPath.Values.Distinct())
        {
            var hasOwn = _ownDifferences.TryGetValue(row, out var own);
            var inside = changesInside.GetValueOrDefault(row);
            var isMarked = hasOwn || inside > 0;
            var note = hasOwn ? NoteFor(own!) : inside > 0 ? $"{inside} changed inside" : null;
            switch (row)
            {
                case FieldRow fieldRow:
                    fieldRow.SetChangeMarker(isMarked, note, hasOwn ? revertTooltip : null);
                    break;
                case FieldSectionHeader header:
                    header.SetChangeMarker(isMarked, note, inside, hasOwn ? revertTooltip : null);
                    break;
            }
        }
    }

    /// <summary>
    /// Hides every row but the changed ones: a row stays when a difference is at it, inside it (a heading above a change)
    /// or above it (a changed value's own rows), and a removed row always does; notes and previews go. With nothing
    /// changed, a line says so.
    /// </summary>
    private void HideUnchangedRows(Window host)
    {
        var differences = _comparison?.Differences ?? [];
        var changedPlaces = differences.Select(difference => difference.CurrentPlace).OfType<ContentPath>().Where(place => !place.IsRoot).ToList();
        var removedFrom = differences.Where(difference => difference.CurrentPlace is null).Select(difference => difference.RemovedFrom).OfType<ContentPath>().ToList();
        var pathsByRow = _rowsByPath.ToDictionary(entry => entry.Value, entry => entry.Key);
        foreach (var child in host.ChildElements.ToList())
        {
            var keeps = _removedRows.Contains(child)
                || pathsByRow.TryGetValue(child, out var pathText) && ContentPath.TryParse(pathText, out var path)
                    && (changedPlaces.Any(place => place.IsWithin(path) || path.IsWithin(place)) || removedFrom.Any(container => container.IsWithin(path)));
            if (!keeps)
            {
                child.IsVisible = false;
            }
        }

        if (differences.Count == 0)
        {
            AddNote(_comparison is { } comparison ? $"Nothing differs from {comparison.BaselineName}." : "Nothing to compare with.", depth: 0);
        }
    }

    /// <summary>What a row's tooltip says of its difference: what the baseline holds there, or that it was added or moved.</summary>
    private string NoteFor(ContentFieldDifference difference)
    {
        var baselineName = _comparison!.BaselineName;
        return difference.Kind switch
        {
            ContentDifferenceKind.Added => $"Added: {baselineName} doesn't have it",
            ContentDifferenceKind.Moved => $"Moved: {baselineName} has it at [{difference.BaselinePath!.Last.Index}]",
            _ => $"{baselineName}: {DescribeBaseline(difference)}",
        };
    }

    /// <summary>The baseline's value as a reader reads it (ContentDifferenceText).</summary>
    private string DescribeBaseline(ContentFieldDifference difference)
    {
        var field = difference.CurrentPlace is { } place && _record is { } record ? ContentSchemas.FieldAt(types, record.ValueType, record.Root, place) : null;
        return ContentDifferenceText.Value(difference.BaselineJson, field, RecordName);
    }

    /// <summary>The name a reference picker shows for id of tableName; null when it lists none.</summary>
    public string? RecordName(string tableName, Guid id)
    {
        foreach (var choice in source.ReferenceChoices(tableName))
        {
            if (choice.Id == id)
            {
                return choice.Label;
            }
        }

        return null;
    }

    /// <summary>Asks for row's own difference to be put back as the baseline has it, as one edit.</summary>
    private void RequestRevertOf(Element row)
    {
        if (_ownDifferences.TryGetValue(row, out var difference))
        {
            RequestRevert(difference);
        }
    }

    /// <summary>Asks for difference to be put back as the baseline has it, as one edit -- what a row's revert button does, for anything else that offers it (the Changes list).</summary>
    public void RequestRevert(ContentFieldDifference difference)
    {
        var baselineName = _comparison?.BaselineName ?? "the baseline";
        var fieldPath = (difference.CurrentPlace ?? difference.RemovedFrom)?.Text;
        Request(new ContentInspectorEdit($"Revert {difference.DisplayPath} to {baselineName}", record => ContentRecordRevert.Revert(record, types, difference), null, ChangesShape: true) { FieldPath = fieldPath });
    }

    private void RefillDecorations()
    {
        if (_decorations.Count == 0 || _record is not { } record)
        {
            return;
        }

        using var document = JsonDocument.Parse(record.Json);
        foreach (var (host, decoration, objectPath) in _decorations)
        {
            elementPool.CloseAllChildren(host);
            if (ElementAt(document.RootElement, objectPath) is { ValueKind: JsonValueKind.Object } objectJson)
            {
                decoration.Fill(host, objectJson, host.ContentSize.X);
            }
        }
    }

    private static JsonElement? ElementAt(JsonElement root, ContentPath path)
    {
        var current = root;
        foreach (var segment in path.Segments)
        {
            if (segment.IsIndex)
            {
                if (current.ValueKind != JsonValueKind.Array || segment.Index >= current.GetArrayLength())
                {
                    return null;
                }

                current = current[segment.Index];
                continue;
            }

            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment.Key!, out current))
            {
                return null;
            }
        }

        return current;
    }
}
