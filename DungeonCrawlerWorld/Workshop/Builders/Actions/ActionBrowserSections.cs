using Builders.Browsing;
using Engine.Tags;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;

namespace Builders.Actions;

/// <summary>The Actions browser's groups: toggles first by what they are, then by activator.</summary>
public static class ActionBrowserSections
{
    public static IReadOnlyList<string> HeadingsInOrder { get; } = ["Abilities", "Spells", "Toggles", "Other"];

    public static IReadOnlyList<RecordBrowserSection<ActionDefinition>> Build(IEnumerable<ActionDefinition> actions, string search, GameplayTagRegistry gameplayTags) =>
        RecordBrowserSections.Build(actions, HeadingsInOrder, HeadingOf, static action => action.Name, static action => action.Tags, search, gameplayTags);

    private static string HeadingOf(ActionDefinition action)
    {
        if (action.Toggle is not null)
        {
            return "Toggles";
        }

        return action.Activator switch
        {
            SpellActivator => "Spells",
            DirectAction => "Abilities",
            _ => "Other",
        };
    }
}
