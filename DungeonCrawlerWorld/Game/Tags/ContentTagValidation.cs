using Engine.Tags;
using Game.Effects;
using Game.Modules;
using Game.Modules.Actions;
using Game.Modules.Auras;

namespace Game.Tags;

/// <summary>Checks that every gameplay tag the build's registered content uses is declared by one of its modules.</summary>
/// <remarks>
/// Run once every module is configured, so a mod whose content names an undeclared tag (a typo, or one it forgot to
/// declare) fails its dry run instead of silently never matching. Covers the tags of every action, item, terrain
/// contact and aura registered by then, and every tag an effect entry any of them holds names. The per-record forms
/// (ProblemsOf) are the same rules for one definition, which a definition made at runtime is checked with
/// (Game.Content.ContentChecks).
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
            ThrowFirst(ProblemsOf(gameplayTags, action, $"Action '{action.Name}'"));
        }

        foreach (var item in context.Items.Definitions)
        {
            ThrowFirst(ProblemsOf(gameplayTags, item, $"Item '{item.Name}'"));
        }

        var terrain = context.Terrain;
        for (var terrainTypeId = 1; terrainTypeId <= terrain.Count; terrainTypeId++)
        {
            if (terrain.TryGet((ushort)terrainTypeId, out var terrainDefinition) && terrainDefinition.Contact is { } contact)
            {
                var heldBy = $"Terrain '{terrainDefinition.Name}'";
                ThrowFirst(ProblemsOf(gameplayTags, contact.Tags, contact.Effects, heldBy));
            }
        }

        var auras = context.Auras;
        for (var auraId = 0; auraId < auras.Count; auraId++)
        {
            var aura = auras.Get((byte)auraId);
            ThrowFirst(ProblemsOf(gameplayTags, aura, $"Aura '{aura.Name}'"));
        }
    }

    /// <summary>Every tag definition or an entry it holds uses that no module declared, as a sentence naming heldBy.</summary>
    public static IReadOnlyList<string> ProblemsOf(GameplayTagRegistry gameplayTags, ActivatableDefinition definition, string heldBy)
    {
        var problems = new List<string>();
        AddUndeclared(gameplayTags, definition.Tags, heldBy, problems);
        EffectEntries.ForEach(definition, entry => AddUndeclaredReferences(gameplayTags, entry, heldBy, problems));
        return problems;
    }

    /// <summary>Every tag aura or an entry of its effects uses that no module declared, as a sentence naming heldBy.</summary>
    public static IReadOnlyList<string> ProblemsOf(GameplayTagRegistry gameplayTags, AuraDefinition aura, string heldBy) =>
        ProblemsOf(gameplayTags, aura.Tags, aura.Effects, heldBy);

    private static IReadOnlyList<string> ProblemsOf(GameplayTagRegistry gameplayTags, GameplayTagSet tags, IReadOnlyList<Effect> effects, string heldBy)
    {
        var problems = new List<string>();
        AddUndeclared(gameplayTags, tags, heldBy, problems);
        EffectEntries.ForEach(effects, entry => AddUndeclaredReferences(gameplayTags, entry, heldBy, problems));
        return problems;
    }

    private static void AddUndeclared(GameplayTagRegistry gameplayTags, GameplayTagSet tags, string heldBy, List<string> problems)
    {
        foreach (var tag in tags)
        {
            if (!gameplayTags.IsDeclared(tag))
            {
                problems.Add($"{heldBy} uses gameplay tag '{tag}', which no module in this build declares.");
            }
        }
    }

    private static void AddUndeclaredReferences(GameplayTagRegistry gameplayTags, IEffectEntry entry, string heldBy, List<string> problems)
    {
        foreach (var tag in entry.ReferencedTags)
        {
            if (!gameplayTags.IsDeclared(tag))
            {
                problems.Add($"{heldBy} has an effect that names gameplay tag '{tag}', which no module in this build declares.");
            }
        }
    }

    private static void ThrowFirst(IReadOnlyList<string> problems)
    {
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(problems[0]);
        }
    }
}
