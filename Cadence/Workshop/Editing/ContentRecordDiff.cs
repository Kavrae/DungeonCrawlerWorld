using System.Text.Json;
using Engine.Content;

namespace Workshop.Editing;

/// <summary>How a value differs between a record's baseline and its current version.</summary>
public enum ContentDifferenceKind : byte
{
    /// <summary>Both versions hold it, with different values (a value omitted on one side compared as its default).</summary>
    Changed,

    /// <summary>Only the current version holds it: a key with no default, or a list element.</summary>
    Added,

    /// <summary>Only the baseline holds it.</summary>
    Removed,

    /// <summary>A list element both versions hold, equal, at another place in the list.</summary>
    Moved,
}

/// <summary>One value that differs between two versions of a record: where it is on each side, its place as an editor names it, and what each side writes.</summary>
/// <param name="BaselinePath">Where the baseline holds it; null for Added.</param>
/// <param name="CurrentPath">Where the current version holds it; null for Removed.</param>
/// <param name="DisplayPath">The field labels down to it, joined by " > ", as a problem names its field.</param>
/// <param name="BaselineJson">The value as the baseline writes it; null when the baseline leaves it out (for Changed: it means its default).</param>
/// <param name="CurrentJson">The value as the current version writes it; null when it is left out.</param>
/// <param name="RemovedFrom">For Removed: the object or list in the current version it was taken from; null otherwise.</param>
public sealed record ContentFieldDifference(ContentDifferenceKind Kind, ContentPath? BaselinePath, ContentPath? CurrentPath, string DisplayPath, string? BaselineJson, string? CurrentJson, ContentPath? RemovedFrom = null)
{
    /// <summary>Where it is now, or where the baseline held it when it is gone.</summary>
    public ContentPath Path => CurrentPath ?? BaselinePath!;

    /// <summary>A list element only the baseline holds.</summary>
    public bool IsRemovedElement => Kind == ContentDifferenceKind.Removed && BaselinePath is { IsRoot: false } path && path.Last.IsIndex;

    /// <summary>Where the current version holds it or would hold it: CurrentPath, or for a removed key its place in RemovedFrom; null for a removed list element, which has no place of its own.</summary>
    public ContentPath? CurrentPlace => CurrentPath ?? (Kind == ContentDifferenceKind.Removed && !IsRemovedElement && RemovedFrom is { } container ? container.Key(BaselinePath!.Last.Key!) : null);
}

/// <summary>A record compared with a baseline: what the baseline is called ("Master") and every difference from it.</summary>
public sealed record ContentComparison(string BaselineName, IReadOnlyList<ContentFieldDifference> Differences);

/// <summary>
/// Compares two versions of one record field by field, the way its codecs read them: what a reader would read the same
/// is never a difference, and every difference is named at the deepest value that has it.
/// </summary>
/// <remarks>
/// Read from JSON, not from decoded definitions, so a record that doesn't decode, a key no schema declares and a value of
/// the wrong kind compare too (the last two as raw JSON). An omitted field equals its default, key order never matters,
/// numbers compare by value, a tag set by its tags, and an omitted list equals an empty one. A polymorphic value whose
/// "$" changed is one difference, the whole value. A list's elements are matched equal first, in order (a longest
/// common subsequence), then equal ones out of order (Moved); between two matched elements, what is left on each side
/// pairs up in order as Changed and is compared inside -- in a polymorphic list only two of the same member, since one
/// of another type is another element -- and the rest is Added or Removed. It names no layer: the
/// baseline is whatever the caller compares with.
/// </remarks>
public static class ContentRecordDiff
{
    private static readonly ContentField DisabledField = ContentField.Boolean(ContentTable.DisabledKey, "Disabled");
    private static readonly ContentField ReplacementField = ContentField.Guid(ContentTable.ReplacementKey, "Replaced by");

    /// <summary>Every difference between baselineJson and currentJson, both records read as valueType's codec reads them, in the order the schema writes them.</summary>
    public static IReadOnlyList<ContentFieldDifference> Compare(string baselineJson, string currentJson, Type valueType, ContentTypeRegistry types)
    {
        using var baseline = JsonDocument.Parse(baselineJson);
        using var current = JsonDocument.Parse(currentJson);
        var differences = new List<ContentFieldDifference>();
        var schema = types.GetValueCodec(valueType).Schema;
        new Walker(types, differences).RecordRoot(baseline.RootElement, current.RootElement, schema);
        return differences;
    }

    private sealed class Walker(ContentTypeRegistry types, List<ContentFieldDifference> differences)
    {
        public void RecordRoot(JsonElement baseline, JsonElement current, ContentSchema schema)
        {
            var root = new Sides(ContentPath.Root, ContentPath.Root, string.Empty);
            if (baseline.ValueKind != JsonValueKind.Object || current.ValueKind != JsonValueKind.Object)
            {
                Raw(baseline, current, root);
                return;
            }

            Object(baseline, current, schema, root, isRecordRoot: true, isPolymorphicMember: false);
        }

        private void Object(JsonElement baseline, JsonElement current, ContentSchema schema, Sides sides, bool isRecordRoot, bool isPolymorphicMember)
        {
            foreach (var field in schema.Fields)
            {
                Field(baseline, current, field, sides);
            }

            if (isRecordRoot)
            {
                Field(baseline, current, DisabledField, sides);
                Field(baseline, current, ReplacementField, sides);
            }

            foreach (var key in UndeclaredKeys(baseline, current, schema, isRecordRoot, isPolymorphicMember))
            {
                var keySides = sides.Key(key, key);
                var hasBaseline = baseline.TryGetProperty(key, out var baselineValue);
                var hasCurrent = current.TryGetProperty(key, out var currentValue);
                Present(hasBaseline ? baselineValue : null, hasCurrent ? currentValue : null, keySides, (left, right) => Raw(left, right, keySides));
            }
        }

        private static IEnumerable<string> UndeclaredKeys(JsonElement baseline, JsonElement current, ContentSchema schema, bool isRecordRoot, bool isPolymorphicMember)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in baseline.EnumerateObject().Concat(current.EnumerateObject()))
            {
                if (schema.HasKey(property.Name)
                    || (isRecordRoot && property.Name is ContentTable.DisabledKey or ContentTable.ReplacementKey)
                    || (isPolymorphicMember && property.Name == ContentWriter.DiscriminatorKey)
                    || !seen.Add(property.Name))
                {
                    continue;
                }

                yield return property.Name;
            }
        }

        private void Field(JsonElement baselineObject, JsonElement currentObject, ContentField field, Sides objectSides)
        {
            var hasBaseline = baselineObject.TryGetProperty(field.Key, out var baselineValue);
            var hasCurrent = currentObject.TryGetProperty(field.Key, out var currentValue);
            if (!hasBaseline && !hasCurrent)
            {
                return;
            }

            var sides = objectSides.Key(field.Key, ContentFieldLabels.LabelOf(field));
            switch (field.Kind)
            {
                case ContentFieldKind.List:
                    List(hasBaseline ? baselineValue : null, hasCurrent ? currentValue : null, field, sides);
                    return;
                case ContentFieldKind.TagSet:
                    TagSet(hasBaseline ? baselineValue : null, hasCurrent ? currentValue : null, sides);
                    return;
                case ContentFieldKind.Object or ContentFieldKind.Polymorphic:
                    Present(hasBaseline ? baselineValue : null, hasCurrent ? currentValue : null, sides, (left, right) => Value(left, right, field.Kind, field, sides));
                    return;
            }

            var defaultValue = DefaultOf(field);
            var baselineEffective = hasBaseline ? baselineValue : defaultValue;
            var currentEffective = hasCurrent ? currentValue : defaultValue;
            if (baselineEffective is null || currentEffective is null)
            {
                Present(baselineEffective, currentEffective, sides, static (_, _) => { });
                return;
            }

            if (!ScalarsEqual(baselineEffective.Value, currentEffective.Value, field.Kind))
            {
                Add(ContentDifferenceKind.Changed, sides, hasBaseline ? baselineValue : null, hasCurrent ? currentValue : null);
            }
        }

        /// <summary>Added or Removed when only one side holds a value, compare when both do.</summary>
        private void Present(JsonElement? baseline, JsonElement? current, Sides sides, Action<JsonElement, JsonElement> compare)
        {
            if (baseline is { } left && current is { } right)
            {
                compare(left, right);
                return;
            }

            if (baseline is not null)
            {
                Add(ContentDifferenceKind.Removed, sides with { CurrentPath = null }, baseline, null, removedFrom: sides.CurrentPath?.Parent);
            }
            else if (current is not null)
            {
                Add(ContentDifferenceKind.Added, sides with { BaselinePath = null }, null, current);
            }
        }

        /// <summary>Compares one value both sides hold, of kind: an object or polymorphic value inside, anything else as a scalar.</summary>
        private void Value(JsonElement baseline, JsonElement current, ContentFieldKind kind, ContentField field, Sides sides)
        {
            switch (kind)
            {
                case ContentFieldKind.Object:
                    if (baseline.ValueKind == JsonValueKind.Object && current.ValueKind == JsonValueKind.Object
                        && field.ValueType is { } objectType && types.TryGetValueCodec(objectType, out var codec))
                    {
                        Object(baseline, current, codec.Schema, sides, isRecordRoot: false, isPolymorphicMember: false);
                        return;
                    }

                    Raw(baseline, current, sides);
                    return;
                case ContentFieldKind.Polymorphic:
                    Polymorphic(baseline, current, field, sides);
                    return;
                default:
                    if (!ScalarsEqual(baseline, current, kind))
                    {
                        Add(ContentDifferenceKind.Changed, sides, baseline, current);
                    }

                    return;
            }
        }

        private void Polymorphic(JsonElement baseline, JsonElement current, ContentField field, Sides sides)
        {
            var baselineName = ContentSchemas.DiscriminatorOf(baseline);
            var currentName = ContentSchemas.DiscriminatorOf(current);
            if (baselineName is null || baselineName != currentName || ContentSchemas.MemberSchema(types, field.ValueType!, current) is not { } member)
            {
                Raw(baseline, current, sides);
                return;
            }

            Object(baseline, current, member, sides with { DisplayPath = $"{sides.DisplayPath} ({currentName})" }, isRecordRoot: false, isPolymorphicMember: true);
        }

        private void TagSet(JsonElement? baseline, JsonElement? current, Sides sides)
        {
            if (baseline is { ValueKind: not JsonValueKind.Array } || current is { ValueKind: not JsonValueKind.Array })
            {
                Present(baseline, current, sides, (left, right) => Raw(left, right, sides));
                return;
            }

            if (!TagNames(baseline).SetEquals(TagNames(current)))
            {
                Add(ContentDifferenceKind.Changed, sides, baseline, current);
            }
        }

        private static HashSet<string> TagNames(JsonElement? tags)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (tags is not { } array)
            {
                return names;
            }

            foreach (var tag in array.EnumerateArray())
            {
                names.Add(tag.ValueKind == JsonValueKind.String ? tag.GetString()! : tag.GetRawText());
            }

            return names;
        }

        private void List(JsonElement? baseline, JsonElement? current, ContentField field, Sides sides)
        {
            if (baseline is { ValueKind: not JsonValueKind.Array } || current is { ValueKind: not JsonValueKind.Array })
            {
                Present(baseline, current, sides, (left, right) => Raw(left, right, sides));
                return;
            }

            var baselineElements = baseline is { } left ? left.EnumerateArray().ToArray() : [];
            var currentElements = current is { } right ? right.EnumerateArray().ToArray() : [];
            var elementKind = field.ElementKind ?? ContentFieldKind.Object;
            var equal = new bool[baselineElements.Length, currentElements.Length];
            for (var baselineIndex = 0; baselineIndex < baselineElements.Length; baselineIndex++)
            {
                for (var currentIndex = 0; currentIndex < currentElements.Length; currentIndex++)
                {
                    equal[baselineIndex, currentIndex] = ElementsEqual(baselineElements[baselineIndex], currentElements[currentIndex], elementKind, field);
                }
            }

            var anchors = LongestCommonSubsequence(equal, baselineElements.Length, currentElements.Length);
            var baselineMatched = new bool[baselineElements.Length];
            var currentMatchedFrom = Enumerable.Repeat(-1, currentElements.Length).ToArray();
            foreach (var (baselineIndex, currentIndex) in anchors)
            {
                baselineMatched[baselineIndex] = true;
                currentMatchedFrom[currentIndex] = baselineIndex;
            }

            var movedFrom = Enumerable.Repeat(-1, currentElements.Length).ToArray();
            for (var currentIndex = 0; currentIndex < currentElements.Length; currentIndex++)
            {
                if (currentMatchedFrom[currentIndex] >= 0)
                {
                    continue;
                }

                for (var baselineIndex = 0; baselineIndex < baselineElements.Length; baselineIndex++)
                {
                    if (!baselineMatched[baselineIndex] && equal[baselineIndex, currentIndex])
                    {
                        baselineMatched[baselineIndex] = true;
                        movedFrom[currentIndex] = baselineIndex;
                        break;
                    }
                }
            }

            var baselineStart = 0;
            var currentStart = 0;
            foreach (var (anchorBaseline, anchorCurrent) in anchors.Append((baselineElements.Length, currentElements.Length)))
            {
                Gap(baselineElements, currentElements, baselineStart, anchorBaseline, currentStart, anchorCurrent, baselineMatched, movedFrom, elementKind, field, sides);
                baselineStart = anchorBaseline + 1;
                currentStart = anchorCurrent + 1;
            }
        }

        /// <summary>
        /// The elements between two matched ones: each moved element where it now is; each other current element paired,
        /// as Changed, with the next unpaired baseline element of the same kind (for a polymorphic list, the same "$"),
        /// keeping both in order; then what is left Added or Removed.
        /// </summary>
        private void Gap(JsonElement[] baselineElements, JsonElement[] currentElements, int baselineStart, int baselineEnd, int currentStart, int currentEnd, bool[] baselineMatched, int[] movedFrom, ContentFieldKind elementKind, ContentField field, Sides sides)
        {
            var unpairedBaseline = new List<int>();
            for (var baselineIndex = baselineStart; baselineIndex < baselineEnd; baselineIndex++)
            {
                if (!baselineMatched[baselineIndex])
                {
                    unpairedBaseline.Add(baselineIndex);
                }
            }

            var nextCandidate = 0;
            for (var currentIndex = currentStart; currentIndex < currentEnd; currentIndex++)
            {
                if (movedFrom[currentIndex] is var from and >= 0)
                {
                    var moved = sides.Element(from, currentIndex);
                    Add(ContentDifferenceKind.Moved, moved, baselineElements[from], currentElements[currentIndex]);
                    continue;
                }

                var pairedAt = -1;
                for (var candidate = nextCandidate; candidate < unpairedBaseline.Count; candidate++)
                {
                    if (AreSameKindOfElement(baselineElements[unpairedBaseline[candidate]], currentElements[currentIndex], elementKind))
                    {
                        pairedAt = candidate;
                        break;
                    }
                }

                if (pairedAt >= 0)
                {
                    var baselineIndex = unpairedBaseline[pairedAt];
                    unpairedBaseline.RemoveAt(pairedAt);
                    nextCandidate = pairedAt;
                    Value(baselineElements[baselineIndex], currentElements[currentIndex], elementKind, field, sides.Element(baselineIndex, currentIndex));
                    continue;
                }

                Add(ContentDifferenceKind.Added, sides.Element(-1, currentIndex) with { BaselinePath = null }, null, currentElements[currentIndex]);
            }

            foreach (var baselineIndex in unpairedBaseline)
            {
                Add(ContentDifferenceKind.Removed, sides.Element(baselineIndex, -1) with { CurrentPath = null }, baselineElements[baselineIndex], null, removedFrom: sides.CurrentPath);
            }
        }

        /// <summary>Whether two elements may be one element changed: any two of an object or scalar list, two of a polymorphic list only when they are the same member -- one of another type is another element, not this one edited.</summary>
        private static bool AreSameKindOfElement(JsonElement baseline, JsonElement current, ContentFieldKind elementKind) =>
            elementKind != ContentFieldKind.Polymorphic || ContentSchemas.DiscriminatorOf(baseline) is { } name && name == ContentSchemas.DiscriminatorOf(current);

        private static List<(int Baseline, int Current)> LongestCommonSubsequence(bool[,] equal, int baselineCount, int currentCount)
        {
            var lengths = new int[baselineCount + 1, currentCount + 1];
            for (var baselineIndex = baselineCount - 1; baselineIndex >= 0; baselineIndex--)
            {
                for (var currentIndex = currentCount - 1; currentIndex >= 0; currentIndex--)
                {
                    lengths[baselineIndex, currentIndex] = equal[baselineIndex, currentIndex]
                        ? lengths[baselineIndex + 1, currentIndex + 1] + 1
                        : System.Math.Max(lengths[baselineIndex + 1, currentIndex], lengths[baselineIndex, currentIndex + 1]);
                }
            }

            var pairs = new List<(int, int)>();
            var left = 0;
            var right = 0;
            while (left < baselineCount && right < currentCount)
            {
                if (equal[left, right])
                {
                    pairs.Add((left, right));
                    left++;
                    right++;
                }
                else if (lengths[left + 1, right] >= lengths[left, right + 1])
                {
                    left++;
                }
                else
                {
                    right++;
                }
            }

            return pairs;
        }

        private bool ElementsEqual(JsonElement baseline, JsonElement current, ContentFieldKind elementKind, ContentField field)
        {
            var scratch = new List<ContentFieldDifference>();
            var sides = new Sides(ContentPath.Root, ContentPath.Root, string.Empty);
            new Walker(types, scratch).Value(baseline, current, elementKind, field, sides);
            return scratch.Count == 0;
        }

        private void Raw(JsonElement baseline, JsonElement current, Sides sides)
        {
            if (!JsonElement.DeepEquals(baseline, current))
            {
                Add(ContentDifferenceKind.Changed, sides, baseline, current);
            }
        }

        private static bool ScalarsEqual(JsonElement baseline, JsonElement current, ContentFieldKind kind)
        {
            switch (kind)
            {
                case ContentFieldKind.Integer or ContentFieldKind.Color when baseline.ValueKind == JsonValueKind.Number && current.ValueKind == JsonValueKind.Number
                    && baseline.TryGetInt64(out var baselineInteger) && current.TryGetInt64(out var currentInteger):
                    return baselineInteger == currentInteger;
                case ContentFieldKind.Number when baseline.ValueKind == JsonValueKind.Number && current.ValueKind == JsonValueKind.Number
                    && baseline.TryGetSingle(out var baselineNumber) && current.TryGetSingle(out var currentNumber):
                    return baselineNumber.Equals(currentNumber);
                case ContentFieldKind.Guid or ContentFieldKind.Reference when baseline.ValueKind == JsonValueKind.String && current.ValueKind == JsonValueKind.String
                    && ContentGuid.TryParse(baseline.GetString()!, out var baselineId) && ContentGuid.TryParse(current.GetString()!, out var currentId):
                    return baselineId == currentId;
                default:
                    return JsonElement.DeepEquals(baseline, current);
            }
        }

        /// <summary>What an omitted field means, as JSON; null for a field whose omission holds no value.</summary>
        private static JsonElement? DefaultOf(ContentField field) => field.Default switch
        {
            null => null,
            Enum member => JsonSerializer.SerializeToElement(member.ToString()),
            var value => JsonSerializer.SerializeToElement(value, value.GetType()),
        };

        private void Add(ContentDifferenceKind kind, Sides sides, JsonElement? baseline, JsonElement? current, ContentPath? removedFrom = null) =>
            differences.Add(new ContentFieldDifference(kind, sides.BaselinePath, sides.CurrentPath, sides.DisplayPath, baseline?.GetRawText(), current?.GetRawText(), removedFrom));
    }

    /// <summary>Where the walk is on each side, and how an editor names it; a side's path is null once only the other holds the value.</summary>
    private readonly record struct Sides(ContentPath? BaselinePath, ContentPath? CurrentPath, string DisplayPath)
    {
        public Sides Key(string key, string label) =>
            new(BaselinePath?.Key(key), CurrentPath?.Key(key), DisplayPath.Length == 0 ? label : $"{DisplayPath} > {label}");

        /// <summary>Element baselineIndex on the baseline's side and currentIndex on the current one's, named by the current index when it has one.</summary>
        public Sides Element(int baselineIndex, int currentIndex) =>
            new(baselineIndex >= 0 ? BaselinePath?.Element(baselineIndex) : null, currentIndex >= 0 ? CurrentPath?.Element(currentIndex) : null, $"{DisplayPath}[{(currentIndex >= 0 ? currentIndex : baselineIndex)}]");
    }
}
