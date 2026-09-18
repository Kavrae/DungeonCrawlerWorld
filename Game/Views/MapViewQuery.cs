using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Math;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.Containers.Components;
using Game.Modules.Core;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Inventory.Components;
using Game.Modules.Shops.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.Terrain;
using Microsoft.Xna.Framework;

namespace Game.Views;

/// <summary>The live <see cref="IMapViewQuery"/> over World and the component pools.</summary>
/// <remarks>
/// Optional pools (a module that isn't registered) read as "absent" rather than throwing, matching
/// how MapWindow treated them before this existed. Pools are resolved once at construction; every
/// answer is a direct read of them per call.
/// </remarks>
public sealed class MapViewQuery : IMapViewQuery
{
    private readonly World.World _world;
    private readonly TerrainRegistry _terrain;
    private readonly ActionCatalog _actionCatalog;
    private readonly DirectComponentPool<TransformComponent> _transforms;
    private readonly DirectComponentPool<GlyphComponent> _glyphs;
    private readonly DirectComponentPool<SpriteComponent> _sprites;
    private readonly DirectComponentPool<BackgroundComponent> _backgrounds;
    private readonly DirectComponentPool<DisplayTextComponent> _displayTexts;
    private readonly MultiComponentPool<NonBlockingComponent> _nonBlocking;
    private readonly PackedComponentPool<SimpleHealthComponent> _health;
    private readonly MultiComponentPool<BodyPartComponent>? _bodyParts;
    private readonly MultiComponentPool<StatModifierComponent>? _statModifiers;
    private readonly PackedComponentPool<DeadComponent>? _dead;
    private readonly MultiComponentPool<InventoryItemStackComponent>? _inventoryStacks;
    private readonly PackedComponentPool<LootedComponent>? _looted;
    private readonly PackedComponentPool<ContainerComponent>? _containers;
    private readonly PackedComponentPool<ShopComponent>? _shops;
    private readonly PackedComponentPool<ActionLockComponent>? _actionLocks;
    private readonly PackedComponentPool<PendingDelayedActionComponent>? _pendingDelayedActions;
    private readonly PackedComponentPool<DodgingComponent>? _dodging;

    public MapViewQuery(World.World world, ComponentManager componentManager, ActionCatalog actionCatalog, TerrainRegistry terrain)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(componentManager);
        ArgumentNullException.ThrowIfNull(actionCatalog);
        ArgumentNullException.ThrowIfNull(terrain);

        _world = world;
        _terrain = terrain;
        _actionCatalog = actionCatalog;
        _transforms = componentManager.GetDirectPool<TransformComponent>();
        _glyphs = componentManager.GetDirectPool<GlyphComponent>();
        _sprites = componentManager.GetDirectPool<SpriteComponent>();
        _backgrounds = componentManager.GetDirectPool<BackgroundComponent>();
        _displayTexts = componentManager.GetDirectPool<DisplayTextComponent>();
        _nonBlocking = componentManager.GetMultiPool<NonBlockingComponent>();
        _health = componentManager.GetPackedPool<SimpleHealthComponent>();
        _bodyParts = componentManager.GetOptionalMultiPool<BodyPartComponent>();
        _statModifiers = componentManager.GetOptionalMultiPool<StatModifierComponent>();
        _dead = componentManager.GetOptionalPackedPool<DeadComponent>();
        _inventoryStacks = componentManager.GetOptionalMultiPool<InventoryItemStackComponent>();
        _looted = componentManager.GetOptionalPackedPool<LootedComponent>();
        _containers = componentManager.GetOptionalPackedPool<ContainerComponent>();
        _shops = componentManager.GetOptionalPackedPool<ShopComponent>();
        _actionLocks = componentManager.GetOptionalPackedPool<ActionLockComponent>();
        _pendingDelayedActions = componentManager.GetOptionalPackedPool<PendingDelayedActionComponent>();
        _dodging = componentManager.GetOptionalPackedPool<DodgingComponent>();
    }

    public MapBounds Bounds => _world.Map.Bounds;

    public int PlayerEntityId => _world.PlayerEntityId;

    public bool IsOnMap(Vector3Int position) => _world.IsOnMap(position);

    public int GetBlockingEntityId(Vector3Int position) => _world.Map.GetBlockingEntityId(position);

    public ReadOnlySpan<int> GetOccupants(Vector3Int position) => _world.Map.GetOccupantEntityIdSpanAt(position);

    public byte GetOccupiedLayerMask(int x, int y) => _world.Map.GetOccupiedLayerMask(x, y);

    public bool TryGetTerrainVisual(int x, int y, int layer, out EntityVisualView visual) =>
        TryGetVisual(_world.GetTerrainAt(new Vector3Int(x, y, layer)), out visual);

    public bool TryGetTerrain(int x, int y, int layer, out TerrainView terrain) =>
        TryGetView(_world.GetTerrainAt(new Vector3Int(x, y, layer)), out terrain);

    public bool TryGetStructureVisual(int x, int y, int layer, out EntityVisualView visual) =>
        TryGetVisual(_world.GetStructureAt(new Vector3Int(x, y, layer)), out visual);

    public bool TryGetStructure(int x, int y, int layer, out TerrainView structure) =>
        TryGetView(_world.GetStructureAt(new Vector3Int(x, y, layer)), out structure);

    private bool TryGetVisual(TerrainCell cell, out EntityVisualView visual)
    {
        if (!TryGetDefinition(cell, out var definition))
        {
            visual = default;
            return false;
        }

        visual = TerrainVisual(cell, definition);
        return true;
    }

    private bool TryGetView(TerrainCell cell, out TerrainView view)
    {
        if (!TryGetDefinition(cell, out var definition))
        {
            view = default;
            return false;
        }

        view = new TerrainView(definition.Name, definition.Description, TerrainVisual(cell, definition));
        return true;
    }

    private bool TryGetDefinition(TerrainCell cell, out TerrainDefinition definition)
    {
        if (cell.IsEmpty || !_terrain.TryGet(cell.TypeId, out definition))
        {
            definition = null!;
            return false;
        }

        return true;
    }

    /// <remarks>Sprite wins over glyph, the same rule entity visuals follow.</remarks>
    private EntityVisualView TerrainVisual(TerrainCell cell, TerrainDefinition definition) =>
        _terrain.TryGetSprite(cell, out var sprite)
            ? new EntityVisualView(new SpriteView(sprite.SheetPath, sprite.SourceRectangle), string.Empty, default, IsDead: false)
            : new EntityVisualView(null, definition.Glyph, definition.GlyphColor, IsDead: false);

    public Color GetBackgroundColor(int x, int y, int layer)
    {
        if (!_world.IsOnMap(new Vector3Int(x, y, 0)))
        {
            return Color.Black;
        }

        var occupantEntityId = _world.Map.GetBlockingEntityId(new Vector3Int(x, y, layer));
        if (occupantEntityId != -1 && _backgrounds.TryGetReadonly(occupantEntityId, out var occupantBackground))
        {
            return occupantBackground.BackgroundColor;
        }

        var position = new Vector3Int(x, y, layer);
        if (TryGetDefinition(_world.GetStructureAt(position), out var structure))
        {
            return structure.BackgroundColor;
        }

        return TryGetDefinition(_world.GetTerrainAt(position), out var terrain) ? terrain.BackgroundColor : Color.White;
    }

    /// <remarks>The glyph pool is only read when there is no sprite: most drawn entities have one, and every skipped read is a scattered access into an entity-indexed array on the per-tile draw path.</remarks>
    public bool TryGetVisual(int entityId, out EntityVisualView visual)
    {
        var isDead = _dead?.Has(entityId) == true;

        if (_sprites.TryGetReadonly(entityId, out var sprite))
        {
            visual = new EntityVisualView(new SpriteView(sprite.SheetPath, sprite.SourceRectangle), string.Empty, default, isDead);
            return true;
        }

        if (_glyphs.TryGetReadonly(entityId, out var glyph))
        {
            visual = new EntityVisualView(null, glyph.Glyph, glyph.GlyphColor, isDead);
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

        occupant = new OccupantView(transform.Position, transform.Size, NonBlockingQueries.CombinedKind(_nonBlocking, entityId), _dead?.Has(entityId) == true);
        return true;
    }

    /// <remarks>The loot bag is only resolved for a container or a corpse, the two things that ever show one -- every live creature carries inventory stacks, and counting them for each visible occupant every frame would be wasted work.</remarks>
    public EntityStatusView GetStatus(int entityId)
    {
        var isContainer = _containers?.Has(entityId) == true;
        var lootBag = (isContainer || _dead?.Has(entityId) == true) && _inventoryStacks?.CountForEntity(entityId) > 0
            ? _looted?.Has(entityId) == true ? LootBagState.Looted : LootBagState.Unlooted
            : LootBagState.None;

        return new EntityStatusView(GetHealthBarFraction(entityId), isContainer, lootBag, _dodging?.Has(entityId) == true);
    }

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
        if (_pendingDelayedActions is null ||
            !_pendingDelayedActions.TryGetReadonly(entityId, out var pending) ||
            !_actionCatalog.TryGet(pending.ActionId, out var definition))
        {
            action = default;
            return false;
        }

        SpriteView? sprite = definition.SpriteName is { } spriteName && SpriteViews.TryGetFirst(spriteName, out var resolved)
            ? resolved
            : null;

        action = new ChargingActionView(sprite, definition.Glyph, definition.GlyphColor, GetActionLockTotalFrames(entityId));
        return true;
    }

    public int GetActionLockTotalFrames(int entityId) =>
        _actionLocks is not null && _actionLocks.TryGetReadonly(entityId, out var actionLock) ? actionLock.CurrentLockTotalFrames : 0;

    public EntityInteractionView GetInteraction(int entityId) => new(
        _displayTexts.TryGetReadonly(entityId, out var displayText) ? displayText.Name : "Unknown",
        _shops?.Has(entityId) == true,
        _containers?.Has(entityId) == true,
        _dead?.Has(entityId) == true);
}
