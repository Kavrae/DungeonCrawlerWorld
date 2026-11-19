using System.Globalization;
using Engine.ECS.Entities;
using Engine.ECS.Properties;
using Game.Blueprints;
using Game.Modules;
using Game.Modules.AbilityScores;
using Game.Modules.Actions;
using Game.Modules.Achievements;
using Game.Modules.Auras;
using Game.Modules.Health;
using Game.Modules.Inventory;
using Game.Spawning;
using Game.Terrain;

namespace Game.ComponentProperties;

/// <summary>The session's meaning resolvers for display: the engine's, then the game's own, each beside the catalog or registry that names what it stands for.</summary>
public static class GameComponentPropertyMeanings
{
    public static ComponentPropertyMeanings For(GameModuleContext context)
    {
        var meanings = ComponentPropertyMeanings.WithEngineResolvers(context.SimulationClock);
        meanings.Register(ComponentPropertyMeaningNames.EntityKey, new EntityKeyResolver(context.EntityKeys, context.Definitions));
        meanings.Register(ComponentPropertyMeaningNames.EntityId, new EntityIdResolver(context.Definitions));
        meanings.Register(GamePropertyMeanings.BlueprintId, new BlueprintResolver(context.Definitions));
        meanings.Register(GamePropertyMeanings.AuraId, new AuraResolver(context.Auras));
        meanings.Register(GamePropertyMeanings.ItemId, new ItemResolver(context.Items, byHandle: true));
        meanings.Register(GamePropertyMeanings.ItemDefinitionId, new ItemResolver(context.Items, byHandle: false));
        meanings.Register(GamePropertyMeanings.ActionId, new ActionResolver(context.Actions));
        meanings.Register(GamePropertyMeanings.AchievementId, new AchievementResolver(context.Achievements));
        meanings.Register(GamePropertyMeanings.TerrainTypeId, new TerrainResolver(context.Terrain));
        meanings.Register(GamePropertyMeanings.BodyPartId, new BodyPartResolver(context.Definitions));
        meanings.Register(GamePropertyMeanings.AbilityScoreType, new AbilityScoreTypeResolver());
        meanings.Register(GamePropertyMeanings.UtcTicks, new UtcTicksResolver());
        meanings.Register(GamePropertyMeanings.Floor, new FloorResolver());
        return meanings;
    }

    /// <summary>The entity a key names, read where the value was: "none" for no key, "gone" for one whose entity was destroyed.</summary>
    private sealed class EntityKeyResolver(EntityKeys keys, BlueprintRegistry definitions) : IComponentPropertyMeaningResolver
    {
        public string? DescribeInteger(in ComponentPropertyContext context, long value)
        {
            var key = new EntityKey(unchecked((ulong)value));
            if (key.IsNone)
            {
                return "none";
            }

            return keys.TryGetEntityId(key, out var entityId)
                ? $"{EntityNaming.For(context.Source, definitions).NameOf(entityId)} (entity {entityId})"
                : $"{key} (gone)";
        }
    }

    private sealed class EntityIdResolver(BlueprintRegistry definitions) : IComponentPropertyMeaningResolver
    {
        public string? DescribeInteger(in ComponentPropertyContext context, long value) =>
            $"{EntityNaming.For(context.Source, definitions).NameOf((int)value)} (entity {value})";
    }

    private sealed class BlueprintResolver(BlueprintRegistry definitions) : IComponentPropertyMeaningResolver
    {
        public string? DescribeInteger(in ComponentPropertyContext context, long value) =>
            value == BlueprintRegistry.None ? "none"
            : definitions.TryGet((ushort)value, out var definition) ? $"{definition.Name} ({value})"
            : null;

        public bool TryGetChoices(in ComponentPropertyContext context, out IReadOnlyList<ComponentPropertyChoice> choices)
        {
            var list = new List<ComponentPropertyChoice> { new("none", ComponentPropertyValue.FromInteger(BlueprintRegistry.None)) };
            for (var id = 1; id <= definitions.Count; id++)
            {
                if (definitions.TryGet((ushort)id, out var definition))
                {
                    list.Add(new ComponentPropertyChoice($"{definition.Name} ({id})", ComponentPropertyValue.FromInteger(id)));
                }
            }

            choices = list;
            return true;
        }
    }

    private sealed class AuraResolver(AuraCatalog auras) : IComponentPropertyMeaningResolver
    {
        public string? DescribeInteger(in ComponentPropertyContext context, long value) =>
            value >= 0 && value < auras.Count ? $"{auras.Get((byte)value).Name} ({value})" : null;

        public bool TryGetChoices(in ComponentPropertyContext context, out IReadOnlyList<ComponentPropertyChoice> choices)
        {
            choices = Enumerable.Range(0, auras.Count)
                .Select(id => new ComponentPropertyChoice($"{auras.Get((byte)id).Name} ({id})", ComponentPropertyValue.FromInteger(id)))
                .ToList();
            return true;
        }
    }

    /// <summary>An item by its session handle or its Guid.</summary>
    private sealed class ItemResolver(ItemCatalog items, bool byHandle) : IComponentPropertyMeaningResolver
    {
        public string? DescribeInteger(in ComponentPropertyContext context, long value) =>
            value is >= 0 and <= ushort.MaxValue ? Describe(ItemIds.DefinitionIdOf((ushort)value)) : null;

        public string? DescribeGuid(in ComponentPropertyContext context, Guid value) => Describe(value);

        private string? Describe(Guid id) => items.TryGet(id, out var item) ? item.Name : null;

        public bool TryGetChoices(in ComponentPropertyContext context, out IReadOnlyList<ComponentPropertyChoice> choices)
        {
            choices = items.Summaries
                .OrderBy(summary => summary.Name, StringComparer.Ordinal)
                .Select(summary => new ComponentPropertyChoice(summary.Name, byHandle ? ComponentPropertyValue.FromInteger(ItemIds.IdFor(summary.Id)) : ComponentPropertyValue.FromGuid(summary.Id)))
                .ToList();
            return true;
        }
    }

    private sealed class ActionResolver(ActionCatalog actions) : IComponentPropertyMeaningResolver
    {
        public string? DescribeGuid(in ComponentPropertyContext context, Guid value) =>
            value == Guid.Empty ? "none" : actions.TryGet(value, out var action) ? action.Name : null;

        public bool TryGetChoices(in ComponentPropertyContext context, out IReadOnlyList<ComponentPropertyChoice> choices)
        {
            choices = actions.Summaries
                .OrderBy(summary => summary.Name, StringComparer.Ordinal)
                .Select(summary => new ComponentPropertyChoice(summary.Name, ComponentPropertyValue.FromGuid(summary.Id)))
                .ToList();
            return true;
        }
    }

    private sealed class AchievementResolver(AchievementCatalog achievements) : IComponentPropertyMeaningResolver
    {
        public string? DescribeGuid(in ComponentPropertyContext context, Guid value) =>
            achievements.TryGet(value, out var achievement) ? achievement.Name : null;
    }

    private sealed class TerrainResolver(TerrainRegistry terrain) : IComponentPropertyMeaningResolver
    {
        public string? DescribeInteger(in ComponentPropertyContext context, long value) =>
            value is >= 0 and <= ushort.MaxValue && terrain.TryGet((ushort)value, out var definition) ? definition.Name : null;

        public bool TryGetChoices(in ComponentPropertyContext context, out IReadOnlyList<ComponentPropertyChoice> choices)
        {
            var list = new List<ComponentPropertyChoice>();
            for (var id = 1; id <= terrain.Count; id++)
            {
                if (terrain.TryGet((ushort)id, out var definition))
                {
                    list.Add(new ComponentPropertyChoice(definition.Name, ComponentPropertyValue.FromInteger(id)));
                }
            }

            choices = list;
            return true;
        }
    }

    /// <summary>A body part by its id, named by the entity's race; a part slot the race doesn't have is no row.</summary>
    private sealed class BodyPartResolver(BlueprintRegistry definitions) : IComponentPropertyMeaningResolver
    {
        public string? DescribeInteger(in ComponentPropertyContext context, long value) =>
            TryNameRow(context, (int)value, out var name) ? name : null;

        public bool TryNameRow(in ComponentPropertyContext context, int index, out string label)
        {
            if (EntityBodyParts.For(context.Source, definitions).TryGet(context.EntityId, index, out var part))
            {
                label = part.Name;
                return true;
            }

            label = "";
            return false;
        }
    }

    private sealed class AbilityScoreTypeResolver : IComponentPropertyMeaningResolver
    {
        public bool TryNameRow(in ComponentPropertyContext context, int index, out string label)
        {
            label = ((AbilityScoreType)index).ToString();
            return true;
        }
    }

    private sealed class UtcTicksResolver : IComponentPropertyMeaningResolver
    {
        public string? DescribeInteger(in ComponentPropertyContext context, long value) =>
            value >= DateTime.MinValue.Ticks && value <= DateTime.MaxValue.Ticks
                ? new DateTime(value, DateTimeKind.Utc).ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
                : null;
    }

    /// <summary>A floor number, where 0 means none: a class that never expires.</summary>
    private sealed class FloorResolver : IComponentPropertyMeaningResolver
    {
        public string? DescribeInteger(in ComponentPropertyContext context, long value) => value == 0 ? "never" : null;
    }
}
