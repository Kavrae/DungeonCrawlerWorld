using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Modules.Mana.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.Rendering;
using Presentation.UI;
using Presentation.UI.Content;

namespace Tests.Presentation;

[TestClass]
[DoNotParallelize]
public sealed class PlayerManaBarContentTests
{
    private const int PlayerEntityId = 1;

    private static (PlayerManaBarContent Content, ComponentManager ComponentManager) Build(ManaComponent? mana)
    {
        var world = TestWorlds.Create(new Game.World.Map(new Vector3Int(20, 20, 1)), playerEntityId: PlayerEntityId);
        var fontService = TestFonts.Shared;
        var windowService = TestElementPoolServiceFactory.Create(fontService, new LabelRenderer());

        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(20, 10));
        if (mana is { } playerMana)
        {
            componentManager.Merge(PlayerEntityId, playerMana);
        }

        var content = new PlayerManaBarContent(world, componentManager, fontService);
        var hostWindow = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions
            {
                RelativePosition = new Vector2(1700, 60),
                Size = PlayerManaBarContent.Size,
                DisplayMode = ElementDisplayMode.Fixed,
            },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = false, CanUserFocus = false },
        });
        hostWindow.SetContent(content);
        hostWindow.Initialize();

        return (content, componentManager);
    }

    [TestMethod]
    public void Update_FractionalCurrentMana_RoundsDown()
    {
        var (content, _) = Build(new ManaComponent(2.7f, 5f));

        content.Update(new GameTime());

        Assert.AreEqual("2 / 5", content.ValueText);
    }

    [TestMethod]
    public void Update_MaximumManaModifier_ChangesDisplayedMaximum()
    {
        var (content, componentManager) = Build(new ManaComponent(4f, 5f));
        componentManager.GetMultiPool<StatModifierComponent>().Add(PlayerEntityId, new StatModifierComponent(StatModifierTarget.MaximumMana, StatModifierOperation.Additive, StatModifierPolarity.Buff,
            canModify: true, magnitude: 3f, expiresAtFrame: FrameDeadline.Never, ActionSource.Admin));

        content.Update(new GameTime());

        Assert.AreEqual("4 / 8", content.ValueText);
    }

    [TestMethod]
    public void Update_NoManaComponent_NoValueText()
    {
        var (content, _) = Build(null);

        content.Update(new GameTime());

        Assert.IsNull(content.ValueText);
    }
}
