using Engine.ECS.Context;
using Engine.Events;
using Engine.Math;
using Game.Blueprints;
using Game.Blueprints.Composites;
using Game.Spawning;
using Game.Modules;
using Game.Modules.Actions;
using Game.Modules.Actions.Definitions;
using Game.World;

namespace Tests.Blueprints;

internal static class BlueprintTestContext
{
    /// <summary>The built-in blueprints, registered once the way BlueprintsModule does it -- races and classes are looked up in these, and their action grants live on them.</summary>
    public static BlueprintRegistry Definitions { get; } = BuildDefinitions();

    /// <summary>The built-in actions, registered the way CoreActionsModule does it.</summary>
    public static ActionCatalog Actions { get; } = BuildActions();

    public static BlueprintContext ContextFor(this EcsContext ecsContext, int entityId, int seed = 1, BlueprintRegistry? creatures = null) =>
        new(ecsContext.ComponentManager, entityId, new MathUtility(new Random(seed)), ecsContext.EntityManager.Keys, (uint)seed, creatures ?? Definitions);

    /// <summary>The player's blueprint (see FloorBuilder.CreatePlayer).</summary>
    public static ushort PlayerBlueprint { get; } = Definitions.GetId(Player.Id);

    /// <summary>A goblin engineer's blueprint (see TestMapBuilder's fixtures).</summary>
    public static ushort GoblinEngineerBlueprint { get; } = Definitions.GetId(GoblinEngineer.Id);

    /// <summary>Builds a whole blueprint, includes and all, the way EntityFactory does in the real world.</summary>
    public static void BuildBlueprint(this EcsContext ecsContext, int entityId, ushort blueprintId, int seed = 1) =>
        new EntityFactory(Definitions, ecsContext.EntityManager.Keys).Build(ecsContext.ComponentManager, entityId, blueprintId, (uint)seed, now: 0);

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

    private static BlueprintRegistry BuildDefinitions()
    {
        var definitions = new BlueprintRegistry();
        new BlueprintsModule().Configure(ModuleContext(creatures: definitions));
        return definitions;
    }

    private static ActionCatalog BuildActions()
    {
        var context = ModuleContext(creatures: new BlueprintRegistry());
        new CoreActionsModule().Configure(context);
        return context.Actions;
    }

    private static GameModuleContext ModuleContext(BlueprintRegistry creatures) =>
        new(new Game.World.World(new Map(new Vector3Int(1, 1, 1))), new MathUtility(), new EventBus()) { PlayerQuery = TestPlayerQuery.NoPlayer, Definitions = creatures };
}
