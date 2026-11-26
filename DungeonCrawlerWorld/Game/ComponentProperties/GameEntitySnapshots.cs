using System.Text.Json;
using Engine.Content;
using Engine.ECS.Properties;
using Game.Blueprints;
using Game.Modules;
using Game.Modules.AbilityScores;
using Game.Modules.Actions;
using Game.Modules.Auras;
using Game.Modules.Core.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Lootboxes;
using Game.Modules.ProcessingTier.Components;
using Game.World;

namespace Game.ComponentProperties;

/// <summary>A session's entity snapshots: every meaning's persistent form, definitions through their content codecs, and the host that creates, places and finishes each restored entity.</summary>
/// <remarks>
/// Phase 4 of PLAN-generated-component-property-access.md, the per-pool half of a save; a test consumer only. Session
/// ids (blueprints, auras, behaviors, item handles, terrain) are saved by the Guid or key they stand for, so a snapshot
/// restores into another build of the same content though its ids differ. A skeleton isn't captured: its spawn record
/// rebuilds it, which is what a neighborhood save keeps.
/// </remarks>
public sealed class GameEntitySnapshots(GameModuleContext context, World.World world)
{
    public ComponentPropertyPersistence Persistence { get; } = PersistenceFor(context);

    /// <summary>Every saved property with no persistent form, by name; empty when any built entity can be saved.</summary>
    public IReadOnlyList<string> Check() => EntitySnapshots.Check(context.ComponentManager, Persistence);

    /// <summary>The entities' components on the current frame; refused, naming it, for a skeleton.</summary>
    public EntitySnapshotCaptureResult Capture(IReadOnlyList<int> entityIds)
    {
        var skeletons = entityIds.Where(context.EntityFactory.Skeletons.IsSkeleton).Select(entityId => $"Entity {entityId} is a skeleton: its spawn record rebuilds it.").ToList();
        return skeletons.Count > 0
            ? new EntitySnapshotCaptureResult(null, skeletons)
            : EntitySnapshots.Capture(context.ComponentManager, entityIds, context.SimulationClock.CurrentFrame, Persistence);
    }

    /// <summary>Restores a snapshot onto fresh entities on the current frame, between frames: each created tiered where it stood, its components added, then placed on the map and its ability score totals recomputed.</summary>
    public EntitySnapshotRestoreResult Restore(string json)
    {
        var host = new Host(context, world);
        var result = EntitySnapshots.Restore(context.ComponentManager, json, context.SimulationClock.CurrentFrame, Persistence, host);
        return host.Problems.Count == 0 ? result : result with { Problems = [.. result.Problems, .. host.Problems] };
    }

    private static ComponentPropertyPersistence PersistenceFor(GameModuleContext context)
    {
        var persistence = ComponentPropertyPersistence.WithEngineMeanings();
        var blueprints = new BlueprintPersistence(context.Definitions);
        persistence.Register(GamePropertyMeanings.BlueprintId, blueprints);
        persistence.Register(GamePropertyMeanings.BehaviorSetId, blueprints);
        persistence.Register(GamePropertyMeanings.AuraId, new AuraPersistence(context));
        persistence.Register(GamePropertyMeanings.ItemId, new ItemHandlePersistence());
        persistence.Register(GamePropertyMeanings.TerrainTypeId, new TerrainPersistence(context.Terrain));
        persistence.Register(ComponentPropertyMeaningNames.BehaviorId, new BehaviorPersistence(context));
        persistence.RegisterAsStored(
            GamePropertyMeanings.ActionId,
            GamePropertyMeanings.MenuCommandId,
            GamePropertyMeanings.AchievementId,
            GamePropertyMeanings.ItemDefinitionId,
            GamePropertyMeanings.BodyPartId,
            GamePropertyMeanings.AbilityScoreType,
            GamePropertyMeanings.SpriteName,
            GamePropertyMeanings.UtcTicks,
            GamePropertyMeanings.Floor);
        persistence.RegisterRemapped(GamePropertyMeanings.StackInstanceId, static () => InventoryItemStackComponent.NextStackInstanceId(), noneValue: 0);
        persistence.RegisterRemapped(GamePropertyMeanings.HeldGrantKey, () => context.Toggles.IssueKey(), noneValue: 0);

        var references = new SessionReferences(context);
        var actions = new CodecPersistence<ActionDefinition>(context.ContentTypes, references);
        var items = new CodecPersistence<ItemDefinition>(context.ContentTypes, references);
        persistence.RegisterDefinition(typeof(ActionDefinition), actions);
        persistence.RegisterDefinition(typeof(ItemDefinition), items);
        persistence.RegisterDefinition(typeof(ActivatableDefinition), new ActivatablePersistence(actions, items));
        persistence.RegisterDefinition(typeof(LootboxReward), new CodecPersistence<LootboxReward>(context.ContentTypes, references));
        return persistence;
    }

    /// <summary>Creates each restored entity tiered where it stood, restores where it stands and its tier itself, and places and finishes it once every component is back.</summary>
    private sealed class Host(GameModuleContext context, World.World world) : IEntitySnapshotHost
    {
        private readonly Dictionary<int, TransformComponent> _savedTransforms = [];

        public List<string> Problems { get; } = [];

        public int CreateEntity(EntitySnapshotEntity saved)
        {
            if (!saved.TryBuild<TransformComponent>(out var transform, out var problem))
            {
                Problems.Add($"Entity {saved.Index}: {problem}; restored with no position.");
                return context.EntityManager.CreateEntity();
            }

            var entityId = context.ProcessingTierResolver.CreateEntityAt(context.EntityManager, transform.Position);
            context.ComponentManager.GetDirectPool<TransformComponent>().Add(entityId, new TransformComponent(TransformComponent.UnplacedOn((MapLayer)transform.Position.Z), transform.Size));
            _savedTransforms[entityId] = transform;
            return entityId;
        }

        public bool RestoresItself(Type componentType) => componentType == typeof(TransformComponent) || componentType == typeof(ProcessingTierComponent);

        public void Restored(int entityId, EntitySnapshotEntity saved)
        {
            AbilityScoreEffects.RecomputeAll(context.ComponentManager, entityId);
            if (!_savedTransforms.TryGetValue(entityId, out var savedTransform) || savedTransform.Position.X == TransformComponent.UnplacedCoordinate)
            {
                return;
            }

            Engine.Math.Vector3Int placed;
            using (var transform = context.ComponentManager.GetDirectPool<TransformComponent>().Write(entityId))
            {
                world.PlaceEntityOnMap(entityId, savedTransform.Position, ref transform.Value);
                placed = transform.Value.Position;
            }

            if (placed != savedTransform.Position)
            {
                Problems.Add($"Entity {saved.Index}: {savedTransform.Position} can't take it; left off the map.");
                return;
            }

            context.EntityFactory.SpawnMoves.Record(new EntityMovedEvent(entityId, placed, placed, savedTransform.Size));
        }
    }

    /// <summary>A blueprint by its Guid, or a composed one (BlueprintRegistry.Compose: an NPC's behavior set) as the list of what it includes, composed again on restore; none as null.</summary>
    private sealed class BlueprintPersistence(BlueprintRegistry definitions) : IComponentPropertyMeaningPersistence
    {
        public bool TryWrite(Utf8JsonWriter json, ComponentPropertyDescriptor property, in ComponentPropertyValue value, EntitySnapshotCapture capture)
        {
            if (value.Integer == BlueprintRegistry.None)
            {
                json.WriteNullValue();
                return true;
            }

            if (!definitions.TryGet((ushort)value.Integer, out var definition))
            {
                return false;
            }

            if (!BlueprintRegistry.IsComposed(definition))
            {
                json.WriteStringValue(ContentGuid.Format(definition.Id));
                return true;
            }

            json.WriteStartArray();
            foreach (var include in definition.Includes)
            {
                json.WriteStringValue(ContentGuid.Format(include));
            }

            json.WriteEndArray();
            return true;
        }

        public bool TryRead(JsonElement element, ComponentPropertyDescriptor property, EntitySnapshotRestore restore, out ComponentPropertyValue value, out string? problem)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                return TryCompose(element, out value, out problem);
            }

            if (TryReadGuid(element, out var guid, out problem, ifNull: BlueprintRegistry.None, out value) || problem is not null)
            {
                return problem is null;
            }

            return TryResolve(definitions.TryGetId(guid, out var id), id, $"no blueprint {guid} is registered", out value, out problem);
        }

        private bool TryCompose(JsonElement element, out ComponentPropertyValue value, out string? problem)
        {
            var includes = new List<Guid>();
            foreach (var include in element.EnumerateArray())
            {
                if (TryReadGuid(include, out var guid, out problem, ifNull: null, out value) || problem is not null)
                {
                    return false;
                }

                if (!definitions.TryGetId(guid, out _))
                {
                    problem = $"no blueprint {guid} is registered to compose";
                    return false;
                }

                includes.Add(guid);
            }

            value = ComponentPropertyValue.FromInteger(definitions.Compose([.. includes]));
            problem = null;
            return true;
        }
    }

    /// <summary>An aura by its Guid, registered from the session's content if nothing has loaded it yet.</summary>
    private sealed class AuraPersistence(GameModuleContext context) : IComponentPropertyMeaningPersistence
    {
        public bool TryWrite(Utf8JsonWriter json, ComponentPropertyDescriptor property, in ComponentPropertyValue value, EntitySnapshotCapture capture)
        {
            if ((ulong)value.Integer >= (ulong)context.Auras.Count)
            {
                return false;
            }

            json.WriteStringValue(ContentGuid.Format(context.Auras.Get((byte)value.Integer).Id));
            return true;
        }

        public bool TryRead(JsonElement element, ComponentPropertyDescriptor property, EntitySnapshotRestore restore, out ComponentPropertyValue value, out string? problem)
        {
            if (TryReadGuid(element, out var guid, out problem, ifNull: null, out value) || problem is not null)
            {
                return problem is null;
            }

            if (!context.Auras.TryGetId(guid, out var auraId))
            {
                if (!context.Content.TryLoadAura(guid, out var aura))
                {
                    problem = $"no aura {guid} is in this session's content";
                    return false;
                }

                auraId = context.Auras.Register(aura);
            }

            value = ComponentPropertyValue.FromInteger(auraId);
            return true;
        }
    }

    /// <summary>An item handle by the item definition's Guid; handles are process-wide, so any Guid takes one.</summary>
    private sealed class ItemHandlePersistence : IComponentPropertyMeaningPersistence
    {
        public bool TryWrite(Utf8JsonWriter json, ComponentPropertyDescriptor property, in ComponentPropertyValue value, EntitySnapshotCapture capture)
        {
            json.WriteStringValue(ContentGuid.Format(ItemIds.DefinitionIdOf((ushort)value.Integer)));
            return true;
        }

        public bool TryRead(JsonElement element, ComponentPropertyDescriptor property, EntitySnapshotRestore restore, out ComponentPropertyValue value, out string? problem)
        {
            if (TryReadGuid(element, out var guid, out problem, ifNull: null, out value) || problem is not null)
            {
                return problem is null;
            }

            value = ComponentPropertyValue.FromInteger(ItemIds.IdFor(guid));
            return true;
        }
    }

    /// <summary>A terrain type by its key; none as null.</summary>
    private sealed class TerrainPersistence(Terrain.TerrainRegistry terrain) : IComponentPropertyMeaningPersistence
    {
        public bool TryWrite(Utf8JsonWriter json, ComponentPropertyDescriptor property, in ComponentPropertyValue value, EntitySnapshotCapture capture)
        {
            if (value.Integer == Terrain.TerrainRegistry.None)
            {
                json.WriteNullValue();
                return true;
            }

            if (!terrain.TryGet((ushort)value.Integer, out var definition))
            {
                return false;
            }

            json.WriteStringValue(definition.Key);
            return true;
        }

        public bool TryRead(JsonElement element, ComponentPropertyDescriptor property, EntitySnapshotRestore restore, out ComponentPropertyValue value, out string? problem)
        {
            problem = null;
            if (element.ValueKind == JsonValueKind.Null)
            {
                value = ComponentPropertyValue.FromInteger(Terrain.TerrainRegistry.None);
                return true;
            }

            return TryResolve(terrain.TryGetId(element.GetString() ?? string.Empty, out var id), id, $"no terrain \"{element.GetString()}\" is registered", out value, out problem);
        }
    }

    /// <summary>An NPC behavior by its Guid; none as null.</summary>
    private sealed class BehaviorPersistence(GameModuleContext context) : IComponentPropertyMeaningPersistence
    {
        public bool TryWrite(Utf8JsonWriter json, ComponentPropertyDescriptor property, in ComponentPropertyValue value, EntitySnapshotCapture capture)
        {
            if (value.Integer == 0)
            {
                json.WriteNullValue();
                return true;
            }

            if (context.NpcBehaviors.BySessionId((ushort)value.Integer) is not { } behavior)
            {
                return false;
            }

            json.WriteStringValue(ContentGuid.Format(behavior.Id));
            return true;
        }

        public bool TryRead(JsonElement element, ComponentPropertyDescriptor property, EntitySnapshotRestore restore, out ComponentPropertyValue value, out string? problem)
        {
            if (TryReadGuid(element, out var guid, out problem, ifNull: 0, out value) || problem is not null)
            {
                return problem is null;
            }

            if (!context.NpcBehaviors.TryGet(guid, out _))
            {
                problem = $"no behavior {guid} is registered";
                return false;
            }

            value = ComponentPropertyValue.FromInteger(context.NpcBehaviors.SessionIdOf(guid));
            return true;
        }
    }

    /// <summary>A definition written as its content record, through its codec, and read back resolving its auras against the session.</summary>
    private sealed class CodecPersistence<T>(ContentTypeRegistry types, IContentReferences references) : IComponentDefinitionPersistence
        where T : class
    {
        public void Write(Utf8JsonWriter json, object definition) => json.WriteRawValue(ContentWriter.WriteRecord(types, (T)definition));

        public bool TryRead(JsonElement element, out object definition, out string? problem)
        {
            try
            {
                definition = ContentObject.Read<T>(element, types, references);
                problem = null;
                return true;
            }
            catch (ContentFormatException exception)
            {
                definition = null!;
                problem = exception.Message;
                return false;
            }
        }
    }

    /// <summary>An action's or an item's definition, whichever a windup captured: an object naming which, holding its record.</summary>
    private sealed class ActivatablePersistence(CodecPersistence<ActionDefinition> actions, CodecPersistence<ItemDefinition> items) : IComponentDefinitionPersistence
    {
        private const string ActionKey = "action";
        private const string ItemKey = "item";

        public void Write(Utf8JsonWriter json, object definition)
        {
            json.WriteStartObject();
            if (definition is ActionDefinition)
            {
                json.WritePropertyName(ActionKey);
                actions.Write(json, definition);
            }
            else
            {
                json.WritePropertyName(ItemKey);
                items.Write(json, definition);
            }

            json.WriteEndObject();
        }

        public bool TryRead(JsonElement element, out object definition, out string? problem)
        {
            if (element.TryGetProperty(ActionKey, out var action))
            {
                return actions.TryRead(action, out definition, out problem);
            }

            if (element.TryGetProperty(ItemKey, out var item))
            {
                return items.TryRead(item, out definition, out problem);
            }

            definition = null!;
            problem = $"an activatable definition is saved under \"{ActionKey}\" or \"{ItemKey}\"";
            return false;
        }
    }

    /// <summary>The auras a restored definition names, the session's own, else its content's, registered as they load; and a scroll's spell, the session's.</summary>
    private sealed class SessionReferences(GameModuleContext context) : IContentReferences
    {
        public bool TryResolve<TValue>(Guid id, out TValue value)
            where TValue : class
        {
            if (typeof(TValue) == typeof(AuraDefinition))
            {
                if (context.Auras.TryGetId(id, out var auraId))
                {
                    value = (TValue)(object)context.Auras.Get(auraId);
                    return true;
                }

                if (context.Content.TryLoadAura(id, out var aura))
                {
                    context.Auras.Register(aura);
                    value = (TValue)(object)aura;
                    return true;
                }
            }

            if (typeof(TValue) == typeof(ActionDefinition) && context.Actions.TryGet(id, out var action))
            {
                value = (TValue)(object)action;
                return true;
            }

            value = null!;
            return false;
        }
    }

    /// <summary>A saved Guid; for null, ifNull's value when the meaning has a none, else a problem. True when value is settled (none); false with no problem when guid is to be resolved.</summary>
    private static bool TryReadGuid(JsonElement element, out Guid guid, out string? problem, long? ifNull, out ComponentPropertyValue value)
    {
        guid = default;
        value = default;
        problem = null;
        if (element.ValueKind == JsonValueKind.Null)
        {
            if (ifNull is { } none)
            {
                value = ComponentPropertyValue.FromInteger(none);
                return true;
            }

            problem = "null names nothing here";
            return false;
        }

        if (element.ValueKind != JsonValueKind.String || !ContentGuid.TryParse(element.GetString() ?? string.Empty, out guid))
        {
            problem = "expected a Guid";
        }

        return false;
    }

    private static bool TryResolve(bool found, long id, string notFound, out ComponentPropertyValue value, out string? problem)
    {
        value = found ? ComponentPropertyValue.FromInteger(id) : default;
        problem = found ? null : notFound;
        return found;
    }
}
