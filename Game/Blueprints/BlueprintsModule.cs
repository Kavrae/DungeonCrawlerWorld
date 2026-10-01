using Engine.ECS.Components;
using Engine.ECS.Systems;
using Game.Blueprints.Classes;
using Game.Blueprints.Composites;
using Game.Blueprints.NPCs.Generic;
using Game.Blueprints.Objects;
using Game.Blueprints.Parts;
using Game.Blueprints.Races;
using Game.Spawning;
using Game.Modules;
using Engine.Modules;

namespace Game.Blueprints;

/// <summary>Registers the built-in blueprint definitions every entity is spawned from, the spawn record each one carries, and the list of parts applied to one after it spawned.</summary>
/// <remarks>Each blueprint declares its whole definition -- id, name, includes, race or class, occupancy, shared actions -- as its own static Definition; this only lists them. Definitions are immutable and their blueprints stateless, so the same instance is registered in every world.</remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class BlueprintsModule : IGameModule
{
    private static readonly BlueprintDefinition[] CoreBlueprints =
    [
        Goblin.Definition,
        Fairy.Definition,
        Ghost.Definition,
        Human.Definition,

        Engineer.Definition,
        Tank.Definition,

        PlayerKit.Definition,
        TreasureChest.Definition,
        HealingShrine.Definition,
        Shop.Definition,
        GeneralShopStock.Definition,
        PotionShopStock.Definition,
        GeneralShop.Definition,
        PotionShop.Definition,
        TestDummyBlueprint.Definition,

        GoblinEngineer.Definition,
        Boss.Definition,
        StationaryPart.Definition,
        LongDescriptionPart.Definition,
        Tiny.Definition,
        Phasing.Definition,

        Player.Definition,
        GoblinForeman.Definition,
        TinyGoblin.Definition,
        PhasingFairy.Definition,
        StationaryFairyEngineer.Definition,
        GoblinFairy.Definition,
        GoblinEngineerTank.Definition,
        LongDescriptionGoblin.Definition,
    ];

    public static readonly Guid ModuleId = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000021");

    public Guid Id => ModuleId;

    public void Configure(GameModuleContext context)
    {
        foreach (var definition in CoreBlueprints)
        {
            context.Definitions.Register(definition);
        }
    }

    public void RegisterComponents(ComponentRegistration registration)
    {
        var componentManager = registration.ComponentManager;

        componentManager.RegisterDirectPool<SpawnRecordComponent>(static (ref existing, incoming) => existing = incoming);

        // Rare: only entities something was applied to at runtime.
        componentManager.RegisterMultiPool<AppliedBlueprintComponent>(initialCapacity: 16);
    }

    public void RegisterBehavior(BehaviorRegistration<GameModuleContext> registration)
    {
    }
}
