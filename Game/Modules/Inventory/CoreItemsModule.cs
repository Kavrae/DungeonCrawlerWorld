using Engine.Modules;
using Game.Modules.Auras;
using Game.Modules.Inventory.Definitions;
using Game.Modules.Poison;

namespace Game.Modules.Inventory;

/// <summary>
/// Registers the first real, permanent item catalog -- race/class-agnostic items any entity can
/// carry. See PlayerKit for where these are granted. Mirrors CoreActionsModule/
/// AchievementModule's static Definitions list, one file per item under Definitions/.
/// </summary>
public sealed class CoreItemsModule : IGameModule
{
    public static readonly Guid ModuleId = new("d9f6a1c4-8b2e-4f3a-9c1d-00000000001a");

    public Guid Id => ModuleId;

    public IReadOnlyList<Guid> Requires { get; } = [AurasModule.ModuleId, PoisonModule.ModuleId];

    private static readonly IReadOnlyList<Func<ItemDefinition>> Definitions = [
        HealthPotion.Build,
        CurePoisonPotion.Build,
        ManaPotion.Build,
        HotkeyExpansionPotion.Build,
        DamagePotion.Build,
        ToxicPotion.Build,
        ToxicIdol.Build,
        ScrollOfHealing.Build,
        ScrollOfTorch.Build,
        WandOfFireball.Build,
        ImmunityTestPotion.Build,
        ResistanceTestPotion.Build,
    ];

    public void Configure(GameModuleContext context)
    {
        foreach (var build in Definitions)
        {
            context.Items.Register(build());
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
