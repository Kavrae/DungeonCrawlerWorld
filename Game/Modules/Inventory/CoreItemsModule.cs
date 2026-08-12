using Engine.ECS.Components;
using Engine.ECS.Systems;
using Game.Modules.Inventory.Definitions;

namespace Game.Modules.Inventory;

/// <summary>
/// Registers the first real, permanent item catalog -- race/class-agnostic items any entity can
/// carry. See PlayerBlueprint for where these are granted. Mirrors CoreActionsModule/
/// AchievementModule's static Definitions list, one file per item under Definitions/.
/// </summary>
public sealed class CoreItemsModule : IGameModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000011");

    public IReadOnlyList<Type> Dependencies { get; } = [];

    private static readonly IReadOnlyList<Func<ItemDefinition>> Definitions = [
        HealthPotion.Build,
        ManaPotion.Build,
        HotkeyExpansionPotion.Build,
        DamagePotion.Build,
        ToxicPotion.Build,
        ToxicIdol.Build,
    ];

    public void Configure(GameModuleContext context)
    {
        foreach (var build in Definitions)
        {
            context.Items.Register(build());
        }
    }

    public void RegisterComponents(ComponentManager componentManager)
    {
        // No components of its own -- see class doc comment.
    }

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
        // No systems of its own -- see class doc comment.
    }
}
