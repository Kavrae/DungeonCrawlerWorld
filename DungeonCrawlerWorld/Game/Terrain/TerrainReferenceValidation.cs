using Game.Effects;
using Game.Modules;

namespace Game.Terrain;

/// <summary>Checks that every terrain key the build's registered content names is registered in its TerrainRegistry.</summary>
/// <remarks>
/// Run once every module is configured, so content naming a terrain that doesn't exist (a typo, or a mod's terrain whose
/// mod isn't loaded) fails the build instead of quietly changing nothing. Reaches every entry through EffectContent and
/// asks it what it names (IEffectEntry.ReferencedTerrainKeys).
/// </remarks>
internal static class TerrainReferenceValidation
{
    /// <exception cref="InvalidOperationException">A definition's effect names a terrain key nothing registered, naming both.</exception>
    public static void EnsureRegistered(GameModuleContext context)
    {
        var terrain = context.Terrain;
        EffectContent.ForEachEntry(context, (entry, heldBy) =>
        {
            foreach (var terrainKey in entry.ReferencedTerrainKeys)
            {
                if (!terrain.TryGetId(terrainKey, out _))
                {
                    throw new InvalidOperationException($"{heldBy} has an effect that names terrain '{terrainKey}', which no module in this build registers.");
                }
            }
        });
    }
}
