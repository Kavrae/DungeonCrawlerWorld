using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Events;
using Engine.Math;
using Game.Modules.Core.Components;
using Game.Modules.StatusEffectAura;
using Game.Modules.StatusEffectAura.Components;
using Game.Modules.StatusEffects;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.UI;

namespace Tests.Presentation;

[TestClass]
public sealed class MapTintGridTests
{
    private const int SourceEntityId = 1;
    private static readonly Vector3Int MapSize = new(100, 100, 1);
    private static readonly Vector3Int SourcePosition = new(10, 10, 0);
    private static readonly Vector2Byte UnitSize = new(1, 1);

    private static (ComponentManager ComponentManager, EventBus EventBus) BuildComponentManager()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 10);
        componentManager.RegisterDirectPool<TransformComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterMultiPool<StatusEffectAuraSourceComponent>();
        return (componentManager, new EventBus());
    }

    /// <summary>Regression: matches the constructor-time full scan this class used to do directly -- a source placed before construction (the real terrain-population case) must show up exactly as it always has.</summary>
    [TestMethod]
    public void Constructor_SourcePlacedBeforeConstruction_ScattersItImmediately()
    {
        var (componentManager, eventBus) = BuildComponentManager();
        componentManager.Merge(SourceEntityId, new TransformComponent(SourcePosition, UnitSize));
        componentManager.GetMultiPool<StatusEffectAuraSourceComponent>().Add(SourceEntityId, new StatusEffectAuraSourceComponent(StatusEffectType.Burning, auraAndGlowStrength: 8, Color.Orange));

        var tintGrid = new MapTintGrid(componentManager, MapSize, eventBus);

        Assert.IsTrue(tintGrid.TryGetTint(SourcePosition.X, SourcePosition.Y, SourcePosition.Z, out var tint));
        Assert.AreEqual(Color.Orange, tint.Color);
        Assert.AreEqual(1f, tint.Factor);
    }

    [TestMethod]
    public void TryGetTint_OutsideAnySourceRadius_ReturnsFalse()
    {
        var (componentManager, eventBus) = BuildComponentManager();
        componentManager.Merge(SourceEntityId, new TransformComponent(SourcePosition, UnitSize));
        componentManager.GetMultiPool<StatusEffectAuraSourceComponent>().Add(SourceEntityId, new StatusEffectAuraSourceComponent(StatusEffectType.Burning, auraAndGlowStrength: 8, Color.Orange));

        var tintGrid = new MapTintGrid(componentManager, MapSize, eventBus);

        var farAway = new Vector3Int(SourcePosition.X + 50, SourcePosition.Y, SourcePosition.Z);
        Assert.IsFalse(tintGrid.TryGetTint(farAway.X, farAway.Y, farAway.Z, out _));
    }

    /// <summary>The bug this class's own reactive AddSource/RemoveSource exists to fix: a source that appears after construction (see AuraSourceEffects.Toggle) must show up here too, not just in the gameplay grid.</summary>
    [TestMethod]
    public void SourceAddedAfterConstruction_ReflectedInTryGetTint()
    {
        var (componentManager, eventBus) = BuildComponentManager();
        var tintGrid = new MapTintGrid(componentManager, MapSize, eventBus);
        Assert.IsFalse(tintGrid.TryGetTint(SourcePosition.X, SourcePosition.Y, SourcePosition.Z, out _));

        componentManager.Merge(SourceEntityId, new TransformComponent(SourcePosition, UnitSize));
        var source = new StatusEffectAuraSourceComponent(StatusEffectType.Poison, auraAndGlowStrength: 8, Color.DarkGreen);
        eventBus.Publish(new AuraSourceAddedEvent(SourceEntityId, source));

        Assert.IsTrue(tintGrid.TryGetTint(SourcePosition.X, SourcePosition.Y, SourcePosition.Z, out var tint));
        Assert.AreEqual(Color.DarkGreen, tint.Color);
        Assert.AreEqual(1f, tint.Factor);
    }

    [TestMethod]
    public void SourceRemovedAfterConstruction_NoLongerReflectedInTryGetTint()
    {
        var (componentManager, eventBus) = BuildComponentManager();
        componentManager.Merge(SourceEntityId, new TransformComponent(SourcePosition, UnitSize));
        var source = new StatusEffectAuraSourceComponent(StatusEffectType.Burning, auraAndGlowStrength: 8, Color.Orange);
        componentManager.GetMultiPool<StatusEffectAuraSourceComponent>().Add(SourceEntityId, source);
        var tintGrid = new MapTintGrid(componentManager, MapSize, eventBus);
        Assert.IsTrue(tintGrid.TryGetTint(SourcePosition.X, SourcePosition.Y, SourcePosition.Z, out _));

        eventBus.Publish(new AuraSourceRemovedEvent(SourceEntityId, source));

        Assert.IsFalse(tintGrid.TryGetTint(SourcePosition.X, SourcePosition.Y, SourcePosition.Z, out _), "No residual entry left behind once the source's own contribution is fully retracted.");
    }

    [TestMethod]
    public void TwoOverlappingSources_ColorsAreFalloffWeightedBlend()
    {
        var (componentManager, eventBus) = BuildComponentManager();
        componentManager.Merge(SourceEntityId, new TransformComponent(SourcePosition, UnitSize));
        componentManager.GetMultiPool<StatusEffectAuraSourceComponent>().Add(SourceEntityId, new StatusEffectAuraSourceComponent(StatusEffectType.Burning, auraAndGlowStrength: 8, Color.Red));

        const int secondSourceEntityId = 2;
        var secondPosition = new Vector3Int(SourcePosition.X + 2, SourcePosition.Y, SourcePosition.Z);
        componentManager.Merge(secondSourceEntityId, new TransformComponent(secondPosition, UnitSize));
        componentManager.GetMultiPool<StatusEffectAuraSourceComponent>().Add(secondSourceEntityId, new StatusEffectAuraSourceComponent(StatusEffectType.Poison, auraAndGlowStrength: 4, Color.Green));

        var tintGrid = new MapTintGrid(componentManager, MapSize, eventBus);

        // At the first source's own position: weight 8 (distance 0) from Red, weight 1 (distance 2, 4 >> 2 == 1) from Green.
        Assert.IsTrue(tintGrid.TryGetTint(SourcePosition.X, SourcePosition.Y, SourcePosition.Z, out var tint));
        var expected = new Color((byte)((255 * 8 + 0 * 1) / 9f), (byte)((0 * 8 + 128 * 1) / 9f), (byte)((0 * 8 + 0 * 1) / 9f));
        Assert.AreEqual(expected, tint.Color);
    }

    /// <summary>The bug report this guards against: a source carried by a moving entity (e.g. the player holding a toggled-on Toxic Idol) must have its glow move with it, not stay fixed at wherever it was when added.</summary>
    [TestMethod]
    public void SourceMoves_TintFollowsToNewPositionAndLeavesOldOne()
    {
        var (componentManager, eventBus) = BuildComponentManager();
        componentManager.Merge(SourceEntityId, new TransformComponent(SourcePosition, UnitSize));
        componentManager.GetMultiPool<StatusEffectAuraSourceComponent>().Add(SourceEntityId, new StatusEffectAuraSourceComponent(StatusEffectType.Burning, auraAndGlowStrength: 8, Color.Orange));
        var tintGrid = new MapTintGrid(componentManager, MapSize, eventBus);
        Assert.IsTrue(tintGrid.TryGetTint(SourcePosition.X, SourcePosition.Y, SourcePosition.Z, out _));

        var newPosition = new Vector3Int(SourcePosition.X + 20, SourcePosition.Y, SourcePosition.Z);
        componentManager.Merge(SourceEntityId, new TransformComponent(newPosition, UnitSize));
        eventBus.Publish(new EntityMovedEvent(SourceEntityId, SourcePosition, newPosition, UnitSize));

        Assert.IsFalse(tintGrid.TryGetTint(SourcePosition.X, SourcePosition.Y, SourcePosition.Z, out _), "Old position must no longer show the tint once the source has moved away.");
        Assert.IsTrue(tintGrid.TryGetTint(newPosition.X, newPosition.Y, newPosition.Z, out var tint));
        Assert.AreEqual(Color.Orange, tint.Color);
    }

    /// <summary>The exact reported regression: toggle on, move, toggle off -- must not leave a permanent ghost tint at the original position (OnSourceRemoved retracting from the entity's then-CURRENT position, which never matched where the glow actually was, since nothing had moved it there).</summary>
    [TestMethod]
    public void SourceTogglesOnThenMovesThenTogglesOff_NoResidualTintAtEitherPosition()
    {
        var (componentManager, eventBus) = BuildComponentManager();
        componentManager.Merge(SourceEntityId, new TransformComponent(SourcePosition, UnitSize));
        var tintGrid = new MapTintGrid(componentManager, MapSize, eventBus);
        var sourcePool = componentManager.GetMultiPool<StatusEffectAuraSourceComponent>();

        AuraSourceEffects.Toggle(sourcePool, eventBus, SourceEntityId, StatusEffectType.Poison, auraAndGlowStrength: 8, Color.DarkGreen);
        Assert.IsTrue(tintGrid.TryGetTint(SourcePosition.X, SourcePosition.Y, SourcePosition.Z, out _));

        var newPosition = new Vector3Int(SourcePosition.X + 20, SourcePosition.Y, SourcePosition.Z);
        componentManager.Merge(SourceEntityId, new TransformComponent(newPosition, UnitSize));
        eventBus.Publish(new EntityMovedEvent(SourceEntityId, SourcePosition, newPosition, UnitSize));
        Assert.IsTrue(tintGrid.TryGetTint(newPosition.X, newPosition.Y, newPosition.Z, out _));

        AuraSourceEffects.Toggle(sourcePool, eventBus, SourceEntityId, StatusEffectType.Poison, auraAndGlowStrength: 8, Color.DarkGreen);

        Assert.IsFalse(tintGrid.TryGetTint(SourcePosition.X, SourcePosition.Y, SourcePosition.Z, out _), "No residual ghost tint at the original toggle-on position.");
        Assert.IsFalse(tintGrid.TryGetTint(newPosition.X, newPosition.Y, newPosition.Z, out _), "New (moved-to) position must also be clear after toggling off.");
    }
}
