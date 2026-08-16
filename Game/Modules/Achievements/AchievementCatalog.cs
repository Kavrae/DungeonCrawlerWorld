using Engine.Modules;

namespace Game.Modules.Achievements;

/// <summary>Collects every IAchievementDefinition registered during IGameModule.Configure, keyed by Id.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class AchievementCatalog() : Catalog<IAchievementDefinition>(static definition => definition.Id);
