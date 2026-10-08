using Game.Modules.Auras;
using Game.Terrain;
using Game.World;

namespace Game.Views;

/// <summary>The name to show a player for what caused an effect.</summary>
/// <remarks>
/// A terrain's and an aura's name are read from their definition by id each time, so a definition
/// replaced during the session shows its new name. An entity is named as it was when the source was
/// created (ActionSource.Identity), which outlives the entity. ActionSource.ToString stays the
/// debug form.
/// </remarks>
public sealed class ActionSourceNaming(TerrainRegistry terrain, AuraCatalog auras)
{
    public string Describe(ActionSource source) => source.Kind switch
    {
        ActionSourceKind.Entity => source.Identity.DisplayName,
        ActionSourceKind.Terrain when terrain.TryGet(source.TerrainTypeId, out var terrainDefinition) => terrainDefinition.Name,
        ActionSourceKind.Aura when source.AuraId < auras.Count => auras.Get(source.AuraId).Name,
        ActionSourceKind.Terrain or ActionSourceKind.Aura => source.ToString(),
        _ => source.Kind.ToString(),
    };
}
