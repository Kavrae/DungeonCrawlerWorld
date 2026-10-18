using Engine.Tags;

namespace Builders.Browsing;

/// <summary>Groups a browser's records under headings in a fixed order, sorted by name within each, keeping only those a search matches.</summary>
public static class RecordBrowserSections
{
    /// <summary>Sections in headingsInOrder's order, empty ones left out; search matches a name or a tag's display name, ignoring case, and an empty search matches everything.</summary>
    /// <param name="headingOf">Which of headingsInOrder a record goes under.</param>
    public static IReadOnlyList<RecordBrowserSection<TRecord>> Build<TRecord>(
        IEnumerable<TRecord> records,
        IReadOnlyList<string> headingsInOrder,
        Func<TRecord, string> headingOf,
        Func<TRecord, string> nameOf,
        Func<TRecord, GameplayTagSet> tagsOf,
        string search,
        GameplayTagRegistry gameplayTags)
    {
        var recordsByHeading = headingsInOrder.ToDictionary(heading => heading, _ => new List<TRecord>());
        foreach (var record in records)
        {
            if (Matches(nameOf(record), tagsOf(record), search, gameplayTags))
            {
                recordsByHeading[headingOf(record)].Add(record);
            }
        }

        var sections = new List<RecordBrowserSection<TRecord>>();
        foreach (var heading in headingsInOrder)
        {
            var sectionRecords = recordsByHeading[heading];
            if (sectionRecords.Count == 0)
            {
                continue;
            }

            sectionRecords.Sort((left, right) => string.Compare(nameOf(left), nameOf(right), StringComparison.OrdinalIgnoreCase));
            sections.Add(new RecordBrowserSection<TRecord>(heading, sectionRecords));
        }

        return sections;
    }

    private static bool Matches(string name, GameplayTagSet tags, string search, GameplayTagRegistry gameplayTags)
    {
        if (search.Length == 0 || name.Contains(search, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var tag in tags)
        {
            if (gameplayTags.GetDisplayName(tag).Contains(search, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
