using Engine.Modules;
using Game.Modules;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Inventory;

namespace Tests.Modules.Inventory;

/// <summary>A build refuses a cooldown on an item used up when it is used: the cooldown belongs to a unit that is gone.</summary>
[TestClass]
public sealed class ItemCooldownContentValidationTests
{
    private sealed class RegisteringModule(Action<GameModuleContext> configure) : IGameModule
    {
        public void RegisterComponents(ComponentRegistration registration)
        {
        }

        public void Configure(GameModuleContext context) => configure(context);

        public void RegisterBehavior(BehaviorRegistration<GameModuleContext> registration)
        {
        }
    }

    private static void BuildWith(ItemDefinition item) =>
        BuiltInTestModules.BuildModules([new RegisteringModule(context => context.Items.Register(item))]);

    [TestMethod]
    public void APotionDeclaringACooldown_ThrowsNamingTheItem()
    {
        var potion = TestContent.Item(BuiltInItemIds.HealthPotion);
        var potionActivator = (PotionActivator)potion.Activator!;
        var potionWithCooldown = potion with { Id = Guid.NewGuid(), Name = "Hasty Draught", Activator = potionActivator with { Timing = potionActivator.Timing with { CooldownFrames = 60 } } };

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => BuildWith(potionWithCooldown));

        Assert.Contains("Item 'Hasty Draught'", exception.Message);
        Assert.Contains("cooldown", exception.Message);
    }

    [TestMethod]
    public void AToggleItemDeclaringACooldown_Builds()
    {
        var idol = TestContent.Item(BuiltInItemIds.ToxicIdol);
        var idolWithCooldown = idol with { Id = Guid.NewGuid(), Activator = new ToggleItemActivator(idol.Activator!.Timing with { CooldownFrames = 60 }) };

        BuildWith(idolWithCooldown);
    }
}
