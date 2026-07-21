using Engine.Bootstrap;
using Engine.ECS.World;
using Engine.Events;
using Engine.Math;
using Engine.Modules;
using Game.Modules;
using Game.Modules.Core;
using Game.Modules.Energy;
using Game.Modules.Health;
using Game.Modules.Movement;
using Game.Modules.Movement.Components;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI;
using Presentation.UI.Content;

namespace Tests.Presentation;

[TestClass]
public sealed class DebugWindowContentTests
{
    private static EcsContext BuildEcsContext()
    {
        var world = new Game.World.World(new Map(new Vector3Int(5, 5, 1)));
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

        return Bootstrapper.Build(modules, initialEntityCapacity: 100, initialComponentCapacity: 50);
    }

    [TestMethod]
    public void Update_WithLivingAndMovingEntities_DoesNotThrow()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();
        ecsContext.ComponentManager.GetPackedPool<MovementComponent>().Add(entityId, new MovementComponent(MovementMode.Random, 10, null, null));

        var fontService = new FontService("Fonts");
        var windowService = new WindowService(fontService, new GlyphRenderer());
        var hostWindow = windowService.CreateWindow<Window>(null, new WindowOptions());
        hostWindow.SetContent(new DebugWindowContent(fontService, ecsContext.EntityManager, ecsContext.ComponentManager));

        hostWindow.Initialize();
        hostWindow.Update(new GameTime());
        hostWindow.Update(new GameTime());
    }

    [TestMethod]
    public void Update_WithNoEntities_DoesNotThrow()
    {
        var ecsContext = BuildEcsContext();

        var fontService = new FontService("Fonts");
        var windowService = new WindowService(fontService, new GlyphRenderer());
        var hostWindow = windowService.CreateWindow<Window>(null, new WindowOptions());
        hostWindow.SetContent(new DebugWindowContent(fontService, ecsContext.EntityManager, ecsContext.ComponentManager));

        hostWindow.Initialize();
        hostWindow.Update(new GameTime());
    }
}
