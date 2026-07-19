using Engine.Bootstrap;
using Engine.Diagnostics;
using Engine.ECS.World;
using Engine.Events;
using Engine.Math;
using Engine.Modules;
using Game.Blueprints.Objects;
using Game.Modules;
using Game.Modules.Core;
using Game.Modules.Energy;
using Game.Modules.Health;
using Game.Modules.Movement;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.UI;
using Presentation.UI.Content;

namespace Tests.Presentation;

[TestClass]
public sealed class SelectionWindowContentTests
{
    private static (EcsContext EcsContext, Game.World.World World) BuildEcsContextAndWorld()
    {
        // Z=6 so Wall's blueprint (which places at MapHeight.Standing = 2) actually lands
        // on the map -- PlaceEntityOnMap silently no-ops for off-map positions.
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 6)));
        var mathUtility = new MathUtility();

        var movementModule = new MovementModule();
        movementModule.Configure(new GameModuleContext(world, mathUtility, new EventBus()));

        IReadOnlyList<IModule> modules =
        [
            new CoreModule(),
            new EnergyModule(),
            new HealthModule(),
            movementModule,
        ];

        return (Bootstrapper.Build(modules, initialEntityCapacity: 100, initialComponentCapacity: 50), world);
    }

    private static int CreateWallEntityAt(EcsContext ecsContext, Game.World.World world, int x, int y)
    {
        var entityId = ecsContext.EntityManager.CreateEntity();
        new Wall().Build(ecsContext.ComponentManager, entityId);

        ref var transform = ref ecsContext.ComponentManager.GetDirectPool<Game.Modules.Core.Components.TransformComponent>().Get(entityId);
        world.PlaceEntityOnMap(entityId, new Vector3Int(x, y, transform.Position.Z), ref transform);

        return entityId;
    }

    private static Window CreateHostWindow(WindowService windowService, IWindowContent content)
    {
        var hostWindow = windowService.CreateWindow<Window>(null, new WindowOptions
        {
            Hierarchy = new WindowHierarchyOptions { CanContainChildWindows = true, ChildWindowTileMode = WindowTileMode.Vertical },
            Layout = new WindowLayoutOptions { Size = new Vector2(300, 700), DisplayMode = WindowDisplayMode.Static },
            Chrome = new WindowChromeOptions { ShowTitle = true },
        });
        hostWindow.SetContent(content);
        hostWindow.Initialize();
        return hostWindow;
    }

    [TestMethod]
    public void Update_NoSelection_CreatesNoChildWindowsAndSetsDefaultTitle()
    {
        var (ecsContext, world) = BuildEcsContextAndWorld();
        var fontService = new FontService("Fonts");
        var windowService = new WindowService(fontService);
        var componentInspector = new ComponentInspector(ecsContext.ComponentManager);
        var hostWindow = CreateHostWindow(windowService, new SelectionWindowContent(world, ecsContext.ComponentManager, componentInspector, windowService));

        hostWindow.Update(new GameTime());

        Assert.IsEmpty(hostWindow.ChildWindows);
        Assert.AreEqual("No map nodes selected", hostWindow.TitleText);
    }

    [TestMethod]
    public void Update_SelectingEntity_CreatesOneChildWindowPerNameAndComponent()
    {
        var (ecsContext, world) = BuildEcsContextAndWorld();
        CreateWallEntityAt(ecsContext, world, 2, 2);

        var fontService = new FontService("Fonts");
        var windowService = new WindowService(fontService);
        var componentInspector = new ComponentInspector(ecsContext.ComponentManager);
        var hostWindow = CreateHostWindow(windowService, new SelectionWindowContent(world, ecsContext.ComponentManager, componentInspector, windowService));

        world.SelectedMapNodePosition = new Point(2, 2);
        hostWindow.Update(new GameTime());

        // One name window (Wall has a DisplayTextComponent) plus one window per inspected
        // component (DisplayText, Glyph, Transform -- everything Wall.Build sets).
        Assert.HasCount(4, hostWindow.ChildWindows);
        Assert.AreEqual("Selected Map Node : 2,2", hostWindow.TitleText);
    }

    [TestMethod]
    public void Update_DeselectingEntity_RemovesItsChildWindows()
    {
        var (ecsContext, world) = BuildEcsContextAndWorld();
        CreateWallEntityAt(ecsContext, world, 2, 2);

        var fontService = new FontService("Fonts");
        var windowService = new WindowService(fontService);
        var componentInspector = new ComponentInspector(ecsContext.ComponentManager);
        var hostWindow = CreateHostWindow(windowService, new SelectionWindowContent(world, ecsContext.ComponentManager, componentInspector, windowService));

        world.SelectedMapNodePosition = new Point(2, 2);
        hostWindow.Update(new GameTime());
        Assert.AreNotEqual(0, hostWindow.ChildWindows.Count);

        world.SelectedMapNodePosition = null;
        hostWindow.Update(new GameTime());

        Assert.IsEmpty(hostWindow.ChildWindows);
        Assert.AreEqual("No map nodes selected", hostWindow.TitleText);
    }

    /// <summary>
    /// Regression test: SelectedMapNodePosition is a plain settable property, so this
    /// guards SelectionWindowContent itself against an out-of-bounds value regardless of
    /// what set it -- MapWindow.SelectMapNodes is the current, now-fixed source of the bug
    /// report (a click within the visible viewport but past the map's real edge), but this
    /// test bypasses MapWindow entirely to isolate SelectionWindowContent's own guard.
    /// </summary>
    [TestMethod]
    public void Update_SelectedMapNodePositionOffMap_DoesNotThrow()
    {
        var (ecsContext, world) = BuildEcsContextAndWorld();
        var fontService = new FontService("Fonts");
        var windowService = new WindowService(fontService);
        var componentInspector = new ComponentInspector(ecsContext.ComponentManager);
        var hostWindow = CreateHostWindow(windowService, new SelectionWindowContent(world, ecsContext.ComponentManager, componentInspector, windowService));

        world.SelectedMapNodePosition = new Point(999, 999);

        hostWindow.Update(new GameTime());

        Assert.IsEmpty(hostWindow.ChildWindows);
    }
}
