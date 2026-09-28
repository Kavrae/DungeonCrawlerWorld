using Engine.ECS.Entities;
using Engine.ECS.Context;
using Engine.Events;
using Engine.Math;
using Engine.Modules;
using Game.Modules;
using Game.Modules.Core;
using Game.Modules.Health;
using Game.Modules.Movement;
using Game.Modules.Movement.Components;
using Game.Modules.ProcessingTier;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI;
using Presentation.UI.Content;

namespace Tests.Presentation;

[TestClass]
[DoNotParallelize]
public sealed class DebugWindowContentTests
{
    private static EcsContext BuildEcsContext() =>
        BuiltInTestModules.Build(new Map(new Vector3Int(5, 5, 1)), initialEntityCapacity: 100, initialComponentCapacity: 50).EcsContext;

    [TestMethod]
    public void Update_WithLivingAndMovingEntities_DoesNotThrow()
    {
        var ecsContext = BuildEcsContext();
        var entityId = ecsContext.EntityManager.CreateEntity();
        ecsContext.ComponentManager.GetPackedPool<MovementComponent>().Add(entityId, new MovementComponent(MovementMode.Random, null, null));

        var fontService = TestFonts.Shared;
        var windowService = TestElementPoolServiceFactory.Create(fontService, new LabelRenderer());
        var hostWindow = windowService.CreateElement<Window>(null, new ElementOptions());
        hostWindow.SetContent(new DebugWindowContent(fontService, ecsContext.EntityManager, ecsContext.ComponentManager, diagnostics: null));

        hostWindow.Initialize();
        hostWindow.Update(new GameTime());
        hostWindow.Update(new GameTime());
    }

    [TestMethod]
    public void Update_WithNoEntities_DoesNotThrow()
    {
        var ecsContext = BuildEcsContext();

        var fontService = TestFonts.Shared;
        var windowService = TestElementPoolServiceFactory.Create(fontService, new LabelRenderer());
        var hostWindow = windowService.CreateElement<Window>(null, new ElementOptions());
        hostWindow.SetContent(new DebugWindowContent(fontService, ecsContext.EntityManager, ecsContext.ComponentManager, diagnostics: null));

        hostWindow.Initialize();
        hostWindow.Update(new GameTime());
    }
}