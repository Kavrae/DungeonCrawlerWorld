using Engine.ECS.Components;
using Engine.Math;
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

    /// <inheritdoc cref="TargetResolution.EffectiveSpec"/>
    public TargetingSpec EffectiveSpec(int casterEntityId, IActionActivator activator) => targetResolution.EffectiveSpec(casterEntityId, activator);

    /// <inheritdoc cref="TargetResolution.Select"/>
    public TargetSelection Select(int casterEntityId, IActionActivator activator, TargetingMode mode, Vector3Int aimedTile, long now) =>
        targetResolution.Select(casterEntityId, activator, mode, aimedTile, now);

    /// <inheritdoc cref="TargetResolution.SelectEntity"/>
    public TargetSelection SelectEntity(int casterEntityId, IActionActivator activator, int targetEntityId) =>
        targetResolution.SelectEntity(casterEntityId, activator, targetEntityId);

    /// <summary>Fills tiles with where selection would land for casterEntityId activating activator, now.</summary>
    public ResolvedTargets Resolve(int casterEntityId, IActionActivator activator, TargetSelection selection, List<Vector3Int> tiles) =>
        targetResolution.Resolve(casterEntityId, activator.Targeting, selection, tiles);

    /// <summary>Fills tiles with where entityId's windup would land if it ended now -- following its target in Target mode. False, with tiles empty, when what it winds up is gone.</summary>
    public bool TryResolveWindup(int entityId, in PendingWindupComponent pending, List<Vector3Int> tiles)
    {
        if (!_activatables.TryGetActivator(entityId, pending.Activatable, out var activator))
        {
            tiles.Clear();
            return false;
        }

        targetResolution.Resolve(entityId, activator.Targeting, pending.Selection, tiles);
        return true;
    }

    /// <summary>Whether what entityId is winding up can be dodged (GameTags.TraitDodgeable on the action or item it resolves into); false when it is gone.</summary>
    public bool IsWindupDodgeable(int entityId, in PendingWindupComponent pending) =>
        _activatables.TryGetDefinition(entityId, pending.Activatable, out var definition) && definition.Tags.Has(GameTags.TraitDodgeable);
}
