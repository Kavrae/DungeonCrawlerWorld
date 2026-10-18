using Engine.Modules;
using Game.Modules.Actions;
using Game.Modules.Auras;
using Game.Modules.Poison;

namespace Game.Modules.Inventory;

/// <summary>
/// Registers the built-in items -- race/class-agnostic items any entity can carry -- from the master manifest's Items
/// table, in file order. See PlayerKit for where these are granted, by their BuiltInItemIds. A disabled item's
/// replacement is registered as its redirect.
/// </summary>
public sealed class CoreItemsModule : IGameModule
{
    public static readonly Guid ModuleId = new("d9f6a1c4-8b2e-4f3a-9c1d-00000000001a");

    public Guid Id => ModuleId;

    /// <summary>Inventory owns the item codec, Actions the activators', Auras the aura sources items grant, Poison the status effect they apply.</summary>
    public IReadOnlyList<Guid> Requires { get; } = [InventoryModule.ModuleId, ActionsModule.ModuleId, AurasModule.ModuleId, PoisonModule.ModuleId];

    public void Configure(GameModuleContext context)
    {
        var content = context.Content;
        foreach (var item in content.Items)
        {
            context.Items.Register(item);
        }

        foreach (var (from, to) in content.ItemRedirects)
        {
            context.Items.AddRedirect(from, to);
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
