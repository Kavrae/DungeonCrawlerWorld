using Builders.Browsing;
using Engine.Tags;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;

namespace Builders.Actions;

/// <summary>The Actions browser's groups -- toggles first by what they are, then by activator -- and the values its filters choose between.</summary>
public static class ActionBrowserSections
{
    public static IReadOnlyList<string> HeadingsInOrder { get; } = ["Abilities", "Spells", "Toggles", "Other"];

    public static IReadOnlyList<string> TimingsInOrder { get; } = ["FreeCast", "Immediate", "Delayed"];

    public static IReadOnlyList<string> CoreChoicesInOrder { get; } = [CoreChoice, NotCoreChoice];

    private const string CoreChoice = "Core";
    private const string NotCoreChoice = "Not core";

    public static IReadOnlyList<RecordBrowserSection<ActionDefinition>> Build(IEnumerable<ActionDefinition> actions, string search, GameplayTagRegistry gameplayTags) =>
        RecordBrowserSections.Build(actions, HeadingsInOrder, HeadingOf, static action => action.Name, static action => action.Tags, search, gameplayTags);

    /// <summary>The heading action is listed under: Toggles for a toggle, else by activator.</summary>
    public static string HeadingOf(ActionDefinition action)
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

    /// <summary>The Timing filter's value for action: its timing's category.</summary>
    public static string TimingOf(ActionDefinition action) => action.Activator.Timing.Category switch
    {
        ActionTimingCategory.FreeCast => "FreeCast",
        ActionTimingCategory.Delayed => "Delayed",
        _ => "Immediate",
    };

    /// <summary>The Core filter's value for action: whether every crawler has it (ActionDefinition.Core).</summary>
    public static string CoreChoiceOf(ActionDefinition action) => action.Core ? CoreChoice : NotCoreChoice;
}
