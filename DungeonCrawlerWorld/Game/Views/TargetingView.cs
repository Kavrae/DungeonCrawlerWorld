using System.Runtime.InteropServices;
using Engine.ECS.Components;
using Engine.Math;
using Engine.Tiles;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.Inventory;
using Game.Tags;

namespace Game.Views;

/// <summary>Targeting for anything outside Game: the selection a confirm queues, the tiles it would land on, and where a windup in progress lands now.</summary>
/// <remarks>Every answer comes from TargetResolution, the function activations and windups resolve through, so a preview or telegraph draws exactly what lands.</remarks>
public sealed class TargetingView(TargetResolution targetResolution, EntityActions entityActions, ItemCatalog itemCatalog, ComponentManager componentManager)
{
    private readonly ActivatableLookup _activatables = new(entityActions, itemCatalog, componentManager);

    /// <summary>A resolve's shape and path tiles, each once in order: AddPathTiles' scratch.</summary>
    private readonly TileSet _drawnTiles = new();

    /// <inheritdoc cref="TargetResolution.SightVersion"/>
    public int SightVersion => targetResolution.SightVersion;

    /// <inheritdoc cref="TargetResolution.EffectiveSpec"/>
    public TargetingSpec EffectiveSpec(int casterEntityId, IActionActivator activator) => targetResolution.EffectiveSpec(casterEntityId, activator);

    /// <inheritdoc cref="TargetResolution.Select"/>
    public TargetSelection Select(int casterEntityId, IActionActivator activator, TargetingMode mode, Vector3Int aimedTile, long now) =>
        targetResolution.Select(casterEntityId, activator, mode, aimedTile, now);

    /// <inheritdoc cref="TargetResolution.SelectEntity"/>
    public TargetSelection SelectEntity(int casterEntityId, IActionActivator activator, int targetEntityId) =>
        targetResolution.SelectEntity(casterEntityId, activator, targetEntityId);

    /// <inheritdoc cref="TargetResolution.IsTargetableTile"/>
    public bool IsTargetableTile(Vector3Int tile) => targetResolution.IsTargetableTile(tile);

    /// <inheritdoc cref="TargetResolution.ResolveReachable"/>
    public void ResolveReachable(TargetingSpec spec, Vector3Int origin, Vector2Byte originSize, List<Vector3Int> aimableTiles, List<Vector3Int> reachedTiles) =>
        targetResolution.ResolveReachable(spec, origin, originSize, aimableTiles, reachedTiles);

    /// <summary>Fills tiles with where selection would land for casterEntityId activating activator, now, and the tiles its path crosses on the way, for drawing.</summary>
    public ResolvedTargets Resolve(int casterEntityId, IActionActivator activator, TargetSelection selection, List<Vector3Int> tiles)
    {
        var resolved = targetResolution.Resolve(casterEntityId, activator.Targeting, selection, tiles);
        AddPathTiles(resolved, tiles);
        return resolved;
    }

    /// <summary>Fills tiles with where entityId's windup would land if it ended now -- following its target in Target mode, its path included. False, with tiles empty, when what it winds up is gone.</summary>
    public bool TryResolveWindup(int entityId, in PendingWindupComponent pending, List<Vector3Int> tiles)
    {
        if (!_activatables.TryGetActivator(entityId, pending.Activatable, out var activator))
        {
            tiles.Clear();
            return false;
        }

        AddPathTiles(targetResolution.Resolve(entityId, activator.Targeting, pending.Selection, tiles), tiles);
        return true;
    }

    /// <summary>Adds the tiles resolved's path crossed that tiles doesn't hold yet, so a path is drawn up to where it stopped.</summary>
    private void AddPathTiles(ResolvedTargets resolved, List<Vector3Int> tiles)
    {
        if (resolved.PathTiles.Count == 0)
        {
            return;
        }

        tiles.AddRange(resolved.PathTiles);
        _drawnTiles.SetTo(CollectionsMarshal.AsSpan(tiles));
        tiles.Clear();
        tiles.AddRange(_drawnTiles.AsSpan());
    }

    /// <summary>Whether what entityId is winding up can be dodged (GameTags.TraitDodgeable on the action or item it resolves into); false when it is gone.</summary>
    public bool IsWindupDodgeable(int entityId, in PendingWindupComponent pending) =>
        _activatables.TryGetDefinition(entityId, pending.Activatable, out var definition) && definition.Tags.Has(GameTags.TraitDodgeable);
}
