using Game.Effects;
using Game.Modules;

namespace Game.Tags;

/// <summary>Checks that every gameplay tag the build's registered content uses is declared by one of its modules.</summary>
/// <remarks>
/// Run once every module is configured, so a mod whose content names an undeclared tag (a typo, or one it forgot to
/// declare) fails its dry run instead of silently never matching. Covers the tags of every action, item, terrain
/// contact and aura registered by then, and every tag an effect entry any of them holds names (EffectContent).
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
            gameplayTags.EnsureDeclared(action.Tags, $"Action '{action.Name}'");
        }

        foreach (var item in context.Items.Definitions)
        {
            gameplayTags.EnsureDeclared(item.Tags, $"Item '{item.Name}'");
        }

        var terrain = context.Terrain;
        for (var terrainTypeId = 1; terrainTypeId <= terrain.Count; terrainTypeId++)
        {
            if (terrain.TryGet((ushort)terrainTypeId, out var terrainDefinition) && terrainDefinition.Contact is { } contact)
            {
                gameplayTags.EnsureDeclared(contact.Tags, $"Terrain '{terrainDefinition.Name}'");
            }
        }

        var auras = context.Auras;
        for (var auraId = 0; auraId < auras.Count; auraId++)
        {
            var aura = auras.Get((byte)auraId);
            gameplayTags.EnsureDeclared(aura.Tags, $"Aura '{aura.Name}'");
        }

        EffectContent.ForEachEntry(context, (entry, heldBy) =>
        {
            foreach (var tag in entry.ReferencedTags)
            {
                if (!gameplayTags.IsDeclared(tag))
                {
                    throw new InvalidOperationException($"{heldBy} has an effect that names gameplay tag '{tag}', which no module in this build declares.");
                }
            }
        });
    }
}
