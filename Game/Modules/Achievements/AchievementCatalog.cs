namespace Game.Modules.Achievements;

/// <summary>
/// Collects every IAchievementDefinition registered during IGameModule.Configure, keyed by Id
/// -- mirrors ActionCatalog. A fresh instance per GameModuleContext, so a dry-run
/// mod-validation trial's registrations never leak into the real build's.
/// </summary>
public sealed class AchievementCatalog
{
    private readonly Dictionary<Guid, IAchievementDefinition> _definitionsById = [];

    public void Register(IAchievementDefinition definition) => _definitionsById[definition.Id] = definition;

    public bool TryGet(Guid achievementId, out IAchievementDefinition definition) =>
        _definitionsById.TryGetValue(achievementId, out definition!);
}
