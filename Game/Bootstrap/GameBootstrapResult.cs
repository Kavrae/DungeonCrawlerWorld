using Engine.ECS.Context;
using Engine.ECS.Systems;
using Engine.Modules;
using Engine.Settings;
using Game.Modules.Actions;
using Game.Modules.Inventory;
using Game.Modules.ProcessingTier;
using Game.Modules.StatusEffects;
using Game.World;
using Game.Blueprints;

namespace Game.Bootstrap;

public sealed record GameBootstrapResult(EcsContext EcsContext, World.World World, IReadOnlyList<ModuleFailure> Failures, SettingValues Settings, IReadOnlyList<SettingsFailure> SettingsFailures, ActionCatalog ActionCatalog, FrameEventBuffer<EntityMovedEvent> MovedEntities, ItemCatalog ItemCatalog, Modules.Lootboxes.LootboxCatalog LootboxCatalog, Modules.Lootboxes.LootboxOpener LootboxOpener, StatusEffectDisplayRegistry StatusEffectDisplays, LocalTierRoster LocalTierRoster, ProcessingTierResolver ProcessingTierResolver, Terrain.TerrainRegistry Terrain, Blueprints.BlueprintRegistry Definitions, Spawning.SpawnRecordRebuilder SpawnRecordRebuilder, Spawning.CreatureSkeletons Skeletons, Spawning.EntityFactory Factory, EntityTeleporter Teleporter);