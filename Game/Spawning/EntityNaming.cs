using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules.Core.Components;
using Game.Blueprints;

namespace Game.Spawning;

/// <summary>What an entity is called and how it describes itself: its own DisplayTextComponent when it has one, else what its blueprint calls it.</summary>
/// <remarks>
/// An entity's name and description come from its blueprint's EntityAppearance (BlueprintRegistry.NameFor),
/// shared by every entity of that blueprint rather than written to each one -- so a DisplayTextComponent
/// means "this one entity is called something its blueprint would not call it", such as a container
/// renamed by ContainerDestructionSystem. The class names and name suffixes of parts applied to it after it
/// spawned (EntityFactory.Apply) follow its blueprint's name, unless the blueprint names it outright. The
/// same resolution serves an entity that has never been built, since it reads the spawn record rather
/// than anything a build writes.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class EntityNaming(
    PackedComponentPool<DisplayTextComponent> displayTexts,
    DirectComponentPool<SpawnRecordComponent>? spawnRecords,
    BlueprintRegistry? creatures,
    MultiComponentPool<AppliedBlueprintComponent>? appliedParts = null)
{
    /// <summary>What an entity is called when neither it nor its blueprint names it.</summary>
    public const string UnknownName = "Unknown";

    private readonly PackedComponentPool<DisplayTextComponent> _displayTexts = displayTexts ?? throw new ArgumentNullException(nameof(displayTexts));

    /// <summary>Builds one from a ComponentManager, for callers that hold the manager rather than the pools.</summary>
    public static EntityNaming For(ComponentManager componentManager, BlueprintRegistry? creatures)
    {
        ArgumentNullException.ThrowIfNull(componentManager);

        return new EntityNaming(
            componentManager.GetPackedPool<DisplayTextComponent>(),
            componentManager.IsRegistered<SpawnRecordComponent>() ? componentManager.GetDirectPool<SpawnRecordComponent>() : null,
            creatures,
            componentManager.IsRegistered<AppliedBlueprintComponent>() ? componentManager.GetMultiPool<AppliedBlueprintComponent>() : null);
    }

    /// <summary>The entity's name without building a resolver, for a caller on a hot path that holds only the manager (ActionSource.FromEntity, once per hit).</summary>
    public static bool TryResolveName(ComponentManager componentManager, BlueprintRegistry? creatures, int entityId, out string name)
    {
        ArgumentNullException.ThrowIfNull(componentManager);

        if (componentManager.GetPackedPool<DisplayTextComponent>().TryGetReadonly(entityId, out var displayText) && !string.IsNullOrEmpty(displayText.Name))
        {
            name = displayText.Name;
            return true;
        }

        if (creatures is not null
            && componentManager.IsRegistered<SpawnRecordComponent>()
            && componentManager.GetDirectPool<SpawnRecordComponent>().TryGetReadonly(entityId, out var record)
            && BlueprintName(
                creatures,
                record,
                entityId,
                componentManager.IsRegistered<AppliedBlueprintComponent>() ? componentManager.GetMultiPool<AppliedBlueprintComponent>() : null) is { Length: > 0 } creatureName)
        {
            name = creatureName;
            return true;
        }

        name = string.Empty;
        return false;
    }

    /// <summary>The entity's name, or fallback when nothing names it.</summary>
    public string NameOf(int entityId, string fallback = UnknownName) =>
        TryGetName(entityId, out var name) ? name : fallback;

    /// <summary>The entity's name, or false when nothing names it.</summary>
    public bool TryGetName(int entityId, out string name)
    {
        if (_displayTexts.TryGetReadonly(entityId, out var displayText) && !string.IsNullOrEmpty(displayText.Name))
        {
            name = displayText.Name;
            return true;
        }

        if (creatures is not null && spawnRecords?.TryGetReadonly(entityId, out var record) == true && BlueprintName(creatures, record, entityId, appliedParts) is { Length: > 0 } creatureName)
        {
            name = creatureName;
            return true;
        }

        name = string.Empty;
        return false;
    }

    /// <summary>The entity's description, or empty when it has none.</summary>
    public string DescriptionOf(int entityId)
    {
        if (_displayTexts.TryGetReadonly(entityId, out var displayText) && !string.IsNullOrEmpty(displayText.Description))
        {
            return displayText.Description;
        }

        return creatures is not null
            && spawnRecords?.TryGetReadonly(entityId, out var record) == true
            && creatures.TryGetAppearance(record.BlueprintId, out var appearance)
                ? appearance.Description
                : string.Empty;
    }

    /// <summary>What record's blueprint calls the entity, followed by each applied part's name suffix and class name in the order they were applied -- unless the blueprint gives it an explicit name, which nothing extends (the player stays "Player1").</summary>
    private static string BlueprintName(
        BlueprintRegistry creatures,
        SpawnRecordComponent record,
        int entityId,
        MultiComponentPool<AppliedBlueprintComponent>? appliedParts)
    {
        if (!creatures.TryResolve(record.BlueprintId, out var blueprint))
        {
            return string.Empty;
        }

        var name = blueprint.NameFor(record.Seed);
        if (blueprint.Appearance.HasExplicitName || appliedParts is null || appliedParts.GetFirstDenseIndex(entityId) == -1)
        {
            return name;
        }

        var applied = new List<AppliedBlueprintComponent>();
        appliedParts.CopyAll(entityId, applied);
        applied.Sort(static (left, right) => left.Order.CompareTo(right.Order));

        foreach (var part in applied)
        {
            if (!creatures.TryGet(part.BlueprintId, out var definition))
            {
                continue;
            }

            if (definition.Appearance?.NameSuffix is { } suffix)
            {
                name = Joined(name, suffix);
            }

            if (definition.Class is not null)
            {
                name = Joined(name, definition.Name);
            }
        }

        return name;
    }

    private static string Joined(string name, string suffix) => name.Length == 0 ? suffix : $"{name} {suffix}";
}
