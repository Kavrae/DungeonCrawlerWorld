using Engine.ECS.Components;
using Engine.ECS.Systems;
using Game.Modules.Actions.Definitions.DirectActions;
using Game.Modules.Actions.Definitions.Spells;

namespace Game.Modules.Actions.Definitions;

/// <summary>
/// Registers the first real, permanent action catalog -- race/class-agnostic actions any entity
/// can be granted. A flat catalog rather than one module per tag, since tags are multi-valued
/// (e.g. Punch is Melee+Unarmed+Attack, with no single tag-module it would belong to) -- see
/// PlayerBlueprint and the race blueprints for where these are granted. Mirrors AchievementModule's
/// static Definitions list, one file per action under Spells/DirectActions (organized by
/// ActionActivator kind).
/// </summary>
public sealed class CoreActionsModule : IGameModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000017");

    public IReadOnlyList<Type> Dependencies { get; } = [];

    private static readonly IReadOnlyList<Func<ActionDefinition>> Definitions = [
        HealAction.Build,
        PunchAction.Build,
        MagicMissileAction.Build,
        ToxicStrikeAction.Build,
    ];

    public void Configure(GameModuleContext context)
    {
        foreach (var build in Definitions)
        {
            context.Actions.Register(build());
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
