using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Blueprints;
using Game.Spawning;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Race.Components;
using Microsoft.Xna.Framework;

namespace Tests;

/// <summary>Gives test entities a body plan: one race definition holding the templates, and the pools EntityBodyParts reads.</summary>
/// <remarks>
/// Body parts come from a race definition now (see EntityBodyParts), so a test that wants a Complex
/// entity registers a race and gives the entity its slot rather than adding a component per part.
/// </remarks>
internal sealed class BodyPartTestWorld
{
    private static readonly Guid TestRaceId = new("99999999-0000-0000-0000-00000000000b");

    /// <summary>The definitions registered against each ComponentManager a test built a world for, so a helper that only holds the manager can still resolve its races.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<ComponentManager, BlueprintRegistry> CreaturesByComponentManager = new();

    /// <summary>The races registered for componentManager -- an empty set, remembered, when no body plan was ever given to it.</summary>
    public static BlueprintRegistry CreaturesOf(ComponentManager componentManager) =>
        CreaturesByComponentManager.GetOrAdd(componentManager, static _ => new BlueprintRegistry());

    /// <summary>An EntityBodyParts over componentManager, which must already hold the built-in pools.</summary>
    public static EntityBodyParts PartsOf(ComponentManager componentManager) =>
        EntityBodyParts.For(componentManager, CreaturesOf(componentManager));

    public ComponentManager Components { get; }

    public BlueprintRegistry Definitions { get; }

    public EntityBodyParts BodyParts { get; }

    public PackedComponentPool<BodyPartStateComponent> States { get; }

    private readonly ushort _raceId;

    public BodyPartTestWorld(params BodyPartTemplate[] templates)
        : this(BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 10)), templates)
    {
    }

    public BodyPartTestWorld(ComponentManager components, params BodyPartTemplate[] templates)
    {
        Components = components;
        Definitions = CreaturesOf(components);

        _raceId = Definitions.Register(new BlueprintDefinition(TestRaceId, "Test Race")
        {
            Race = new RaceFacet(templates),
        });

        States = components.GetPackedPool<BodyPartStateComponent>();
        BodyParts = EntityBodyParts.For(components, Definitions);
    }

    /// <inheritdoc cref="WithParts(ComponentManager, int, ValueTuple{string, BodyPartType, float, ushort, bool}[])"/>
    public static BodyPartTestWorld WithParts(int entityId, params (string Name, BodyPartType Type, float Current, ushort Max, bool Vital)[] parts) =>
        WithParts(BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 10)), entityId, parts);

    /// <summary>A world whose race has these parts, given to entityId, each already at the health named.</summary>
    public static BodyPartTestWorld WithParts(ComponentManager components, int entityId, params (string Name, BodyPartType Type, float Current, ushort Max, bool Vital)[] parts)
    {
        // Declaration order is top-to-bottom, the way a race authors its own body plan (Head first,
        // Feet last), so Topmost/Bottommost selection has something meaningful to sort on.
        var world = new BodyPartTestWorld(components, [.. parts.Select((part, index) => new BodyPartTemplate(part.Name, part.Type, (byte)(parts.Length - 1 - index), part.Max, part.Vital))]);
        world.Give(entityId);

        for (var partId = 0; partId < parts.Length; partId++)
        {
            if (parts[partId].Current != parts[partId].Max)
            {
                world.SetHealth(entityId, partId, parts[partId].Current);
            }
        }

        return world;
    }

    /// <summary>Gives the entity this world's body plan.</summary>
    public void Give(int entityId) => Components.Merge(entityId, new RaceSlotsComponent(_raceId));

    /// <summary>A part at a chosen health, for a test that wants an entity already hurt.</summary>
    public void SetHealth(int entityId, int partId, float currentHealth)
    {
        Give(entityId);
        BodyParts.TryGet(entityId, partId, out var part);
        BodyParts.Damage(entityId, partId, part.CurrentHealth - currentHealth, part.MaximumHealth, now: 0, lockoutFrames: 0);
    }
}
