using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Spawning;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.Containers.Components;
using Game.Modules.Core;
using Game.Modules.Core.Components;
using Game.Modules.Currency.Components;
using Game.Modules.Death;
using Game.Modules.Death.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Shops.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.Terrain;
using Microsoft.Xna.Framework;
using Game.Blueprints;

namespace Game.Views;

/// <summary>The live <see cref="IMapViewQuery"/> over World and the component pools.</summary>
/// <remarks>
// Pools are resolved once at construction; every answer is a direct read of them per call.
/// </remarks>
public sealed class MapViewQuery : IMapViewQuery
{
    private readonly World.World _world;
    private readonly TerrainRegistry _terrain;
    private readonly ActivatableLookup _activatables;
    private readonly DirectComponentPool<TransformComponent> _transforms;
    private readonly PackedComponentPool<GlyphComponent> _glyphs;
    private readonly PackedComponentPool<SpriteComponent> _sprites;
    private readonly EntityNaming _naming;
    private readonly MultiComponentPool<NonBlockingComponent> _nonBlocking;
    private readonly PackedComponentPool<SimpleHealthComponent> _health;
    private readonly EntityBodyParts _bodyParts;
    private readonly MultiComponentPool<StatModifierComponent> _statModifiers;
    private readonly PackedComponentPool<DeadComponent> _dead;
    private readonly MultiComponentPool<InventoryItemStackComponent> _inventoryStacks;
    private readonly PackedComponentPool<LootedComponent> _looted;
    private readonly PackedComponentPool<CurrencyComponent> _currencies;
    private readonly PackedComponentPool<ContainerComponent> _containers;
    private readonly PackedComponentPool<ShopComponent> _shops;
    private readonly PackedComponentPool<ActionLockComponent> _actionLocks;
    private readonly PackedComponentPool<PendingWindupComponent> _pendingWindups;
    private readonly PackedComponentPool<DodgingComponent> _dodging;
    private readonly BlueprintRegistry _creatures;
    private readonly DirectComponentPool<SpawnRecordComponent> _spawnRecords;
    private readonly SimulationClock _simulationClock;
    private readonly LootRights _lootRights;

    /// <param name="creatures">The blueprint definitions, for drawing and naming every entity that holds no visual or name of its own -- which is nearly all of them, built or skeleton.</param>
    /// <param name="simulationClock">The current simulation frame, which a windup's progress is measured against.</param>
    public MapViewQuery(World.World world, ComponentManager componentManager, ActionCatalog actionCatalog, TerrainRegistry terrain, BlueprintRegistry creatures, SimulationClock simulationClock    /// <param name="itemCatalog">Names the item an item windup is for, so it is drawn like an action's; null (a test without items) draws none.</param>
, ItemCatalog? itemCatalog = null)
    {
        _simulationClock = simulationClock;
        _world = world;
        _terrain = terrain;
        _activatables = new ActivatableLookup(EntityActions.For(componentManager, actionCatalog, creatures), itemCatalog, componentManager);
        _transforms = componentManager.GetDirectPool<TransformComponent>();
        _glyphs = componentManager.GetPackedPool<GlyphComponent>();
        _sprites = componentManager.GetPackedPool<SpriteComponent>();
        _naming = EntityNaming.For(componentManager, creatures);
        _nonBlocking = componentManager.GetMultiPool<NonBlockingComponent>();
        _health = componentManager.GetPackedPool<SimpleHealthComponent>();
        _bodyParts = EntityBodyParts.For(componentManager, creatures);
        _statModifiers = componentManager.GetMultiPool<StatModifierComponent>();
        _dead = componentManager.GetPackedPool<DeadComponent>();
        _inventoryStacks = componentManager.GetMultiPool<InventoryItemStackComponent>();
        _looted = componentManager.GetPackedPool<LootedComponent>();
        _currencies = componentManager.GetPackedPool<CurrencyComponent>();
        _containers = componentManager.GetPackedPool<ContainerComponent>();
        _shops = componentManager.GetPackedPool<ShopComponent>();
        _actionLocks = componentManager.GetPackedPool<ActionLockComponent>();
        _pendingWindups = componentManager.GetPackedPool<PendingWindupComponent>();
        _dodging = componentManager.GetPackedPool<DodgingComponent>();
        _creatures = creatures;
        _spawnRecords = componentManager.GetDirectPool<SpawnRecordComponent>();
        _lootRights = LootRights.For(componentManager, world.EntityKeys);
    }

    public MapBounds Bounds => _world.Map.Bounds;

    public int PlayerEntityId => _world.PlayerEntityId;

    public bool IsOnMap(Vector3Int position) => _world.IsOnMap(position);

    public int GetBlockingEntityId(Vector3Int position) => _world.Map.GetBlockingEntityId(position);

    public ReadOnlySpan<int> GetOccupants(Vector3Int position) => _world.Map.GetOccupantEntityIdSpanAt(position);

    public byte GetOccupiedLayerMask(int x, int y) => _world.Map.GetOccupiedLayerMask(x, y);

    public TerrainRegistry Terrain => _terrain;

    public void CopyCells(int minX, int minY, int width, int height, int mapLayer, World.MapCellBuffer cells) =>
        _world.Map.CopyCells(minX, minY, width, height, mapLayer, cells);

    public bool TryGetTerrain(int x, int y, int layer, out TerrainView terrain) =>
        TryGetView(_world.GetTerrainAt(new Vector3Int(x, y, layer)), _world.GetTerrainSeedAt(new Vector3Int(x, y, layer)), out terrain);

    public bool TryGetStructure(int x, int y, int layer, out TerrainView structure) =>
        TryGetView(_world.GetStructureAt(new Vector3Int(x, y, layer)), _world.GetStructureSeedAt(new Vector3Int(x, y, layer)), out structure);

    private bool TryGetView(TerrainCell cell, uint seed, out TerrainView view)
    {
        if (cell.IsEmpty || !_terrain.TryGet(cell.TypeId, out var definition))
        {
            view = default;
            return false;
        }

        view = new TerrainView(definition.Name, definition.Description, new TerrainVisualView(definition.SpriteName, seed, definition.Glyph, definition.GlyphColor));
        return true;
    }

    /// <remarks>An entity's own SpriteComponent or GlyphComponent -- a per-instance override -- wins over its blueprint's appearance. The glyph pool is only read when there is no sprite: every skipped read is a scattered access into an entity-indexed array on the per-tile draw path. The blueprint path carries the glyph beside the sprite name (a reader draws the glyph when the manifest has no such sprite), so a reader that only shows glyphs still gets one.</remarks>
    public bool TryGetVisual(int entityId, out EntityVisualView visual)
    {
        var isDead = _dead.Has(entityId);

        if (_sprites.TryGetReadonly(entityId, out var sprite))
        {
            visual = new EntityVisualView(sprite.SpriteName, sprite.Seed, string.Empty, default, isDead);
            return true;
        }

        if (_glyphs.TryGetReadonly(entityId, out var glyph))
        {
            visual = new EntityVisualView(null, 0, glyph.Glyph, glyph.GlyphColor, isDead);
            return true;
        }

        if (TryGetBlueprintAppearance(entityId, out var record, out var appearance) && (appearance.SpriteName is not null || appearance.Glyph.Length > 0))
        {
            visual = new EntityVisualView(appearance.SpriteName, record.Seed, appearance.Glyph, appearance.GlyphColor, isDead);
            return true;
        }

        visual = default;
        return false;
    }

    public bool TryGetOccupant(int entityId, out OccupantView occupant)
    {
        if (!_transforms.TryGetReadonly(entityId, out var transform))
        {
            occupant = default;
            return false;
        }

        occupant = new OccupantView(transform.Position, transform.Size, NonBlockingQueries.CombinedKind(_nonBlocking, entityId), _dead.Has(entityId));
        return true;
    }

    /// <remarks>The loot bag is only resolved for a container or a corpse, the two things that ever show one -- every live creature carries inventory stacks, and counting them for each visible occupant every frame would be wasted work. Either one shows it while it holds anything to loot: a stack, or any Gold or Credits. A shop is a container only so its destruction wipes its stock; standing, it trades rather than being looted, so it shows a bag only once destroyed, as a corpse does.</remarks>
    public EntityStatusView GetStatus(int entityId)
    {
        var isContainer = _containers.Has(entityId) && !_shops.Has(entityId);
        var lootBag = (isContainer || _dead.Has(entityId)) && HoldsLoot(entityId)
            ? _looted.Has(entityId) ? LootBagState.Looted : LootBagState.Unlooted
            : LootBagState.None;

        return new EntityStatusView(GetHealthBarFraction(entityId), isContainer, lootBag, _dodging.Has(entityId));
    }

    private bool HoldsLoot(int entityId) =>
        _inventoryStacks.Has(entityId) ||
        (_currencies.TryGetReadonly(entityId, out var currency) && (currency.Gold > 0 || currency.Credits > 0));

    /// <summary>Current over modifier-effective maximum, or null when the bar is hidden: no health at all, a non-positive maximum, or already full.</summary>
    private float? GetHealthBarFraction(int entityId)
    {
        if (!HealthQueries.TryGetTotals(_health, _bodyParts, entityId, out var current, out var maximum) || maximum <= 0)
        {
            return null;
        }

        var effectiveMaximum = StatModifierMath.GetEffectiveValue(_statModifiers, entityId, StatModifierTarget.MaximumHealth, maximum);
        return effectiveMaximum <= 0 || current >= effectiveMaximum ? null : current / effectiveMaximum;
    }

    public bool TryGetChargingAction(int entityId, out ChargingActionView action)
    {
        if (!_pendingWindups.TryGetReadonly(entityId, out var pending) ||
            !_activatables.TryGetDefinition(entityId, pending.Activatable, out var definition))
        {
            action = default;
            return false;
        }

        action = new ChargingActionView(definition.SpriteName, definition.Glyph, definition.GlyphColor);
        return true;
    }

    /// <remarks>The windup's length is the lock's CurrentLockTotalFrames, set on the same frame ReadyAtFrame was copied from the lock's deadline.</remarks>
    public float GetChargeFraction(int entityId)
    {
        if (!_pendingWindups.TryGetReadonly(entityId, out var pending) ||
            !_actionLocks.TryGetReadonly(entityId, out var actionLock) ||
            actionLock.CurrentLockTotalFrames == 0)
        {
            return 0f;
        }

        var remainingFrames = FrameDeadline.Remaining(pending.ReadyAtFrame, _simulationClock.CurrentFrame);
        return Math.Clamp(1f - (float)remainingFrames / actionLock.CurrentLockTotalFrames, 0f, 1f);
    }

    public EntityInteractionView GetInteraction(int entityId) => new(
        ResolveName(entityId),
        _shops.Has(entityId),
        _containers.Has(entityId),
        _dead.Has(entityId),
        !_lootRights.CanLoot(entityId, _world.PlayerEntityKey, _simulationClock.CurrentFrame));

    private string ResolveName(int entityId) => _naming.NameOf(entityId);

    /// <summary>An entity with a spawn record and no visual of its own -- which is every entity not given one -- draws as its blueprint's appearance for its seed, built or not.</summary>
    private bool TryGetBlueprintAppearance(int entityId, out SpawnRecordComponent record, out EntityAppearance appearance)
    {
        if (_spawnRecords.TryGetReadonly(entityId, out record) && _creatures.TryGetAppearance(record.BlueprintId, out appearance))
        {
            return true;
        }

        record = default;
        appearance = null!;
        return false;
    }
}
