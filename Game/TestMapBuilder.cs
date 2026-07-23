using Engine.ECS.Components;
using Engine.ECS.Entities;
using Engine.Math;
using Game.Blueprints;
using Game.Blueprints.Classes;
using Game.Blueprints.NPCs.Generic;
using Game.Blueprints.Objects;
using Game.Blueprints.Races;
using Game.Blueprints.Terrain;
using Game.Modules.Core.Components;
using Game.Modules.Movement.Components;

namespace Game;

/// <summary>
/// Builds a test map across all three MapLayers: Ground (border walls, a cross hallway,
/// dirt/lava terrain, a large wandering goblin population at two densities), UnderGround
/// (border walls and a randomized dirt/lava mixture), and Flying (scattered wandering
/// Fairies) -- plus a handful of standalone multi-trait fixtures, via the Blueprint
/// composition system.
/// </summary>
public sealed class TestMapBuilder(EntityManager entityManager, ComponentManager componentManager, MathUtility mathUtility)
{
    // Matches MapWindow.DrawGlyphs's own medium/large/huge glyph-size selection
    // (TransformComponent.Size.X: 1 => medium, 2 => large, _ => huge) -- rotating through
    // them one-for-one keeps the three groups as even as possible regardless of how many
    // goblins the map ends up with.
    private static readonly Vector2Byte[] GoblinSizes = [new(1, 1), new(2, 2), new(3, 3)];

    private const string LongWordWrapDescription =
        "ThisIsAReallyLongDescriptionToTestTheWordWrapCapabilitiesAroundHyphenatingLongWordsMultipleTimes";

    private readonly StoneFloor _stoneFloor = new();
    private readonly Wall _wall = new();
    private readonly Dirt _dirt = new();
    private readonly Lava _lava = new();
    private readonly Goblin _goblin = new(mathUtility);
    private readonly Fairy _fairy = new(mathUtility);
    private readonly Engineer _engineer = new();
    private readonly Tank _tank = new();
    private readonly GoblinEngineerBlueprint _goblinEngineer = new(new Goblin(mathUtility), new Engineer());
    private int _goblinsBuilt;

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
                    // Occasional lava patches instead of dirt -- uncommon by design.
                    var isLava = row % 33 == 17 && column % 47 == 23;
                    BuildTerrainFromBlueprint(
                        world,
                        isLava
                            ? _lava
                            : _dirt,
                        column,
                        row,
                        TerrainLayer.Ground);

                    if (row % 5 == 0 && column % 4 == 0)
                    {
                        BuildGoblin(world, column, row);
                    }
                    else if (row % 3 == 0 && column % 3 == 0)
                    {
                        // Denser secondary population: plain Goblins (no Engineer class),
                        // separate from and denser than the GoblinEngineer spawn above.
                        BuildFromBlueprint(world, _goblin, column, row);
                    }
                }

                // UnderGround layer: border walls (its own MapLayer, so distinct from the
                // Ground-layer walls above -- entities on different layers never collide).
                // Mirrors Ground's own split: StoneFloor under the border (so the wall's
                // background reads as a consistent wall floor, not the interior's speckled
                // terrain -- MapWindow's background resolution only ever shows terrain color,
                // never the creature standing on it, so the border cells need dedicated
                // terrain of their own the same way Ground's isWallOrHallway branch already
                // does) and a genuinely randomized dirt/lava mixture everywhere else, unlike
                // Ground's sparse, deterministic lava patches (a fixed pattern one layer down
                // would just look identical, not "random").
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
                }

                // Flying layer: scattered ordinary Fairies. Independent of whatever's on
                // Ground/UnderGround at the same (column,row) -- a different MapLayer, so no
                // collision between them.
                if (row % 8 == 0 && column % 6 == 0)
                {
                    BuildFromBlueprintAtLayer(world, _fairy, column, row, MapLayer.Flying);
                }
            }
        }

        BuildFixtureEntities(world);
    }

    private void BuildFromBlueprint(World.World world, IBlueprint blueprint, int column, int row)
    {
        var entityId = entityManager.CreateEntity();
        blueprint.Build(componentManager, entityId);

        PlaceAt(world, entityId, column, row);
    }

    /// <summary>Same as BuildFromBlueprint, but places at mapLayer instead of preserving whatever Z the blueprint itself set.</summary>
    private void BuildFromBlueprintAtLayer(World.World world, IBlueprint blueprint, int column, int row, MapLayer mapLayer)
    {
        var entityId = entityManager.CreateEntity();
        blueprint.Build(componentManager, entityId);

        ref var transform = ref componentManager.GetDirectPool<TransformComponent>().Get(entityId);
        world.PlaceEntityOnMap(entityId, new Vector3Int(column, row, (int)mapLayer), ref transform);
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

        world.PlaceTerrainOnMap(entityId, column, row, terrainLayer);
    }

    /// <summary>Same placement as BuildFromBlueprint, plus assigning one of the three even-rotation sizes.</summary>
    private void BuildGoblin(World.World world, int column, int row)
    {
        var entityId = entityManager.CreateEntity();
        _goblinEngineer.Build(componentManager, entityId);

        ref var transform = ref componentManager.GetDirectPool<TransformComponent>().Get(entityId);
        transform.Size = GoblinSizes[_goblinsBuilt % GoblinSizes.Length];
        _goblinsBuilt++;

        PlaceAt(world, entityId, column, row);
    }

    /// <summary>
    /// Standalone demonstration entities, placed individually rather than through the main
    /// population loop above. Where BuildGoblin's size rotation already covers "goblins of
    /// every size" in general, these specifically exercise capabilities nothing in the main
    /// loop touches: multiple components of the same type on one entity (MultiComponentPool's
    /// whole reason for existing), removing a component after blueprint construction, and
    /// text long enough to actually word-wrap/hyphenate when selected.
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

        PlaceAt(world, longDescriptionId, 2, 2);

        // Huge (3x3) goblin engineer, placed standalone rather than through BuildGoblin's rotation.
        var hugeId = entityManager.CreateEntity();
        _goblinEngineer.Build(componentManager, hugeId);

        ref var hugeTransform = ref componentManager.GetDirectPool<TransformComponent>().Get(hugeId);
        hugeTransform.Size = new Vector2Byte(3, 3);

        PlaceAt(world, hugeId, 5, 5);

        // Stationary Fairy engineer: race+class composed, then MovementComponent removed so
        // it doesn't wander despite Fairy's own baseline movement mode.
        var stationaryFairyId = entityManager.CreateEntity();
        _fairy.Build(componentManager, stationaryFairyId);
        _engineer.Build(componentManager, stationaryFairyId);
        componentManager.GetPackedPool<MovementComponent>().Remove(stationaryFairyId);

        PlaceAt(world, stationaryFairyId, 1, 1);

        // Ordinary moving Fairy, for contrast against the stationary one above.
        BuildFromBlueprint(world, _fairy, 17, 16);

        // Two RaceComponents on one entity (Goblin base with Fairy layered on top). Movement
        // removed since a grounded-goblin/flying-fairy hybrid has no single coherent
        // movement mode.
        var multiRaceId = entityManager.CreateEntity();
        _goblin.Build(componentManager, multiRaceId);
        _fairy.Build(componentManager, multiRaceId);
        componentManager.GetPackedPool<MovementComponent>().Remove(multiRaceId);

        PlaceAt(world, multiRaceId, 17, 9);

        // Two ClassComponents on one entity (Engineer and Tank both applied to the same Goblin).
        var multiClassId = entityManager.CreateEntity();
        _goblin.Build(componentManager, multiClassId);
        _engineer.Build(componentManager, multiClassId);
        _tank.Build(componentManager, multiClassId);

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
        componentManager.GetPackedPool<OccupancyComponent>().Add(phasingFairyId, new OccupancyComponent(isTiny: false, isPhasing: true));
        componentManager.GetMultiPool<NonBlockingComponent>().Add(phasingFairyId, new NonBlockingComponent());

        PlaceAt(world, phasingFairyId, 17, 16);
    }

    /// <summary>Builds count plain-Goblin-glyph entities, all Tiny, all at the same cell -- for exercising MapWindow's tiny-entity grid/cap.</summary>
    private void BuildTinyGoblins(World.World world, int count, int column, int row)
    {
        for (var i = 0; i < count; i++)
        {
            var entityId = entityManager.CreateEntity();
            _goblin.Build(componentManager, entityId);
            componentManager.GetPackedPool<OccupancyComponent>().Add(entityId, new OccupancyComponent(isTiny: true, isPhasing: false));
            componentManager.GetMultiPool<NonBlockingComponent>().Add(entityId, new NonBlockingComponent());

            PlaceAt(world, entityId, column, row);
        }
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