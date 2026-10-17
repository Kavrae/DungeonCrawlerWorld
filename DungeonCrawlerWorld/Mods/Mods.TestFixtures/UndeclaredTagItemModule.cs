using Engine.Modules;
using Engine.Tags;
using Game.Modules;
using Game.Modules.Inventory;
using Microsoft.Xna.Framework;

namespace Mods.TestFixtures;

/// <summary>Registers an item carrying a gameplay tag no module declares -- a fixture for the dry run's content tag check: a mod like this must be excluded and reported, naming the tag.</summary>
public sealed class UndeclaredTagItemModule : IGameModule
{
    public Guid Id { get; } = new("e6f2a017-4b3d-4a1e-9c72-000000000004");

    public void RegisterComponents(ComponentRegistration registration)
    {
    }

    public void Configure(GameModuleContext context) =>
        context.Items.Register(new ItemDefinition(
            new Guid("e6f2a017-4b3d-4a1e-9c72-000000000005"), "Mistagged Trinket", null, "?", Color.White,
            Tags: [GameplayTag.Get("TestFixtures.NeverDeclared")], Effects: []));

    public void RegisterBehavior(BehaviorRegistration<GameModuleContext> registration) { }
}
