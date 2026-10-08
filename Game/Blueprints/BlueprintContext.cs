using Engine.ECS.Components;
using Engine.ECS.Entities;
using Engine.Math;
using Game.Modules.Actions;

namespace Game.Blueprints;

/// <summary>The entity a blueprint builds, and what it builds it with.</summary>
/// <param name="Rolls">The only random sequence a build may draw from -- EntityFactory seeds it per creature, so drawing from anything else breaks SpawnRecordComponent's rebuild.</param>
/// <param name="EntityKeys">The key table of the world ComponentManager belongs to.</param>
/// <param name="Seed">The entity's own seed (Rolls starts from it for a creature). For choices that must be known without building, like appearance -- see EntityAppearance.</param>
/// <param name="Definitions">The session's blueprint definitions -- what a blueprint looks a race or class up in (see EntityBodyParts, ActionSource).</param>
/// <param name="Actions">The session's action definitions -- what an action granted by id is read from (see ActionGrantEffects).</param>
/// <cleanupVersion>1</cleanupVersion>
public readonly record struct BlueprintContext(ComponentManager ComponentManager, int EntityId, MathUtility Rolls, EntityKeys EntityKeys, uint Seed, BlueprintRegistry Definitions, ActionCatalog Actions);
