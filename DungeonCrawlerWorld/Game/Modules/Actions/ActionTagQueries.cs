using Engine.Tags;
using Game.Tags;

namespace Game.Modules.Actions;

/// <summary>Action-category queries over an entity's actions: the Actions window's per-category tabs.</summary>
public static class ActionTagQueries
{
    /// <summary>For every action category entityId's actions other than Core ones fall in, how many actions are in it, most populated first.</summary>
    /// <remarks>
    /// A category is an Action category tag (GameTagDomains.ActionCategory) other than the Action root itself:
    /// Action.Attack, Spell and anything below either. Each action counts once under each of its effective definition's
    /// categories, implied tags included, and under every ancestor of those that is one -- so a spell counts under Spell.
    /// Core actions have a tab of their own and count nowhere here. Ties are broken by display name, then full name, for
    /// a stable tab order. A category no action falls in produces no entry.
    /// </remarks>
    public static List<(GameplayTag Tag, int Count)> GetActionCategoryCounts(EntityActions entityActions, GameplayTagRegistry gameplayTags, int entityId)
    {
        var actionIds = new List<Guid>();
        entityActions.CopyActionIds(entityId, actionIds);

        var counts = new Dictionary<GameplayTag, int>();
        var actionCategories = new HashSet<GameplayTag>();
        foreach (var actionId in actionIds)
        {
            if (entityActions.IsCore(actionId) || !entityActions.TryGetEffectiveAction(entityId, actionId, out var definition))
            {
                continue;
            }

            actionCategories.Clear();
            foreach (var tag in definition.Tags)
            {
                for (var category = tag; IsCategory(gameplayTags, category); category = category.Parent)
                {
                    actionCategories.Add(category);
                }
            }

            foreach (var category in actionCategories)
            {
                counts[category] = counts.GetValueOrDefault(category) + 1;
            }
        }

        var result = new List<(GameplayTag Tag, int Count)>(counts.Count);
        foreach (var (tag, count) in counts)
        {
            result.Add((tag, count));
        }

        result.Sort((a, b) =>
        {
            var byCount = b.Count.CompareTo(a.Count);
            if (byCount != 0)
            {
                return byCount;
            }

            var byDisplayName = string.CompareOrdinal(gameplayTags.GetDisplayName(a.Tag), gameplayTags.GetDisplayName(b.Tag));
            return byDisplayName != 0 ? byDisplayName : string.CompareOrdinal(a.Tag.Name, b.Tag.Name);
        });

        return result;
    }

    private static bool IsCategory(GameplayTagRegistry gameplayTags, GameplayTag tag) =>
        tag != GameTags.Action && gameplayTags.Accepts(GameTagDomains.ActionCategory, tag);
}
