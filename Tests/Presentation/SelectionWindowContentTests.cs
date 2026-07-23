using Engine.Bootstrap;
using Engine.Diagnostics;
using Engine.ECS.World;
using Engine.Events;
using Engine.Math;
using Engine.Modules;
using Game.Blueprints.Objects;
using Game.Blueprints.Terrain;
using Game.Modules;
using Game.Modules.Core;
using Game.Modules.Core.Components;
using Game.Modules.Energy;
using Game.Modules.Health;
using Game.Modules.Movement;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI;
using Presentation.UI.Content;

namespace Tests.Presentation;

[TestClass]
public sealed class SelectionWindowContentTests
{
    private static (EcsContext EcsContext, Game.World.World World, MapViewState MapViewState) BuildEcsContextAndWorld()
    {
        // Z=3 so Wall's blueprint (which places at MapLayer.Ground = 1) actually lands
        // on the map -- PlaceEntityOnMap silently no-ops for off-map positions.
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 3)));
        var mapViewState = new MapViewState();
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

        var ecsContext = Bootstrapper.Build(modules, initialEntityCapacity: 100, initialComponentCapacity: 50);
        world.NonBlockingComponents = ecsContext.ComponentManager.GetMultiPool<NonBlockingComponent>();
        world.ForceBlockingComponents = ecsContext.ComponentManager.GetMultiPool<ForceBlockingComponent>();

        return (ecsContext, world, mapViewState);
    }

    private static int CreateWallEntityAt(EcsContext ecsContext, Game.World.World world, int x, int y)
    {
        var entityId = ecsContext.EntityManager.CreateEntity();
        new Wall().Build(ecsContext.ComponentManager, entityId);

        ref var transform = ref ecsContext.ComponentManager.GetDirectPool<Game.Modules.Core.Components.TransformComponent>().Get(entityId);
        world.PlaceEntityOnMap(entityId, new Vector3Int(x, y, transform.Position.Z), ref transform);

        return entityId;
    }

    /// <summary>
    /// A Tiny entity, built like CreateWallEntityAt but with a Tiny OccupancyComponent (for
    /// rendering) and a NonBlockingComponent (for collision) both added first -- since
    /// World.NonBlockingComponents is wired up above (matching GameBootstrapper.cs),
    /// PlaceEntityOnMap will skip writing it into Map's Blocking slot entirely (see
    /// World.IsBlocking), so it's only findable by position, not by Map.GetEntityId.
    /// </summary>
    private static int CreateTinyEntityAt(EcsContext ecsContext, Game.World.World world, int x, int y)
    {
        var entityId = ecsContext.EntityManager.CreateEntity();
        new Wall().Build(ecsContext.ComponentManager, entityId);
        ecsContext.ComponentManager.GetPackedPool<OccupancyComponent>().Add(entityId, new OccupancyComponent(isTiny: true, isPhasing: false));
        ecsContext.ComponentManager.GetMultiPool<NonBlockingComponent>().Add(entityId, new NonBlockingComponent());

        ref var transform = ref ecsContext.ComponentManager.GetDirectPool<Game.Modules.Core.Components.TransformComponent>().Get(entityId);
        world.PlaceEntityOnMap(entityId, new Vector3Int(x, y, transform.Position.Z), ref transform);

        return entityId;
    }

    private static int CreateWallEntityAtLayer(EcsContext ecsContext, Game.World.World world, int x, int y, MapLayer mapLayer)
    {
        var entityId = ecsContext.EntityManager.CreateEntity();
        new Wall().Build(ecsContext.ComponentManager, entityId);

        ref var transform = ref ecsContext.ComponentManager.GetDirectPool<Game.Modules.Core.Components.TransformComponent>().Get(entityId);
        world.PlaceEntityOnMap(entityId, new Vector3Int(x, y, (int)mapLayer), ref transform);

        return entityId;
    }

    private static Window CreateHostWindow(WindowService windowService, IWindowContent content)
    {
        var hostWindow = windowService.CreateWindow<Window>(null, new WindowOptions
        {
            Hierarchy = new WindowHierarchyOptions { CanContainChildWindows = true, ChildWindowTileMode = WindowTileMode.Vertical },
            Layout = new WindowLayoutOptions { Size = new Vector2(300, 700), DisplayMode = WindowDisplayMode.Fixed },
            Chrome = new WindowChromeOptions { ShowTitle = true },
        });
        hostWindow.SetContent(content);
        hostWindow.Initialize();
        return hostWindow;
    }

    [TestMethod]
    public void Update_NoSelection_CreatesNoChildWindowsAndSetsDefaultTitle()
    {
        var (ecsContext, world, mapViewState) = BuildEcsContextAndWorld();
        var fontService = new FontService("Fonts");
        var windowService = new WindowService(fontService, new GlyphRenderer());
        var componentInspector = new ComponentInspector(ecsContext.ComponentManager);
        var hostWindow = CreateHostWindow(windowService, new SelectionWindowContent(world, mapViewState, ecsContext.ComponentManager, componentInspector, windowService));

        hostWindow.Update(new GameTime());

        Assert.IsEmpty(hostWindow.ChildWindows);
        Assert.AreEqual("No map nodes selected", hostWindow.TitleText);
    }

    [TestMethod]
    public void Update_SelectingEntity_CreatesOneChildWindowPerNameAndComponent()
    {
        var (ecsContext, world, mapViewState) = BuildEcsContextAndWorld();
        CreateWallEntityAt(ecsContext, world, 2, 2);

        var fontService = new FontService("Fonts");
        var windowService = new WindowService(fontService, new GlyphRenderer());
        var componentInspector = new ComponentInspector(ecsContext.ComponentManager);
        var hostWindow = CreateHostWindow(windowService, new SelectionWindowContent(world, mapViewState, ecsContext.ComponentManager, componentInspector, windowService));

        mapViewState.SelectedMapNodePosition = new Point(2, 2);
        hostWindow.Update(new GameTime());

        // One name window (Wall has a DisplayTextComponent) plus one window per inspected
        // component (DisplayText, Glyph, Transform -- everything Wall.Build sets).
        Assert.HasCount(4, hostWindow.ChildWindows);
        Assert.AreEqual("Selected Map Node : 2,2", hostWindow.TitleText);
    }

    [TestMethod]
    public void Update_DeselectingEntity_RemovesItsChildWindows()
    {
        var (ecsContext, world, mapViewState) = BuildEcsContextAndWorld();
        CreateWallEntityAt(ecsContext, world, 2, 2);

        var fontService = new FontService("Fonts");
        var windowService = new WindowService(fontService, new GlyphRenderer());
        var componentInspector = new ComponentInspector(ecsContext.ComponentManager);
        var hostWindow = CreateHostWindow(windowService, new SelectionWindowContent(world, mapViewState, ecsContext.ComponentManager, componentInspector, windowService));

        mapViewState.SelectedMapNodePosition = new Point(2, 2);
        hostWindow.Update(new GameTime());
        Assert.AreNotEqual(0, hostWindow.ChildWindows.Count);

        mapViewState.SelectedMapNodePosition = null;
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
        var (ecsContext, world, mapViewState) = BuildEcsContextAndWorld();
        var fontService = new FontService("Fonts");
        var windowService = new WindowService(fontService, new GlyphRenderer());
        var componentInspector = new ComponentInspector(ecsContext.ComponentManager);
        var hostWindow = CreateHostWindow(windowService, new SelectionWindowContent(world, mapViewState, ecsContext.ComponentManager, componentInspector, windowService));

        mapViewState.SelectedMapNodePosition = new Point(999, 999);

        hostWindow.Update(new GameTime());

        Assert.IsEmpty(hostWindow.ChildWindows);
    }

    /// <summary>
    /// Regression coverage for the gap Occupancy introduced: a Tiny/Phasing entity never
    /// occupies Map's Blocking slot (see World.IsBlocking), so RecomputeSelectedEntityIds'
    /// per-layer Map scan alone would silently drop it from the debug panel. It must still
    /// show up via the separate Occupancy-pool cross-check.
    /// </summary>
    [TestMethod]
    public void Update_SelectingTinyEntity_StillCreatesItsChildWindows()
    {
        var (ecsContext, world, mapViewState) = BuildEcsContextAndWorld();
        CreateTinyEntityAt(ecsContext, world, 2, 2);

        // Confirms the Tiny entity really isn't in Map's slot -- if this ever fails, the
        // test below would pass for the wrong reason.
        Assert.AreEqual(-1, world.Map.GetEntityId(new Vector3Int(2, 2, (int)MapLayer.Ground)));

        var fontService = new FontService("Fonts");
        var windowService = new WindowService(fontService, new GlyphRenderer());
        var componentInspector = new ComponentInspector(ecsContext.ComponentManager);
        var hostWindow = CreateHostWindow(windowService, new SelectionWindowContent(world, mapViewState, ecsContext.ComponentManager, componentInspector, windowService));

        mapViewState.SelectedMapNodePosition = new Point(2, 2);
        hostWindow.Update(new GameTime());

        // Same shape as Update_SelectingEntity_CreatesOneChildWindowPerNameAndComponent:
        // one name window plus one per inspected component (DisplayText, Glyph, Transform,
        // Occupancy, NonBlocking).
        Assert.HasCount(6, hostWindow.ChildWindows);
    }

    /// <summary>
    /// Terrain is never a Blocking creature-occupancy entity (see World.PlaceTerrainOnMap),
    /// so it lives entirely outside Map's per-layer creature slot RecomputeSelectedEntityIds
    /// otherwise scans -- it has to be looked up independently, or selecting a mapNode would
    /// never show what's actually under an entity's feet.
    /// </summary>
    [TestMethod]
    public void Update_SelectingMapNode_ShowsTerrainAtCurrentLayer()
    {
        var (ecsContext, world, mapViewState) = BuildEcsContextAndWorld();
        var terrainId = ecsContext.EntityManager.CreateEntity();
        new StoneFloor().Build(ecsContext.ComponentManager, terrainId);
        world.PlaceTerrainOnMap(terrainId, 2, 2, TerrainLayer.Ground);

        var fontService = new FontService("Fonts");
        var windowService = new WindowService(fontService, new GlyphRenderer());
        var componentInspector = new ComponentInspector(ecsContext.ComponentManager);
        var hostWindow = CreateHostWindow(windowService, new SelectionWindowContent(world, mapViewState, ecsContext.ComponentManager, componentInspector, windowService));

        mapViewState.SelectedMapNodePosition = new Point(2, 2);
        hostWindow.Update(new GameTime());

        // One name window plus one per inspected component (DisplayText, Background,
        // Transform -- everything StoneFloor.Build sets).
        Assert.HasCount(4, hostWindow.ChildWindows);
    }

    /// <summary>
    /// Two Walls at the same XY but different MapLayers (which never collide -- each layer
    /// has its own independent Map slot) must not both show up at once: the inspector is
    /// scoped to MapViewState.CurrentMapLayer, the same single layer MapWindow is rendering, and
    /// switching layers swaps which one is shown.
    /// </summary>
    [TestMethod]
    public void Update_SelectingMapNode_OnlyShowsEntitiesOnCurrentLayer()
    {
        var (ecsContext, world, mapViewState) = BuildEcsContextAndWorld();
        CreateWallEntityAt(ecsContext, world, 2, 2); // Ground -- matches MapViewState.CurrentMapLayer's default.
        CreateWallEntityAtLayer(ecsContext, world, 2, 2, MapLayer.Flying);

        var fontService = new FontService("Fonts");
        var windowService = new WindowService(fontService, new GlyphRenderer());
        var componentInspector = new ComponentInspector(ecsContext.ComponentManager);
        var hostWindow = CreateHostWindow(windowService, new SelectionWindowContent(world, mapViewState, ecsContext.ComponentManager, componentInspector, windowService));

        mapViewState.SelectedMapNodePosition = new Point(2, 2);
        hostWindow.Update(new GameTime());

        // Only the Ground-layer Wall's windows, same count as
        // Update_SelectingEntity_CreatesOneChildWindowPerNameAndComponent, even though a
        // second Wall exists at the same XY on the Flying layer.
        Assert.HasCount(4, hostWindow.ChildWindows);

        mapViewState.CurrentMapLayer = (int)MapLayer.Flying;
        hostWindow.Update(new GameTime());

        // Switching layers swaps which Wall is inspected -- still 4 windows, now the Flying one's.
        Assert.HasCount(4, hostWindow.ChildWindows);
    }

    /// <summary>
    /// Regression test for the reported bug: when an entity's total component text exceeds
    /// hostWindow's own (Fixed, non-growing) height, each child window used to clamp its own
    /// content size down (see TextWindowSizingTests for the negative-height clamp that used to
    /// make this go further and overlap the *next* child too). The fix is at the parent level,
    /// not per-child: children now render their full, unclamped natural height (see
    /// SelectionWindowContent's UnboundedChildHeight), and hostWindow itself becomes the
    /// scrollable one -- CanUserScrollVertical on hostWindow (see GameShellBootstrapper's
    /// selectionWindow) turns the overflow into a real, positive MaxScrollOffset on hostWindow
    /// instead of clipping/overlapping individual children.
    /// </summary>
    [TestMethod]
    public void Update_EntityComponentsExceedHostWindowHeight_HostWindowBecomesScrollableAndChildrenStayFullHeight()
    {
        var (ecsContext, world, mapViewState) = BuildEcsContextAndWorld();
        CreateWallEntityAt(ecsContext, world, 2, 2);

        var fontService = new FontService("Fonts");
        var windowService = new WindowService(fontService, new GlyphRenderer());
        var componentInspector = new ComponentInspector(ecsContext.ComponentManager);
        // Deliberately much shorter than CreateHostWindow's usual 700px -- forces the 4
        // component windows to exceed the host's own height, the same way a goblin engineer's
        // longer component text did in the original report.
        var hostWindow = windowService.CreateWindow<Window>(null, new WindowOptions
        {
            Hierarchy = new WindowHierarchyOptions { CanContainChildWindows = true, ChildWindowTileMode = WindowTileMode.Vertical },
            Layout = new WindowLayoutOptions { Size = new Vector2(300, 40), DisplayMode = WindowDisplayMode.Fixed },
            Chrome = new WindowChromeOptions { ShowTitle = true, CanUserScrollVertical = true },
        });
        hostWindow.SetContent(new SelectionWindowContent(world, mapViewState, ecsContext.ComponentManager, componentInspector, windowService));
        hostWindow.Initialize();

        mapViewState.SelectedMapNodePosition = new Point(2, 2);
        hostWindow.Update(new GameTime());

        Assert.HasCount(4, hostWindow.ChildWindows);

        // hostWindow, not any individual child, is the one that ends up scrollable.
        Assert.IsTrue(hostWindow.CanUserScrollVertical);
        Assert.IsGreaterThan(0, hostWindow.MaxScrollOffset.Y);

        // No child should have been clamped down to fit hostWindow's tiny 40px content height --
        // every child keeps its own full, positive natural height instead of getting collapsed.
        foreach (var child in hostWindow.ChildWindows)
        {
            Assert.IsGreaterThan(0, child.WindowCurrentSize.Y);
        }
    }
}
