using Engine.ECS.Context;
using Engine.Math;
using Game.Blueprints;
using Game.Blueprints.Composites;
using Game.Bootstrap;
using Game.Spawning;
using Game.Modules.Actions;

namespace Tests.Blueprints;

internal static class BlueprintTestContext
{
    /// <summary>The built-in modules, built once per test thread, because building an entity can write a derived definition into their catalogs.</summary>
    private static readonly ThreadLocal<GameBuildPassResult> BuiltIns = new(static () => BuiltInTestModules.Build(GameBuildPass.CreatePlaceholderMap()));

    /// <summary>This thread's built-in blueprints, registered the way BlueprintsModule does it -- races and classes are looked up in these, and their action grants live on them.</summary>
    public static BlueprintRegistry Definitions => BuiltIns.Value!.Context.Definitions;

    /// <summary>This thread's built-in actions, registered the way CoreActionsModule does it.</summary>
    public static ActionCatalog Actions => BuiltIns.Value!.Context.Actions;

    /// <summary>This thread's built-in items, registered the way CoreItemsModule does it.</summary>
    public static Game.Modules.Inventory.ItemCatalog Items => BuiltIns.Value!.Context.Items;

    public static BlueprintContext ContextFor(this EcsContext ecsContext, int entityId, int seed = 1, BlueprintRegistry? creatures = null) =>
        new(ecsContext.ComponentManager, entityId, new MathUtility(new SeededRandom((ulong)seed)), ecsContext.EntityManager.Keys, (uint)seed, creatures ?? Definitions, Actions, Items);

    /// <summary>The player's blueprint (see FloorBuilder.CreatePlayer).</summary>
    public static ushort PlayerBlueprint => Definitions.GetId(Player.Id);

    /// <summary>A goblin engineer's blueprint (see TestMapBuilder's fixtures).</summary>
    public static ushort GoblinEngineerBlueprint => Definitions.GetId(GoblinEngineer.Id);

    /// <summary>Builds a whole blueprint, includes and all, the way EntityFactory does in the real world.</summary>
    public static void BuildBlueprint(this EcsContext ecsContext, int entityId, ushort blueprintId, int seed = 1) =>
        new EntityBuilder(Definitions, new Game.Modules.Auras.AuraCatalog(), Actions, Items, ecsContext.EntityManager.Keys).Build(ecsContext.ComponentManager, entityId, blueprintId, (uint)seed, now: 0);

    /// <summary>Builds the built-in definition registered as definitionId -- its race or class and its includes as well as its own blueprint.</summary>
    public static void BuildDefinition(this EcsContext ecsContext, int entityId, Guid definitionId, int seed = 1) =>
        ecsContext.BuildBlueprint(entityId, Definitions.GetId(definitionId), seed);

    /// <summary>What the game calls the entity -- its own DisplayTextComponent if it has one, else its blueprint's name for its seed.</summary>
    public static string NameOf(this EcsContext ecsContext, int entityId) =>
        EntityNaming.For(ecsContext.ComponentManager, Definitions).NameOf(entityId);

    /// <summary>How every entity of the built-in definition definitionId looks and is called.</summary>
    public static EntityAppearance AppearanceOf(Guid definitionId) =>
        Definitions.Resolve(Definitions.GetId(definitionId)).Appearance;

    /// <summary>What an entity in this context can actually use -- its own grants plus its blueprint's.</summary>
    public static EntityActions ActionsOf(this EcsContext ecsContext) =>
        EntityActions.For(ecsContext.ComponentManager, Actions, Definitions);
}
