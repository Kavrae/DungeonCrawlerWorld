using Engine.Events;
using Engine.Math;
using Game.Modules.Auras;
using Game.Modules.Auras.Components;
using Game.Modules.Core.Components;
using Game.Terrain;
using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.MapRendering;
using Presentation.Rendering;

namespace Tests.Presentation.MapRendering;

[TestClass]
public sealed class AuraGlowBlendTests
{
    private const byte OrangeAuraId = 0;
    private const byte RedAuraId = 1;
    private const byte GreenAuraId = 2;

    private static readonly Guid OrangeAuraGuid = new("00000000-0000-0000-0000-0000000000b1");
    private static readonly AuraDefinition OrangeAura = new(OrangeAuraGuid, "Orange", Color.Orange);
    private static readonly Vector3Int MapSize = new(100, 100, 1);
    private static readonly Vector3Int SourcePosition = new(10, 10, 0);

    private static (AuraField Field, AuraCatalog Auras, AuraGlowBlend Blend) Build(Game.World.World? world = null, TerrainRegistry? terrain = null)
    {
        var auras = new AuraCatalog();
        auras.Register(OrangeAura);
        auras.Register(new AuraDefinition(new Guid("00000000-0000-0000-0000-0000000000b3"), "Red", Color.Red));
        auras.Register(new AuraDefinition(new Guid("00000000-0000-0000-0000-0000000000b4"), "Green", Color.Green));
        var field = new AuraField(world ?? TestWorlds.Create(new Map(MapSize)), terrain ?? new TerrainRegistry(), auras, new EventBus());
        return (field, auras, new AuraGlowBlend(new AuraPowerView(field, auras)));
    }

    [TestMethod]
    public void OneAura_GlowsItsColourAtItsPower()
    {
        var (field, _, blend) = Build();
        field.AddSource(SourcePosition, new AuraSourceComponent(OrangeAuraId, power: 8, size: 3));

        blend.BeginPass();

        Assert.IsTrue(blend.TryGetGlow(SourcePosition.X, SourcePosition.Y, SourcePosition.Z, out var glowColor, out var totalPower));
        Assert.AreEqual(Color.Orange, glowColor);
        Assert.AreEqual(8, totalPower);
    }

    [TestMethod]
    public void WhereNoAuraReaches_ThereIsNoGlow()
    {
        var (field, _, blend) = Build();
        field.AddSource(SourcePosition, new AuraSourceComponent(OrangeAuraId, power: 8, size: 3));

        blend.BeginPass();

        Assert.IsFalse(blend.TryGetGlow(SourcePosition.X + 50, SourcePosition.Y, SourcePosition.Z, out _, out var totalPower));
        Assert.AreEqual(0, totalPower);
    }

    [TestMethod]
    public void TwoAurasOverlapping_BlendTowardTheStronger_AndTheirPowersAdd()
    {
        var (field, _, blend) = Build();
        field.AddSource(SourcePosition, new AuraSourceComponent(RedAuraId, power: 8, size: 3));
        field.AddSource(new Vector3Int(SourcePosition.X + 2, SourcePosition.Y, SourcePosition.Z), new AuraSourceComponent(GreenAuraId, power: 4, size: 2));

        blend.BeginPass();

        // At the first source: power 8 of Red, 2 of Green.
        Assert.IsTrue(blend.TryGetGlow(SourcePosition.X, SourcePosition.Y, SourcePosition.Z, out var glowColor, out var totalPower));
        Assert.AreEqual(new Color((byte)(255 * 8 / 10f), (byte)(128 * 2 / 10f), (byte)0), glowColor);
        Assert.AreEqual(10, totalPower);
    }

    [TestMethod]
    public void AuraDefinitionReplaced_TheNextPassTakesItsNewColour()
    {
        var (field, auras, blend) = Build();
        field.AddSource(SourcePosition, new AuraSourceComponent(OrangeAuraId, power: 8, size: 3));
        blend.BeginPass();

        auras.Register(new AuraDefinition(OrangeAuraGuid, "Orange", Color.Purple));
        blend.BeginPass();

        Assert.IsTrue(blend.TryGetGlow(SourcePosition.X, SourcePosition.Y, SourcePosition.Z, out var glowColor, out _));
        Assert.AreEqual(Color.Purple, glowColor);
    }

    /// <summary>Terrain auras laid down at population glow from the first frame, before any system has built the field.</summary>
    [TestMethod]
    public void BeforeAnythingBuiltTheField_GlowingTerrainGlows()
    {
        var terrain = new TerrainRegistry();
        var world = TestWorlds.Create(new Map(MapSize));
        var glowing = terrain.Register(new TerrainDefinition("test:glowing", "Glowing", "", "~", default, Aura: new TerrainAura(OrangeAura, 8, 3)));
        world.PopulateTerrain(SourcePosition.X, SourcePosition.Y, TerrainLayer.UnderGround, new TerrainCell(glowing));
        var (_, _, blend) = Build(world, terrain);

        blend.BeginPass();

        Assert.IsTrue(blend.TryGetGlow(SourcePosition.X, SourcePosition.Y, SourcePosition.Z, out var glowColor, out var totalPower));
        Assert.AreEqual(Color.Orange, glowColor);
        Assert.AreEqual(8, totalPower);
    }
}
