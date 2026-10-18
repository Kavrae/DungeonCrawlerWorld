using Engine.Modules;
using Game.Modules.Auras;
using Game.Modules.Poison;

namespace Game.Modules.Actions.Definitions;

/// <summary>
/// Registers the built-in actions -- race/class-agnostic actions any entity can be granted -- from the master manifest's
/// Actions table, in file order. A flat catalog rather than one module per tag, since tags are multi-valued (e.g. Punch
/// is Melee+Unarmed+Attack, with no single tag-module it would belong to) -- see PlayerKit and the race blueprints for
/// where these are granted, by their BuiltInActionIds. A disabled action's replacement is registered as its redirect.
/// </summary>
public sealed class CoreActionsModule : IGameModule
{
    public static readonly Guid ModuleId = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000017");

    public Guid Id => ModuleId;

    /// <summary>Actions owns the codecs the table is read through, Auras the aura sources it grants, Poison the status effect it applies.</summary>
    public IReadOnlyList<Guid> Requires { get; } = [ActionsModule.ModuleId, AurasModule.ModuleId, PoisonModule.ModuleId];

    public void Configure(GameModuleContext context)
    {
        var content = context.Content;
        foreach (var action in content.Actions)
        {
            context.Actions.Register(action);
        }

        foreach (var (from, to) in content.ActionRedirects)
        {
            context.Actions.AddRedirect(from, to);
        }
    }

    public void RegisterComponents(ComponentRegistration registration)
    {
        // No components of its own -- see class doc comment.
    }

    public void RegisterBehavior(BehaviorRegistration<GameModuleContext> registration)
    {
        // No systems of its own -- see class doc comment.
    }
}
