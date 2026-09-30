using Engine.Tags;
using Game.Modules;
using Game.Modules.Actions;
using Game.Modules.Actions.Effects;

namespace Game.Tags;

/// <summary>Checks that every gameplay tag the build's registered content uses is declared by one of its modules.</summary>
/// <remarks>
/// Run once every module is configured, so a mod whose content names an undeclared tag (a typo, or one it forgot to
/// declare) fails its dry run instead of silently never matching. Covers every action and item definition registered
/// by then: their tags and their stat modifier grants' conditions.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
internal static class ContentTagValidation
{
    /// <exception cref="InvalidOperationException">A definition uses a tag no module declared, naming both.</exception>
    public static void EnsureDeclared(GameModuleContext context)
    {
        var gameplayTags = context.GameplayTags;

        foreach (var action in context.Actions.Definitions)
        {
            EnsureDeclared(gameplayTags, action, $"Action '{action.Name}'");
        }

        foreach (var item in context.Items.Definitions)
        {
            EnsureDeclared(gameplayTags, item, $"Item '{item.Name}'");
        }
    }

    private static void EnsureDeclared(GameplayTagRegistry gameplayTags, ActivatableDefinition definition, string usedBy)
    {
        gameplayTags.EnsureDeclared(definition.Tags, usedBy);

        foreach (var effect in definition.Effects)
        {
            foreach (var entry in effect.Entries)
            {
                if (entry is StatModifierGrant { ConditionTag.IsNone: false } grant && !gameplayTags.IsDeclared(grant.ConditionTag))
                {
                    throw new InvalidOperationException($"{usedBy} grants a stat modifier conditioned on gameplay tag '{grant.ConditionTag}', which no module in this build declares.");
                }
            }
        }
    }
}
