using UIEngine.UI.Filtering;

namespace Builders.Browsing;

/// <summary>The filter groups browsers share: where a record came from, and which of a fixed set of values a record has.</summary>
/// <remarks>Both are any-of groups -- a record has exactly one source, one value -- headed so the menu says so ("Source: any of").</remarks>
public static class RecordFilterGroups
{
    public const string SourceGroupId = "source";
    public const string MasterChoiceId = "source:master";
    public const string MasterLabel = "Master";

    private const string AnyOfSuffix = ": any of";
    private const string ModChoicePrefix = "source:mod:";

    /// <summary>Source: Master, then each mod that supplied any of records, by name -- a mod with no record of the table isn't offered.</summary>
    /// <param name="modOf">The mod a record came from; null for master.</param>
    public static ListFilterGroup<TRecord> Source<TRecord>(IEnumerable<TRecord> records, Func<TRecord, string?> modOf)
    {
        var mods = records.Select(modOf).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.OrdinalIgnoreCase).ToList();
        List<ListFilterChoice<TRecord>> choices = [new(MasterChoiceId, MasterLabel, record => modOf(record) is null)];
        choices.AddRange(mods.Select(mod => new ListFilterChoice<TRecord>(ModChoiceId(mod), mod, record => string.Equals(modOf(record), mod, StringComparison.Ordinal))));
        return new ListFilterGroup<TRecord>(SourceGroupId, "Source" + AnyOfSuffix, ListFilterGroupMatch.AnyOf, choices);
    }

    /// <summary>The choice id naming the mod modName.</summary>
    public static string ModChoiceId(string modName) => ModChoicePrefix + modName;

    /// <summary>A group of the values a record can have, in values' order, each choice the records whose valueOf is it; ids "{groupId}:{value}", lower case.</summary>
    public static ListFilterGroup<TRecord> OneOf<TRecord>(string groupId, string heading, IReadOnlyList<string> values, Func<TRecord, string> valueOf) =>
        new(groupId, heading + AnyOfSuffix, ListFilterGroupMatch.AnyOf,
            [.. values.Select(value => new ListFilterChoice<TRecord>($"{groupId}:{value.ToLowerInvariant()}", value, record => string.Equals(valueOf(record), value, StringComparison.Ordinal)))]);
}
