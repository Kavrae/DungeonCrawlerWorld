using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Entities;
using Engine.Math;
using Engine.Utilities;
using Game.Blueprints;
using Game.Blueprints.Classes;
using Game.Blueprints.NPCs.Generic;
using Game.Blueprints.Objects;
using Game.Blueprints.Races;
using Game.Blueprints.Terrain;
using Game.Modules.Core.Components;
using Game.Modules.Crawler.Components;
using Game.Modules.Movement.Components;

namespace Game;

/// <summary>
/// Builds a test map across all three MapLayers, each with its own independent, percentage-
/// rolled population: Ground (border walls, a cross hallway, randomized lava/dirt/grass
/// terrain, 10% of interior tiles get a Goblin/Fairy/Ghost per PopulateGroundEntity's
/// breakdown), UnderGround (border walls, a randomized dirt/lava mixture, 5% of interior
/// tiles get a Ghost per PopulateUnderGroundGhost), and Flying (5% of all tiles get a Fairy
/// per PopulateFlyingFairy) -- plus a handful of standalone multi-trait fixtures, via the
/// Blueprint composition system.
/// </summary>
public sealed class TestMapBuilder(EntityManager entityManager, ComponentManager componentManager, MathUtility mathUtility, UniqueNumberAllocator crawlerNumberAllocator)
{
    // TEMPORARY: halved from the original values below (10/5/5) to reduce the creature
    // population -- Movement/HealthRegen/ContactDamage/StatusEffectAura all iterate this
    // population every frame, and at the original density the game was effectively
    // unplayable (5-10fps) for manual testing. Revert once the performance investigation
    // these values are standing in for (see TODO.md) lands a real fix.
    private const int GroundPopulationPercent = 5;
    private const int UnderGroundGhostPercent = 3;
    private const int FlyingFairyPercent = 3;

    /// <summary>Chance any given rolled NPC (see BuildRaceEntity) is also a Crawler -- deliberately small; most NPCs are not.</summary>
    private const int CrawlerPercent = 2;

    private const string LongWordWrapDescription =
        "ThisIsAReallyLongDescriptionToTestTheWordWrapCapabilitiesAroundHyphenatingLongWordsMultipleTimes";

    private readonly StoneFloor _stoneFloor = new();
    private readonly Wall _wall = new();
    private readonly Dirt _dirt = new();
    private readonly Lava _lava = new();
    private readonly Grass _grass = new();
    private readonly Goblin _goblin = new(mathUtility);
    private readonly Fairy _fairy = new(mathUtility);
    private readonly Ghost _ghost = new(mathUtility);
    private readonly Engineer _engineer = new();
    private readonly Tank _tank = new(mathUtility);
    private readonly GoblinEngineerBlueprint _goblinEngineer = new(new Goblin(mathUtility), new Engineer());
    private readonly PackedComponentPool<MovementComponent> _movementComponents = componentManager.GetPackedPool<MovementComponent>();
    private readonly PackedComponentPool<ActionLockComponent> _actionLocks = componentManager.GetPackedPool<ActionLockComponent>();

    /// <summary>
    /// Populates an already-constructed World's map with terrain and entities. World is
    /// built by the caller (not here) because it must exist before MovementModule --
    /// itself a constructor dependency of Bootstrapper.Build, which is what produces the
    /// EntityManager/ComponentManager this builder needs -- so World can't wait until after
    /// that call to be created.
    /// </summary>
    public void Populate(World.World world)
    {
        var mapColumns = world.Map.Size.X;
        var mapRows = world.Map.Size.Y;

        for (var column = 0; column < mapColumns; column++)
        {
            for (var row = 0; row < mapRows; row++)
            {
                var isBorder = column == 0 || column == mapColumns - 1 || row == 0 || row == mapRows - 1;
                var isWallOrHallway =
                    isBorder ||
                    (column is 10 or 16 && (row < 10 || row > 16)) ||
                    (row is 10 or 16 && (column < 10 || column > 16));

                if (isWallOrHallway)
                {
                    BuildTerrainFromBlueprint(world, _stoneFloor, column, row, TerrainLayer.Ground);
                    BuildFromBlueprint(world, _wall, column, row);
                }
                else
                {
                    // Randomized ground terrain: lava 10%, dirt 45%, grass 45%.
                    BuildTerrainFromBlueprint(
                        world,
                        PickGroundTerrain(),
                        column,
                        row,
                        TerrainLayer.Ground);

                    if (mathUtility.Next(0, 100) < GroundPopulationPercent)
                    {
                        PopulateGroundEntity(world, column, row);
                    }
                }

                // UnderGround layer: border walls (its own MapLayer, so distinct from the
                // Ground-layer walls above -- entities on different layers never collide).
                // Mirrors Ground's own split: StoneFloor under the border (so the wall's
                // background reads as a consistent wall floor, not the interior's speckled
                // terrain -- MapWindow's background resolution only ever shows terrain color,
                // never the creature standing on it, so the border cells need dedicated
                // terrain of their own the same way Ground's isWallOrHallway branch already
                // does) and a genuinely randomized dirt/lava mixture everywhere else -- its own
                // independent roll from Ground's lava/dirt/grass mix one layer up, so the two
                // layers don't mirror each other.
                if (isBorder)
                {
                    BuildTerrainFromBlueprint(world, _stoneFloor, column, row, TerrainLayer.UnderGround);
                    BuildFromBlueprintAtLayer(world, _wall, column, row, MapLayer.UnderGround);
                }
                else
                {
                    BuildTerrainFromBlueprint(
                        world,
                        mathUtility.Next(0, 5) == 0
                            ? _lava
                            : _dirt,
                        column,
                        row,
                        TerrainLayer.UnderGround);

                    if (mathUtility.Next(0, 100) < UnderGroundGhostPercent)
                    {
                        PopulateUnderGroundGhost(world, column, row);
                    }
                }

                // Flying layer: no walls/border of its own (unlike Ground/UnderGround), so this
                // isn't scoped to isBorder/isWallOrHallway -- every cell is eligible.
                if (mathUtility.Next(0, 100) < FlyingFairyPercent)
                {
                    PopulateFlyingFairy(world, column, row);
                }
            }
        }

        BuildFixtureEntities(world);
    }

    /// <summary>
    /// Ground layer's entity roll (see GroundPopulationPercent for the gate already applied
    /// by the caller): 40% 1x1 Goblin, 8% 2x2 Goblin, 1% 3x3 Goblin, 40% 1x1 Fairy, 8% 2x2
    /// Fairy, 1% 3x3 Fairy, 2% 1x2 Ghost -- a 0-99 roll so each share lands exactly. All three
    /// races land on the Ground layer here, including Fairy/Ghost -- distinct from, and in
    /// addition to, the dedicated Ghost-on-UnderGround and Fairy-on-Flying populations below.
    /// </summary>
    private void PopulateGroundEntity(World.World world, int column, int row)
    {
        var roll = mathUtility.Next(0, 100);
        switch (roll)
        {
            case < 40:
                BuildRaceEntity(world, _goblin, column, row, new Vector2Byte(1, 1), MapLayer.Ground);
                break;
            case < 48:
                BuildRaceEntity(world, _goblin, column, row, new Vector2Byte(2, 2), MapLayer.Ground);
                break;
            case < 49:
                BuildRaceEntity(world, _goblin, column, row, new Vector2Byte(3, 3), MapLayer.Ground);
                break;
            case < 89:
                BuildRaceEntity(world, _fairy, column, row, new Vector2Byte(1, 1), MapLayer.Ground);
                break;
            case < 97:
                BuildRaceEntity(world, _fairy, column, row, new Vector2Byte(2, 2), MapLayer.Ground);
                break;
            case < 98:
                BuildRaceEntity(world, _fairy, column, row, new Vector2Byte(3, 3), MapLayer.Ground);
                break;
            default:
                BuildRaceEntity(world, _ghost, column, row, new Vector2Byte(1, 2), MapLayer.Ground);
                break;
        }
    }

    /// <summary>UnderGround layer's dedicated Ghost population (see UnderGroundGhostPercent for the gate): 90% 1x1, 9% 2x2, 1% 3x3.</summary>
    private void PopulateUnderGroundGhost(World.World world, int column, int row)
    {
        var size = mathUtility.Next(0, 100) switch
        {
            < 90 => new Vector2Byte(1, 1),
            < 99 => new Vector2Byte(2, 2),
            _ => new Vector2Byte(3, 3),
        };

        BuildRaceEntity(world, _ghost, column, row, size, MapLayer.UnderGround);
    }

    /// <summary>Flying layer's dedicated Fairy population (see FlyingFairyPercent for the gate): 90% 1x1, 9% 2x2, 1% 3x3.</summary>
    private void PopulateFlyingFairy(World.World world, int column, int row)
    {
        var size = mathUtility.Next(0, 100) switch
        {
            < 90 => new Vector2Byte(1, 1),
            < 99 => new Vector2Byte(2, 2),
            _ => new Vector2Byte(3, 3),
        };

        BuildRaceEntity(world, _fairy, column, row, size, MapLayer.Flying);
    }

    /// <summary>Builds a race blueprint entity at the given size/layer with a staggered action lock -- the shared path for every PopulateEntity roll outcome. A small percentage also become Crawlers (see CrawlerPercent).</summary>
    private void BuildRaceEntity(World.World world, IBlueprint blueprint, int column, int row, Vector2Byte size, MapLayer mapLayer)
    {
        var entityId = entityManager.CreateEntity();
        blueprint.Build(componentManager, entityId);

        ref var transform = ref componentManager.GetDirectPool<TransformComponent>().Get(entityId);
        transform.Size = size;

        if (mathUtility.Next(0, 100) < CrawlerPercent)
        {
            componentManager.Merge(entityId, new CrawlerComponent(crawlerNumberAllocator.Allocate()));
        }

        StaggerActionLock(entityId);
        world.PlaceEntityOnMap(entityId, new Vector3Int(column, row, (int)mapLayer), ref transform);
    }

    private int BuildFromBlueprint(World.World world, IBlueprint blueprint, int column, int row)
    {
        var entityId = entityManager.CreateEntity();
        blueprint.Build(componentManager, entityId);

        PlaceAt(world, entityId, column, row);

        return entityId;
    }

    /// <summary>Same as BuildFromBlueprint, but places at mapLayer instead of preserving whatever Z the blueprint itself set.</summary>
    private int BuildFromBlueprintAtLayer(World.World world, IBlueprint blueprint, int column, int row, MapLayer mapLayer)
    {
        var entityId = entityManager.CreateEntity();
        blueprint.Build(componentManager, entityId);

        ref var transform = ref componentManager.GetDirectPool<TransformComponent>().Get(entityId);
        world.PlaceEntityOnMap(entityId, new Vector3Int(column, row, (int)mapLayer), ref transform);

        return entityId;
    }

    /// <summary>
    /// Terrain (the floor an entity stands on) is never a Blocking creature-occupancy entity
    /// -- it goes through Map's separate terrain store instead of PlaceEntityOnMap, so it
    /// can't clobber (or be clobbered by) whatever creature/wall is standing on it.
    /// </summary>
    private void BuildTerrainFromBlueprint(World.World world, IBlueprint blueprint, int column, int row, TerrainLayer terrainLayer)
    {
        var entityId = entityManager.CreateEntity();
        blueprint.Build(componentManager, entityId);

        ref var transform = ref componentManager.GetDirectPool<TransformComponent>().Get(entityId);
        world.PlaceTerrainOnMap(entityId, column, row, terrainLayer, ref transform);
    }

    /// <summary>Lava 10%, dirt 45%, grass 45% -- via a 0-19 roll (2/9/9 slices) so the 10% share lands exactly.</summary>
    private IBlueprint PickGroundTerrain()
    {
        var roll = mathUtility.Next(0, 100);
        return roll switch
        {
            < 1 => _lava,
            < 40 => _dirt,
            _ => _grass,
        };
    }

    /// <summary>
    /// Standalone demonstration entities, placed individually rather than through the main
    /// population loop above (PopulateEntity). These specifically exercise capabilities
    /// nothing in the main loop touches: multiple components of the same type on one entity
    /// (MultiComponentPool's whole reason for existing), removing a component after blueprint
    /// construction, and text long enough to actually word-wrap/hyphenate when selected.
    /// </summary>
    private void BuildFixtureEntities(World.World world)
    {
        // Long description: visually exercises SelectionWindowContent's word-wrap/
        // hyphenation when selected -- the algorithm itself is unit tested, but nothing
        // else on the map has a description long enough to actually wrap or hyphenate.
        var longDescriptionId = entityManager.CreateEntity();
        _goblin.Build(componentManager, longDescriptionId);

        ref var longDescriptionText = ref componentManager.GetDirectPool<DisplayTextComponent>().Get(longDescriptionId);
        longDescriptionText.Description = LongWordWrapDescription;

        ref var longDescriptionTransform = ref componentManager.GetDirectPool<TransformComponent>().Get(longDescriptionId);
        longDescriptionTransform.Size = new Vector2Byte(2, 2);

        StaggerActionLock(longDescriptionId);
        PlaceAt(world, longDescriptionId, 2, 2);

        // Huge (3x3) goblin engineer, placed standalone rather than through BuildGoblin's rotation.
        var hugeId = entityManager.CreateEntity();
        _goblinEngineer.Build(componentManager, hugeId);

        ref var hugeTransform = ref componentManager.GetDirectPool<TransformComponent>().Get(hugeId);
        hugeTransform.Size = new Vector2Byte(3, 3);

        StaggerActionLock(hugeId);
        PlaceAt(world, hugeId, 5, 5);

        // Stationary Fairy engineer: race+class composed, then MovementComponent removed so
        // it doesn't wander despite Fairy's own baseline movement mode.
        var stationaryFairyId = entityManager.CreateEntity();
        _fairy.Build(componentManager, stationaryFairyId);
        _engineer.Build(componentManager, stationaryFairyId);
        _movementComponents.Remove(stationaryFairyId);

        PlaceAt(world, stationaryFairyId, 1, 1);

        // Ordinary moving Fairy, for contrast against the stationary one above.
        StaggerActionLock(BuildFromBlueprint(world, _fairy, 17, 16));

        // Two RaceComponents on one entity (Goblin base with Fairy layered on top). Movement
        // removed since a grounded-goblin/flying-fairy hybrid has no single coherent
        // movement mode.
        var multiRaceId = entityManager.CreateEntity();
        _goblin.Build(componentManager, multiRaceId);
        _fairy.Build(componentManager, multiRaceId);
        _movementComponents.Remove(multiRaceId);

        PlaceAt(world, multiRaceId, 17, 9);

        // Two ClassComponents on one entity (Engineer and Tank both applied to the same Goblin).
        var multiClassId = entityManager.CreateEntity();
        _goblin.Build(componentManager, multiClassId);
        _engineer.Build(componentManager, multiClassId);
        _tank.Build(componentManager, multiClassId);

        StaggerActionLock(multiClassId);
        PlaceAt(world, multiClassId, 11, 2);

        // Tiny-entity occupancy fixtures: 4 partially fill MapWindow's 3x3 tiny grid, 11
        // exercise its 9-entity cap (the extra 2 are built but never drawn).
        BuildTinyGoblins(world, count: 4, column: 3, row: 5);
        BuildTinyGoblins(world, count: 11, column: 7, row: 5);

        // Phasing fairy, deliberately co-located with the ordinary moving Fairy above (17,16)
        // -- both are Flying layer, so the Phasing entity overlaps a Blocking one at the same
        // layer, the scenario Occupancy exists to support, rather than relying on a
        // coincidental overlap elsewhere.
        var phasingFairyId = entityManager.CreateEntity();
        _fairy.Build(componentManager, phasingFairyId); // Fairy's own blueprint already includes MovementComponent.
        componentManager.GetMultiPool<NonBlockingComponent>().Add(phasingFairyId, new NonBlockingComponent(NonBlockingKind.Phasing));

        StaggerActionLock(phasingFairyId);
        PlaceAt(world, phasingFairyId, 17, 16);
    }

    /// <summary>Builds count plain-Goblin-glyph entities, all Tiny, all at the same cell -- for exercising MapWindow's tiny-entity grid/cap.</summary>
    private void BuildTinyGoblins(World.World world, int count, int column, int row)
    {
        for (var i = 0; i < count; i++)
        {
            var entityId = entityManager.CreateEntity();
            _goblin.Build(componentManager, entityId);
            componentManager.GetMultiPool<NonBlockingComponent>().Add(entityId, new NonBlockingComponent(NonBlockingKind.Tiny));

            StaggerActionLock(entityId);
            PlaceAt(world, entityId, column, row);
        }
    }

    private static readonly short MaximumStaggerFrames = (short)GameTiming.FramesForSeconds(1f);

    /// <summary>
    /// Randomizes a freshly-built goblin/fairy's starting action lock to a value between 0 and
    /// MaximumStaggerFrames, instead of the 0 every race blueprint merges by default -- without
    /// this, an entire periodic population spawns ready to act on the same handful of frames
    /// and visibly moves in lockstep bursts rather than spreading out over time. The exact
    /// upper bound doesn't need to track any entity's real ActionCooldownFrames -- this only
    /// matters for the initial stagger, and gets fully overwritten the first time the entity
    /// actually moves (ActionLockGate.Lock sets both fields together at that point).
    /// </summary>
    private void StaggerActionLock(int entityId)
    {
        var framesToWait = (short)mathUtility.Next(0, MaximumStaggerFrames + 1);

        ActionLockGate.Lock(_actionLocks, entityId, framesToWait);
    }

    /// <summary>
    /// Places an already-built entity at the given grid column/row, preserving the Z height
    /// (map layer) its blueprint already set -- a blueprint's own X/Y is just a placeholder.
    /// </summary>
    private void PlaceAt(World.World world, int entityId, int column, int row)
    {
        ref var transform = ref componentManager.GetDirectPool<TransformComponent>().Get(entityId);
        var position = new Vector3Int(column, row, transform.Position.Z);

        world.PlaceEntityOnMap(entityId, position, ref transform);
    }
}